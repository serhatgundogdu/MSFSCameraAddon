using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CinematicCam.Camera;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Ui;

/// <summary>Ana ekran: mod seçimi, uçağın preset'leri, önizleme, moda özel ayarlar ve sarsıntı.</summary>
public sealed class CameraPage : IPage
{
    private sealed record ModeInfo(CameraModeKind Kind, string Name, string Desc, string Icon, string HotkeyAction, string Hint);

    private static readonly ModeInfo[] Modes =
    {
        new(CameraModeKind.Chase, "Chase", "Arkadan yaylı takip", U.Icons.Chase, "ModeChase",
            "Uçağı arkadan, gecikmeli ve yaylı takip eder. Hızlanırken geride kalır, dönüşlerde dışa savrulur."),
        new(CameraModeKind.Flyby, "Flyby", "Önde bekler, yanından geçersiniz", U.Icons.Flyby, "ModeFlyby",
            "Kamera rotanızın önüne kurulur. Uçak yanından geçerken onu çevirir ve uzaklaştıkça zoom yapar."),
        new(CameraModeKind.Orbit, "Orbit", "Uçağın etrafında döner", U.Icons.Orbit, "ModeOrbit",
            "Uçağın etrafında sabit hızla döner. Apron ve uzun düz uçuş için ideal."),
        new(CameraModeKind.Inside, "İç kamera", "Kokpit, kabin ve GoPro", U.Icons.Inside, "ModeInside",
            "Kokpit, kabin ve GoPro görünümleri. Pilot gözü noktasına göre çalışır; baş hareketi ve G-kuvveti hissi verir."),
        new(CameraModeKind.Cinematic, "Sinematik", "Planlar arasında otomatik geçiş", U.Icons.Cinematic, "ModeCinematic",
            "Farklı planlar arasında kendiliğinden geçiş yapar. Arkanıza yaslanın ve izleyin."),
    };

    private readonly AppServices _app;
    private readonly Grid _root = new();
    private readonly Grid _connected = new();
    private readonly FrameworkElement _waiting;
    private readonly StackPanel _modeList = new();
    private readonly StackPanel _presetCard = new();
    private readonly Canvas _preview = new() { Width = 360, Height = 240, ClipToBounds = true };
    private readonly TextBlock _tileMode = new(), _tileDetailLabel = new(), _tileDetail = new(), _tileFov = new(), _tileCam = new();
    private readonly TextBlock _hint = U.Note("");
    private readonly StackPanel _actions = new();
    private readonly StackPanel _settingsCard = new();
    private readonly StackPanel _shakeCard = new();
    private bool _advancedOpen;
    private CameraModeKind? _drawnMode;
    private string? _drawnPresetId;

    public CameraPage(AppServices app)
    {
        _app = app;
        _waiting = BuildWaiting();
        BuildConnected();
        _root.Children.Add(_connected);
        _root.Children.Add(_waiting);
        Refresh();
    }

    public FrameworkElement View => _root;

    private AppSettings S => _app.Settings;
    private CameraEngine Engine => _app.Engine;

    // ---------------- Yerleşim ----------------

    private void BuildConnected()
    {
        _connected.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        _connected.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 520 });

        var left = U.Stack(20,
            U.Card(new StackPanel { Children = { U.Overline("Kamera modu"), _modeList } }),
            U.Card(_presetCard));
        left.Margin = new Thickness(0, 0, 20, 0);

        // Önizleme kartı
        var previewBox = new Border
        {
            Background = U.B("Bg"),
            BorderBrush = U.Hex("#222731"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Viewbox { Child = _preview, Stretch = Stretch.Uniform, Height = 230 },
        };
        var tiles = new UniformGrid2(2);
        tiles.Add(U.Tile("Mod", "", valueBlock: _tileMode));
        var detailTile = U.Tile("", "", valueBlock: _tileDetail);
        var detailStack = (StackPanel)detailTile.Child;
        detailStack.Children.RemoveAt(0);
        detailStack.Children.Insert(0, _tileDetailLabel);
        _tileDetailLabel.FontSize = 12;
        _tileDetailLabel.Foreground = U.B("FgDim");
        tiles.Add(detailTile);
        tiles.Add(U.Tile("Görüş açısı", "", valueBlock: _tileFov));
        tiles.Add(U.Tile("Kamera", "", valueBlock: _tileCam));
        _actions.Margin = new Thickness(0, 10, 0, 0);

        var previewGrid = new Grid();
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var previewLeft = new StackPanel
        {
            Margin = new Thickness(0, 0, 20, 0),
            Children = { HeaderWithNote("Kamera nerede?", "üstten görünüm"), previewBox },
        };
        var previewRight = new StackPanel { Children = { U.Overline("Şu an"), tiles.Panel, _hint, _actions } };
        Grid.SetColumn(previewRight, 1);
        previewGrid.Children.Add(previewLeft);
        previewGrid.Children.Add(previewRight);

        var right = U.Stack(20, U.Card(previewGrid), U.Card(_settingsCard, 18), U.Card(_shakeCard, 18));
        Grid.SetColumn(right, 1);

        _connected.Children.Add(left);
        _connected.Children.Add(right);
    }

    private static FrameworkElement HeaderWithNote(string title, string note)
    {
        var o = U.Overline(title);
        o.Inlines.Add(new System.Windows.Documents.Run("  — " + note) { FontWeight = FontWeights.Normal });
        return o;
    }

    private FrameworkElement BuildWaiting()
    {
        var steps = new StackPanel();
        (string, string)[] items =
        {
            ("1", "MSFS 2024'ü açın ve bir uçuş başlatın. Ana menüdeyken bağlantı kurulmaz."),
            ("2", "Eklenti kameralarına izin verin: Seçenekler › Genel › Kamera."),
            ("3", "“Kamerayı Aç”a basın veya Ctrl+Shift+C."),
        };
        foreach (var (n, text) in items)
        {
            var badge = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = U.B(n == "1" ? "Accent" : "Raised"),
                Child = new TextBlock { Text = n, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = U.B(n == "1" ? "AccentInk" : "Fg") },
                VerticalAlignment = VerticalAlignment.Top,
            };
            var t = U.Text(text, 14, wrap: true);
            t.Margin = new Thickness(14, 4, 0, 0);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            row.Children.Add(t);
            steps.Children.Add(row);
        }
        var title = U.Title("MSFS 2024'e bağlanılıyor", 24);
        var note = U.Note("Uygulama simülatörü arka planda arıyor. Bağlantı kurulduğunda bu ekran kendiliğinden kamera paneline dönüşür.", 14);
        note.Margin = new Thickness(0, 8, 0, 28);
        var card = U.Card(new StackPanel { Children = { title, note, steps } }, 36);
        card.MaxWidth = 640;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Margin = new Thickness(0, 60, 0, 60);
        return card;
    }

    // ---------------- Güncelleme ----------------

    public void Refresh()
    {
        BuildModeList();
        BuildPresetCard();
        BuildSettings();
        BuildShake();
        _drawnMode = null;
    }

    public void Tick(Telemetry? t)
    {
        bool connected = _app.Client.State == ConnectionState.Connected;
        _connected.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        _waiting.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;

        var mode = S.Mode;
        if (_drawnMode != mode || _drawnPresetId != Engine.ActivePresetId)
        {
            bool modeChanged = _drawnMode != mode;
            _drawnMode = mode;
            _drawnPresetId = Engine.ActivePresetId;
            BuildModeList();
            BuildPresetCard();
            DrawPreview(mode);
            if (modeChanged)
            {
                _advancedOpen = false;
                BuildSettings();
            }
        }

        bool live = Engine.Enabled && _app.Client.CameraAcquired && t != null && t.Pose.FovDeg > 0;
        _tileMode.Text = CameraEngine.ModeLabel(mode);
        (_tileDetailLabel.Text, _tileDetail.Text) = mode switch
        {
            CameraModeKind.Inside => ("Görünüm", live ? t!.Detail : "—"),
            CameraModeKind.Fixed => ("Görünüm", live ? t!.Detail : "—"),
            CameraModeKind.Cinematic => ("Plan", live ? t!.Detail : "—"),
            _ => ("Uzaklık", live ? t!.Detail : "—"),
        };
        _tileFov.Text = live ? $"{t!.Pose.FovDeg:0}°" : "—";
        _tileCam.Text = Engine.Enabled ? (_app.Client.CameraAcquired ? "Aktif" : "Bekliyor…") : "Kapalı";
        _tileCam.Foreground = U.B(Engine.Enabled && _app.Client.CameraAcquired ? "AccentText" : "Fg");
    }

    // ---------------- Modlar ----------------

    private void BuildModeList()
    {
        _modeList.Children.Clear();
        foreach (var m in Modes)
        {
            bool sel = S.Mode == m.Kind;
            var iconBox = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(9),
                Background = sel ? U.B("Accent") : U.Hex("#232833"),
                Child = U.Icon(m.Icon, sel ? "AccentInk" : "Fg"),
            };
            var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(U.Text(m.Name, 15, "Fg", FontWeights.SemiBold));
            texts.Children.Add(U.Text(m.Desc, 12.5, "FgDim", wrap: true));
            var kbd = U.Kbd(U.PrettyKey(S.Hotkeys.GetValueOrDefault(m.HotkeyAction, "")));

            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(iconBox, Dock.Left);
            DockPanel.SetDock(kbd, Dock.Right);
            row.Children.Add(iconBox);
            row.Children.Add(kbd);
            row.Children.Add(texts);

            var b = new Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 10, 10, 10),
                MinHeight = 64,
                Margin = new Thickness(0, 0, 0, 8),
                Background = U.B(sel ? "AccentSoft" : "Item"),
                BorderBrush = sel ? U.B("Accent") : U.Hex("#262B35"),
            };
            U.AutomationProps(b, m.Name + " modu", sel);
            var kind = m.Kind;
            b.Click += (_, _) => Engine.SetMode(kind);
            _modeList.Children.Add(b);
        }
    }

    // ---------------- Uçağın preset'leri ----------------

    private void BuildPresetCard()
    {
        _presetCard.Children.Clear();
        var profile = Engine.Profile;

        var head = new DockPanel();
        var all = U.Button($"Tümü ({profile?.Presets.Count ?? 0})", () => _app.Navigate(PageId.Presets), "link");
        all.Margin = new Thickness(0);
        all.MinHeight = 24;
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        var overline = U.Overline("Bu uçağın preset'leri");
        overline.Margin = new Thickness(0);
        head.Children.Add(overline);
        _presetCard.Children.Add(head);

        var sub = U.Note(profile == null
            ? (Engine.LoadedAircraftPath == null ? "Uçak bekleniyor." : "Bu uçak için kayıtlı preset yok.")
            : $"{profile.DisplayName} · {(profile.Source.Length > 0 ? profile.Source : "elle oluşturuldu")}");
        sub.Margin = new Thickness(0, 4, 0, 12);
        _presetCard.Children.Add(sub);

        if (profile != null && profile.Presets.Count > 0)
        {
            var wrap = new WrapPanel();
            // Moda uygun olanları göster: iç kamerada iç görünümler, diğerlerinde dış görünümler.
            bool inside = S.Mode == CameraModeKind.Inside;
            var quick = profile.Presets.Where(p => p.Kind.IsInside() == inside).Take(9).ToList();
            if (quick.Count == 0) quick = profile.Presets.Take(9).ToList();
            foreach (var p in quick)
            {
                var preset = p;
                string? key = p.Hotkey.Length > 0 ? U.PrettyKey(p.Hotkey.Split('+').Last()) : null;
                wrap.Children.Add(U.Chip(p.Name, Engine.ActivePresetId == p.Id, () => Engine.ApplyCameraPreset(preset), key));
            }
            _presetCard.Children.Add(wrap);
        }

        var import = new Button
        {
            Content = U.Row(8, U.Icon(U.Icons.Download, "Fg", 16, 2), U.Text("ChasePlane export'u içe aktar", 13)),
            Background = Brushes.Transparent,
            BorderBrush = U.Hex("#3A4250"),
            Margin = new Thickness(0, 8, 0, 0),
            MinHeight = 44,
        };
        import.Click += (_, _) => _app.OpenImport();
        _presetCard.Children.Add(import);
    }

    // ---------------- Önizleme ----------------

    private void DrawPreview(CameraModeKind mode)
    {
        var c = _preview;
        c.Children.Clear();
        TopView.Grid(c, 360, 240, 60);

        double cx = 180, cy = 118;
        if (mode == CameraModeKind.Flyby)
            c.Children.Add(new Line { X1 = 180, Y1 = 236, X2 = 180, Y2 = 6, Stroke = U.Hex("#3A4250"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 } });
        if (mode == CameraModeKind.Orbit)
            c.Children.Add(new Ellipse { Width = 184, Height = 184, Stroke = U.Hex("#3A4250"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 }, Margin = new Thickness(cx - 92, cy - 92, 0, 0) });

        // 37 m uzunluğunda bir uçak, 3 px/m.
        TopView.DrawAircraft(c, x => cx + x * 3, z => cy - z * 3, 23, 37, 35, 2.2, false);
        c.Children.Add(new TextBlock { Text = "BURUN ▲", FontSize = 10, Foreground = U.B("FgFaint"), Margin = new Thickness(156, 4, 0, 0) });

        switch (mode)
        {
            case CameraModeKind.Chase:
                TopView.Camera(c, new Point(180, 226), new Point(cx, cy));
                break;
            case CameraModeKind.Flyby:
                TopView.Camera(c, new Point(296, 40), new Point(cx, cy));
                break;
            case CameraModeKind.Orbit:
                TopView.Camera(c, new Point(cx - 65, cy - 65), new Point(cx, cy));
                break;
            case CameraModeKind.Inside:
                TopView.Camera(c, new Point(cx, cy - 58), new Point(cx, 0), 44, 0.6);
                break;
            case CameraModeKind.Cinematic:
                foreach (var p in new[] { new Point(300, 70), new Point(70, 190), new Point(290, 200) })
                    TopView.Dot(c, p, 14, U.B("Raised"), U.Hex("#5B6474"));
                TopView.Camera(c, new Point(70, 52), new Point(cx, cy));
                break;
            case CameraModeKind.Fixed:
                var view = Engine.Profile?.Presets.FirstOrDefault(p => p.Id == Engine.ActivePresetId);
                var at = view == null ? new Point(cx, cy + 60) : new Point(Math.Clamp(cx + view.X * 3, 10, 350), Math.Clamp(cy - view.Z * 3, 10, 230));
                double yaw = (view?.YawDeg ?? 0) * Math.PI / 180;
                TopView.Camera(c, at, new Point(at.X + Math.Sin(yaw) * 50, at.Y - Math.Cos(yaw) * 50), 60, 0.45);
                break;
        }

        var info = Modes.FirstOrDefault(m => m.Kind == mode);
        _hint.Text = info?.Hint ?? "Uçağın gövdesine sabitlenmiş kamera. Ayarları Preset'ler sayfasından düzenleyebilirsiniz.";
        _hint.Margin = new Thickness(0, 4, 0, 0);
        BuildActions(mode);
    }

    private void BuildActions(CameraModeKind mode)
    {
        _actions.Children.Clear();
        var wrap = new WrapPanel();
        (string label, Action action) primary = mode switch
        {
            CameraModeKind.Chase => ("Tam arkaya al", () => Engine.Manual.Reset()),
            CameraModeKind.Flyby => ("Yeni flyby noktası", Engine.Next),
            CameraModeKind.Orbit => ("Dönüş yönünü çevir", Engine.ReverseOrbit),
            CameraModeKind.Inside => ("Sonraki iç görünüm", Engine.Next),
            CameraModeKind.Cinematic => ("Sonraki plan", Engine.Next),
            _ => ("Chase'e dön", () => Engine.SetMode(CameraModeKind.Chase)),
        };
        wrap.Children.Add(U.Button(primary.label, primary.action));
        if (mode != CameraModeKind.Chase) wrap.Children.Add(U.Button("Görünümü sıfırla", () => Engine.Manual.Reset(), "ghost"));
        wrap.Children.Add(U.Button("Preset olarak kaydet", SaveAsPreset, "ghost"));
        _actions.Children.Add(wrap);
    }

    private void SaveAsPreset()
    {
        var t = _app.Engine.Latest;
        if (t != null && t.Pose.Frame == PoseFrame.Body)
        {
            var profile = EnsureProfile();
            if (profile == null) { _app.Log("Uçak tanınmadı; preset kaydedilemedi."); return; }
            var view = Engine.CaptureCurrentView($"Görünüm {profile.Presets.Count + 1}");
            if (view == null) return;
            profile.Presets.Add(view);
            _app.Log($"“{view.Name}” {profile.DisplayName} profiline eklendi.");
        }
        else
        {
            int slot = Array.FindIndex(S.Presets, p => p == null);
            if (slot < 0) slot = 8;
            S.Presets[slot] = Engine.CapturePreset($"Slot {slot + 1}");
            _app.Log($"Kamera ayarları slot {slot + 1}'e kaydedildi ({U.PrettyKey(S.Hotkeys.GetValueOrDefault($"LoadPreset{slot + 1}", ""))} ile yüklenir).");
        }
        _app.Save();
        Refresh();
    }

    private AircraftProfile? EnsureProfile()
    {
        if (Engine.Profile != null) return Engine.Profile;
        var path = Engine.LoadedAircraftPath;
        if (path == null) return null;
        string key = AircraftProfile.KeyFromCfgPath(path);
        var profile = new AircraftProfile { Key = key, DisplayName = key, CfgPath = path };
        S.Profiles.Add(profile);
        Engine.RefreshProfile();
        return profile;
    }

    // ---------------- Ayarlar ----------------

    private static string F1(double v) => v.ToString("0.0");
    private static string F2(double v) => v.ToString("0.00");
    private static string Pct(double v) => $"%{v * 100:0}";
    private static string Elev(double ratio) => $"{Math.Atan(ratio) * 180 / Math.PI:0}°";

    private void BuildSettings()
    {
        _settingsCard.Children.Clear();
        var mode = S.Mode;
        string name = mode == CameraModeKind.Fixed ? "Sabit kamera" : CameraEngine.ModeLabel(mode);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var right = U.Text("Değişiklikler anında uygulanır", 13, "FgDim");
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(U.Title(name + " ayarları"));
        _settingsCard.Children.Add(head);

        var (basic, advanced) = SlidersFor(mode);
        _settingsCard.Children.Add(TwoColumns(basic));

        if (mode == CameraModeKind.Cinematic) _settingsCard.Children.Add(ShotToggles());
        if (mode == CameraModeKind.Flyby)
        {
            var zoom = new CheckBox { Content = "Uzaklaştıkça zoom yap", IsChecked = S.Flyby.ZoomFollow, Margin = new Thickness(0, 0, 0, 4) };
            zoom.Checked += (_, _) => S.Flyby.ZoomFollow = true;
            zoom.Unchecked += (_, _) => S.Flyby.ZoomFollow = false;
            _settingsCard.Children.Add(zoom);
        }
        if (mode == CameraModeKind.Fixed)
            _settingsCard.Children.Add(U.Note("Konum ve açılar preset'ten gelir. Kısayollarla döndürüp yaklaşabilir, Preset'ler sayfasından kalıcı olarak düzenleyebilirsiniz."));

        if (advanced.Count > 0)
        {
            var sep = new Border { BorderBrush = U.B("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0, 8, 0, 0) };
            var panel = new StackPanel();
            var toggle = U.Button($"{(_advancedOpen ? "−" : "+")}  Gelişmiş ayarlar ({advanced.Count})", () =>
            {
                _advancedOpen = !_advancedOpen;
                BuildSettings();
            }, "link");
            panel.Children.Add(toggle);
            if (_advancedOpen) panel.Children.Add(TwoColumns(advanced));
            sep.Child = panel;
            _settingsCard.Children.Add(sep);
        }
    }

    private (List<FrameworkElement> Basic, List<FrameworkElement> Advanced) SlidersFor(CameraModeKind mode)
    {
        var b = new List<FrameworkElement>();
        var a = new List<FrameworkElement>();
        switch (mode)
        {
            case CameraModeKind.Chase:
                var c = S.Chase;
                b.Add(U.SliderRow("Mesafe", "Yakın", "Uzak", 0.8, 8, 0.1, () => c.DistanceSpans, v => c.DistanceSpans = v, v => F1(v) + " × kanat"));
                b.Add(U.SliderRow("Yükseklik", "Alçaktan", "Yukarıdan", -0.5, 1.5, 0.01, () => c.HeightRatio, v => c.HeightRatio = v, Elev));
                b.Add(U.SliderRow("Yumuşaklık", "Sert", "Yaylı", 0, 2, 0.05, () => c.PositionLagSec, v => c.PositionLagSec = v, v => F2(v) + " sn"));
                b.Add(U.SliderRow("Görüş açısı", "Tele", "Geniş", 15, 100, 1, () => c.FovDeg, v => c.FovDeg = v, v => $"{v:0}°"));
                a.Add(U.SliderRow("Dönüş takibi gecikmesi", "Anında", "Geç", 0, 3, 0.05, () => c.HeadingLagSec, v => c.HeadingLagSec = v, v => F2(v) + " sn"));
                a.Add(U.SliderRow("Pitch takibi", "Yok", "Tam", 0, 1, 0.05, () => c.FollowPitch, v => c.FollowPitch = v, Pct));
                a.Add(U.SliderRow("Yatışa eğilme", "Ufuk sabit", "Uçakla birlikte", 0, 1, 0.05, () => c.FollowBank, v => c.FollowBank = v, Pct));
                a.Add(U.SliderRow("G-kuvveti hissi", "Yok", "Güçlü", 0, 10, 0.5, () => c.GForceM, v => c.GForceM = v, v => F1(v) + " m/g"));
                a.Add(U.SliderRow("Burnun önüne bakış", "Uçağa", "Öne", 0, 3, 0.1, () => c.LookAheadSpans, v => c.LookAheadSpans = v, v => F1(v) + " × kanat"));
                a.Add(U.SliderRow("Yana kaydırma", "Sol", "Sağ", -40, 40, 1, () => c.LateralM, v => c.LateralM = v, v => $"{v:0} m"));
                break;

            case CameraModeKind.Flyby:
                var f = S.Flyby;
                b.Add(U.SliderRow("Ne kadar önde", "Hemen", "Uzakta", 1, 20, 0.5, () => f.LeadSec, v => f.LeadSec = v, v => F1(v) + " sn"));
                b.Add(U.SliderRow("Yan mesafe", "Yakın geçiş", "Uzak", 0, 300, 5, () => f.SideOffsetM, v => f.SideOffsetM = v, v => $"{v:0} m"));
                b.Add(U.SliderRow("Kamera yüksekliği", "Aşağıda", "Yukarıda", -30, 100, 1, () => f.HeightOffsetM, v => f.HeightOffsetM = v, v => $"{v:0} m"));
                b.Add(U.SliderRow("Zoom takibi", "Geniş", "Sıkı kadraj", 0.1, 0.9, 0.05, () => f.Framing, v => f.Framing = v, Pct));
                a.Add(U.SliderRow("Yeni nokta mesafesi", "Kısa", "Uzun", 200, 5000, 50, () => f.ResetDistanceM, v => f.ResetDistanceM = v, v => $"{v:0} m"));
                a.Add(U.SliderRow("Kameraman gecikmesi", "Robot", "İnsan", 0, 1.5, 0.05, () => f.OperatorLagSec, v => f.OperatorLagSec = v, v => F2(v) + " sn"));
                a.Add(U.SliderRow("En dar açı", "Çok tele", "Az", 2, 40, 1, () => f.MinFovDeg, v => f.MinFovDeg = v, v => $"{v:0}°"));
                a.Add(U.SliderRow("En geniş açı", "Dar", "Geniş", 30, 100, 1, () => f.MaxFovDeg, v => f.MaxFovDeg = v, v => $"{v:0}°"));
                break;

            case CameraModeKind.Orbit:
                var o = S.Orbit;
                b.Add(U.SliderRow("Yarıçap", "Yakın", "Uzak", 0.8, 10, 0.1, () => o.RadiusSpans, v => o.RadiusSpans = v, v => F1(v) + " × kanat"));
                b.Add(U.SliderRow("Yükseklik", "Alçaktan", "Yukarıdan", -0.5, 1.5, 0.01, () => o.HeightRatio, v => o.HeightRatio = v, Elev));
                b.Add(U.SliderRow("Dönüş hızı", "Yavaş", "Hızlı", 1, 40, 1, () => Math.Abs(o.SpeedDegS), v => o.SpeedDegS = Math.Sign(o.SpeedDegS == 0 ? 1 : o.SpeedDegS) * v, v => $"{v:0}°/sn"));
                b.Add(U.SliderRow("Görüş açısı", "Tele", "Geniş", 15, 100, 1, () => o.FovDeg, v => o.FovDeg = v, v => $"{v:0}°"));
                a.Add(U.SliderRow("Yumuşaklık", "Sert", "Yaylı", 0, 2, 0.05, () => o.PositionLagSec, v => o.PositionLagSec = v, v => F2(v) + " sn"));
                break;

            case CameraModeKind.Inside:
                var i = S.Inside;
                b.Add(U.SliderRow("Görüş açısı farkı", "Daha tele", "Daha geniş", -30, 30, 1, () => i.FovOffsetDeg, v => i.FovOffsetDeg = v, v => v == 0 ? "Preset'teki gibi" : $"{v:+0;−0}°"));
                b.Add(U.SliderRow("Baş hareketi", "Sabit", "Canlı", 0, 1, 0.05, () => i.HeadMotion, v => i.HeadMotion = v, Pct));
                b.Add(U.SliderRow("G-kuvveti hissi", "Yok", "Güçlü", 0, 1, 0.05, () => i.GForce, v => i.GForce = v, Pct));
                b.Add(U.SliderRow("Dönüşe bakma", "Kapalı", "Belirgin", 0, 1, 0.05, () => i.LookIntoTurn, v => i.LookIntoTurn = v, Pct));
                a.Add(U.SliderRow("Görünüm geçişi", "Anında", "Yumuşak", 0, 2, 0.05, () => i.TransitionSec, v => i.TransitionSec = v, v => v == 0 ? "Anında" : F2(v) + " sn"));
                a.Add(U.SliderRow("Gövde titreşimi", "Yok", "Güçlü", 0, 1, 0.05, () => i.Vibration, v => i.Vibration = v, Pct));
                a.Add(U.SliderRow("Preset yokken görüş açısı", "Tele", "Geniş", 30, 110, 1, () => i.FovDeg, v => i.FovDeg = v, v => $"{v:0}°"));
                break;

            case CameraModeKind.Cinematic:
                var cs = S.Cinematic;
                b.Add(U.SliderRow("Plan süresi (en az)", "Kısa", "Uzun", 3, 60, 1, () => cs.MinShotSec, v => cs.MinShotSec = v, v => $"{v:0} sn"));
                b.Add(U.SliderRow("Plan süresi (en çok)", "Kısa", "Uzun", 3, 120, 1, () => cs.MaxShotSec, v => cs.MaxShotSec = v, v => $"{v:0} sn"));
                b.Add(U.SliderRow("Geçiş", "Kesme", "Yumuşak", 0, 5, 0.1, () => cs.BlendSec, v => cs.BlendSec = v, v => v == 0 ? "Kesme" : F1(v) + " sn"));
                break;

            case CameraModeKind.Fixed:
                var view = Engine.Profile?.Presets.FirstOrDefault(p => p.Id == Engine.ActivePresetId);
                if (view != null)
                    b.Add(U.SliderRow("Görüş açısı", "Tele", "Geniş", 10, 110, 1, () => view.FovDeg, v => view.FovDeg = v, v => $"{v:0}°"));
                break;
        }
        return (b, a);
    }

    private static Grid TwoColumns(List<FrameworkElement> items)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < items.Count; i++)
        {
            if (i % 2 == 0) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(items[i], i / 2);
            Grid.SetColumn(items[i], i % 2);
            g.Children.Add(items[i]);
        }
        return g;
    }

    private FrameworkElement ShotToggles()
    {
        var c = S.Cinematic;
        var wrap = new WrapPanel();
        void Add(string label, Func<bool> get, Action<bool> set)
        {
            bool on = get();
            wrap.Children.Add(U.Chip((on ? "✓  " : "+  ") + label, on, () => { set(!get()); BuildSettings(); }));
        }
        Add("Arkadan takip", () => c.UseChase, v => c.UseChase = v);
        Add("Yandan takip", () => c.UseTracking, v => c.UseTracking = v);
        Add("Flyby", () => c.UseFlyby, v => c.UseFlyby = v);
        Add("Orbit", () => c.UseOrbit, v => c.UseOrbit = v);
        Add("Uçağın sabit preset'leri", () => c.UsePresets, v => c.UsePresets = v);
        Add("Kokpit / kabin", () => c.UseInside, v => c.UseInside = v);
        var legend = U.Text("Hangi planlar kullanılsın?", 14, "Fg", FontWeights.Medium);
        legend.Margin = new Thickness(0, 0, 0, 10);
        return new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children = { legend, wrap } };
    }

    // ---------------- Sarsıntı ----------------

    private void BuildShake()
    {
        _shakeCard.Children.Clear();
        var sh = S.Shake;
        string level = !sh.Enabled ? "off" : sh.Master < 0.75 ? "light" : sh.Master < 1.4 ? "cine" : "heavy";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel
        {
            Children =
            {
                U.Title("Kamera sarsıntısı"),
                U.Note("Motor, türbülans, pist ve iniş hissi."),
            },
        };
        var seg = U.Segmented(new[] { ("off", "Kapalı"), ("light", "Hafif"), ("cine", "Sinematik"), ("heavy", "Yoğun") }, level, v =>
        {
            sh.Enabled = v != "off";
            sh.Master = v switch { "light" => 0.5, "heavy" => 1.8, _ => 1.0 };
            BuildShake();
        });
        seg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(seg, 1);
        grid.Children.Add(left);
        grid.Children.Add(seg);
        _shakeCard.Children.Add(grid);

        var details = new StackPanel();
        bool open = _shakeOpen;
        details.Children.Add(U.Button($"{(open ? "−" : "+")}  Kaynaklara göre ayarla", () => { _shakeOpen = !_shakeOpen; BuildShake(); }, "link"));
        if (open)
        {
            details.Children.Add(TwoColumns(new List<FrameworkElement>
            {
                U.SliderRow("Motor titreşimi", "Yok", "Güçlü", 0, 3, 0.05, () => sh.Engine, v => sh.Engine = v, F2),
                U.SliderRow("Türbülans", "Yok", "Güçlü", 0, 3, 0.05, () => sh.Turbulence, v => sh.Turbulence = v, F2),
                U.SliderRow("Pist / yer teması", "Yok", "Güçlü", 0, 3, 0.05, () => sh.GroundRoll, v => sh.GroundRoll = v, F2),
                U.SliderRow("İniş darbesi", "Yok", "Güçlü", 0, 3, 0.05, () => sh.Touchdown, v => sh.Touchdown = v, F2),
                U.SliderRow("Elde kamera hissi", "Yok", "Belirgin", 0, 3, 0.05, () => sh.Handheld, v => sh.Handheld = v, F2),
            }));
        }
        details.Margin = new Thickness(0, 8, 0, 0);
        _shakeCard.Children.Add(details);
    }

    private bool _shakeOpen;
}

/// <summary>Sabit sütun sayılı basit ızgara.</summary>
internal sealed class UniformGrid2
{
    public System.Windows.Controls.Primitives.UniformGrid Panel { get; }
    public UniformGrid2(int columns) => Panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
    public void Add(UIElement e) => Panel.Children.Add(e);
}
