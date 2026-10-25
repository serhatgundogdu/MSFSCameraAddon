using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CinematicCam.Camera;
using CinematicCam.Settings;

namespace CinematicCam.Ui;

/// <summary>Uçak profillerine göre preset kütüphanesi: harita, liste ve ayrıntı.</summary>
public sealed class PresetsPage : IPage
{
    private static readonly Regex Unnamed = new(@"^temp\d*$", RegexOptions.IgnoreCase);
    private const double MapW = 480, MapH = 440;

    private readonly AppServices _app;
    private readonly ScrollViewer _root = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private bool _inside;
    private bool _hideUnnamed;
    private string? _selectedId;
    private AircraftProfile? _shownProfile;

    public PresetsPage(AppServices app)
    {
        _app = app;
        Refresh();
    }

    public FrameworkElement View => _root;

    public void Tick(Telemetry? telemetry)
    {
        if (!ReferenceEquals(_shownProfile, _app.Engine.Profile)) Refresh();
    }

    private AircraftProfile? Profile => _app.Engine.Profile;

    public void Refresh()
    {
        _shownProfile = Profile;
        var page = new StackPanel();
        page.Children.Add(BuildProfileBar());

        var profile = Profile;
        if (profile == null || profile.Presets.Count == 0)
        {
            page.Children.Add(EmptyState(profile));
            _root.Content = page;
            return;
        }

        int outCount = profile.Presets.Count(p => !p.Kind.IsInside());
        int inCount = profile.Presets.Count - outCount;
        var seg = U.Segmented(new[] { (false, $"Dış kamera  {outCount}"), (true, $"İç kamera  {inCount}") }, _inside, v =>
        {
            _inside = v;
            _selectedId = null;
            Refresh();
        });
        seg.Margin = new Thickness(0, 18, 0, 18);
        page.Children.Add(seg);

        var visible = profile.Presets.Where(p => p.Kind.IsInside() == _inside && !(_hideUnnamed && Unnamed.IsMatch(p.Name))).ToList();
        var selected = visible.FirstOrDefault(p => p.Id == _selectedId) ?? visible.FirstOrDefault();
        _selectedId = selected?.Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MinWidth = 340 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

        var map = BuildMap(visible, selected);
        map.Margin = new Thickness(0, 0, 18, 0);
        var list = BuildList(visible, selected);
        list.Margin = new Thickness(0, 0, 18, 0);
        list.VerticalAlignment = VerticalAlignment.Top;
        var detail = selected == null ? U.Card(U.Note("Bu grupta preset yok.")) : BuildDetail(profile, selected);
        detail.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(list, 1);
        Grid.SetColumn(detail, 2);
        grid.Children.Add(map);
        grid.Children.Add(list);
        grid.Children.Add(detail);
        page.Children.Add(grid);

        _root.Content = page;
    }

    // ---------------- Profil çubuğu ----------------

    private FrameworkElement BuildProfileBar()
    {
        var s = _app.Settings;
        var engine = _app.Engine;
        var panel = new StackPanel();
        panel.Children.Add(U.Overline("Uçak profili"));

        var chips = new WrapPanel();
        chips.Children.Add(U.Chip("Otomatik (simdeki uçak)", s.PinnedProfileKey == null, () => Pin(null)));
        foreach (var p in s.Profiles)
        {
            var key = p.Key;
            chips.Children.Add(U.Chip($"{p.DisplayName}  · {p.Presets.Count}", s.PinnedProfileKey == key, () => Pin(key)));
        }
        panel.Children.Add(chips);

        var pills = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var profile = Profile;
        if (engine.LoadedAircraftPath == null)
            pills.Children.Add(U.Pill("Simde uçak bekleniyor", "Raised", "FgDim"));
        else if (engine.MatchedProfile != null && ReferenceEquals(engine.MatchedProfile, profile))
            pills.Children.Add(U.Pill("Simdeki uçakla eşleşti", "OkSoft", "OkText", "Ok"));
        else
            pills.Children.Add(U.Pill("Simdeki uçak: " + AircraftProfile.KeyFromCfgPath(engine.LoadedAircraftPath), "WarnSoft", "AccentText", "Accent"));
        if (profile != null)
        {
            if (profile.Source.Length > 0) pills.Children.Add(U.Pill("Kaynak: " + profile.Source, "Raised", "FgDim"));
            pills.Children.Add(U.Pill(profile.Key, "Raised", "FgDim", mono: true));
        }
        panel.Children.Add(pills);

        var buttons = new WrapPanel();
        buttons.Children.Add(U.Button(U.Row(8, U.Icon(U.Icons.Download, "AccentInk", 16, 2.2), U.Text("İçe aktar", 14, "AccentInk", FontWeights.Bold)), _app.OpenImport, "primary"));
        if (profile != null)
        {
            buttons.Children.Add(U.Button("Dışa aktar", () => Export(profile)));
            var auto = new CheckBox { Content = "Bu uçak yüklenince otomatik etkinleştir", IsChecked = profile.AutoActivate, Margin = new Thickness(8, 0, 16, 8) };
            auto.Checked += (_, _) => { profile.AutoActivate = true; _app.Save(); engine.RefreshProfile(); };
            auto.Unchecked += (_, _) => { profile.AutoActivate = false; _app.Save(); engine.RefreshProfile(); };
            buttons.Children.Add(auto);
            buttons.Children.Add(U.Button("Profili sil", () => DeleteProfile(profile), "danger"));
        }
        panel.Children.Add(buttons);
        return U.Card(panel, 18);
    }

    private void Pin(string? key)
    {
        _app.Settings.PinnedProfileKey = key;
        _app.Engine.RefreshProfile();
        _app.Save();
        _app.RegisterHotkeys();
        Refresh();
    }

    private void DeleteProfile(AircraftProfile profile)
    {
        var ok = MessageBox.Show($"“{profile.DisplayName}” profili ve {profile.Presets.Count} preset'i silinsin mi?", "Profili sil",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ok != MessageBoxResult.Yes) return;
        _app.Settings.Profiles.Remove(profile);
        if (_app.Settings.PinnedProfileKey == profile.Key) _app.Settings.PinnedProfileKey = null;
        _app.Engine.RefreshProfile();
        _app.Save();
        _app.RegisterHotkeys();
        _app.Log($"“{profile.DisplayName}” profili silindi.");
        Refresh();
    }

    private void Export(AircraftProfile profile)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = profile.Key + ".cinematiccam.json",
            Filter = "CinematicCam profili (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true) return;
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, options));
        _app.Log("Profil dışa aktarıldı: " + dialog.FileName);
    }

    private FrameworkElement EmptyState(AircraftProfile? profile)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
        stack.Children.Add(U.Title(profile == null ? "Bu uçak için preset yok" : "Profilde henüz preset yok", 20));
        var note = U.Note("ChasePlane'den dışa aktardığınız zip dosyasını içe aktarın. Kameralar uçağa göre otomatik eşleşir. " +
                          "Ya da sabit/iç kamerada iken ana ekrandaki “Preset olarak kaydet” düğmesini kullanın.", 14);
        note.Margin = new Thickness(0, 8, 0, 18);
        stack.Children.Add(note);
        stack.Children.Add(U.Button("ChasePlane export'u içe aktar", _app.OpenImport, "primary"));
        var card = U.Card(stack, 36);
        card.Margin = new Thickness(0, 18, 0, 0);
        return card;
    }

    // ---------------- Harita ----------------

    private static string KindColor(PresetKind k) => k switch
    {
        PresetKind.Chase or PresetKind.Cockpit => "#FFB547",
        PresetKind.Orbit or PresetKind.Cabin => "#63B3FF",
        _ => "#C8CDD6",
    };

    private FrameworkElement BuildMap(List<CameraPreset> visible, CameraPreset? selected)
    {
        var canvas = new Canvas { Width = MapW, Height = MapH, ClipToBounds = true };
        TopView.Grid(canvas, MapW, MapH, 110);

        // Dış: 4,2 px/m (±57 m). İç: 13 px/m (kabin boyu).
        double scale = _inside ? 13 : 4.2;
        double cy = _inside ? 40 : MapH / 2 - 10;
        double toX(double x) => MapW / 2 + x * scale;
        double toY(double z) => cy - z * scale;
        double span = _app.Engine.Latest?.Aircraft.WingSpanM is > 1 and var sp ? sp : 35.8;
        if (_inside) TopView.DrawAircraft(canvas, toX, toY, 2.5, 30, span, 2.1, true);
        else TopView.DrawAircraft(canvas, toX, toY, 18, span * 1.05, span, 2.0, false);
        canvas.Children.Add(new TextBlock { Text = "BURUN ▲", FontSize = 11, Foreground = U.B("FgFaint"), Margin = new Thickness(MapW / 2 - 26, 4, 0, 0) });

        foreach (var p in visible)
        {
            bool sel = p == selected;
            double px = Math.Clamp(toX(p.X), 14, MapW - 14), py = Math.Clamp(toY(p.Z), 14, MapH - 14);
            double size = sel ? 20 : 14;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = size, Height = size,
                Fill = sel ? U.B("Accent") : U.Hex(KindColor(p.Kind)),
                Stroke = sel ? Brushes.White : U.B("Bg"),
                StrokeThickness = 2,
            };
            var b = new Button
            {
                Content = dot, Width = 36, Height = 36, MinHeight = 0, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0),
                ToolTip = $"{p.Name} — {PresetNaming.Describe(p.Kind, p.X, p.Y, p.Z)}",
            };
            U.AutomationProps(b, p.Name, sel);
            var id = p.Id;
            b.Click += (_, _) => { _selectedId = id; Refresh(); };
            Canvas.SetLeft(b, px - 18);
            Canvas.SetTop(b, py - 18);
            canvas.Children.Add(b);
        }

        var box = new Border
        {
            Background = U.B("Bg"),
            BorderBrush = U.Hex("#222731"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 460 },
        };

        var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var kinds = _inside ? new[] { PresetKind.Cockpit, PresetKind.Cabin, PresetKind.InsideMounted } : new[] { PresetKind.Chase, PresetKind.Orbit, PresetKind.Airframe };
        foreach (var k in kinds)
            legend.Children.Add(U.Row(6, new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = U.Hex(KindColor(k)) }, U.Text(k.Label(), 12, "FgDim")).With(r => r.Margin = new Thickness(0, 0, 14, 0)));

        var note = U.Note(_inside ? "Kokpit ve kabin, pilot gözü noktasına göre. Bir noktaya tıklayın." : "Uçağa göre konumlar, burun yukarı. Bir noktaya tıklayın.");
        note.Margin = new Thickness(0, 0, 0, 12);
        return U.Card(new StackPanel { Children = { U.Overline("Kameralar uçağın neresinde?"), note, box, legend } });
    }

    // ---------------- Liste ----------------

    private FrameworkElement BuildList(List<CameraPreset> visible, CameraPreset? selected)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var hide = new CheckBox { Content = "İsimsizleri gizle", IsChecked = _hideUnnamed, Foreground = U.B("FgDim"), FontSize = 13 };
        hide.Checked += (_, _) => { _hideUnnamed = true; Refresh(); };
        hide.Unchecked += (_, _) => { _hideUnnamed = false; Refresh(); };
        DockPanel.SetDock(hide, Dock.Right);
        head.Children.Add(hide);
        head.Children.Add(U.Title(_inside ? "İç kameralar" : "Dış kameralar"));
        panel.Children.Add(head);

        foreach (var group in visible.GroupBy(p => p.Kind).OrderBy(g => g.Key))
        {
            var title = U.Row(8, new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = U.Hex(KindColor(group.Key)) },
                U.Overline($"{group.Key.Label()} · {group.Count()}").With(o => o.Margin = new Thickness(0)));
            title.Margin = new Thickness(0, 6, 0, 8);
            panel.Children.Add(title);
            foreach (var p in group)
            {
                bool sel = p == selected;
                var texts = new StackPanel();
                texts.Children.Add(U.Text(p.Name, 14, "Fg", FontWeights.SemiBold));
                texts.Children.Add(U.Text(PresetNaming.Describe(p.Kind, p.X, p.Y, p.Z) + " · " + p.Kind.Label(), 12.5, "FgDim"));
                var row = new DockPanel();
                if (p.Hotkey.Length > 0)
                {
                    var k = U.Kbd(U.PrettyKey(p.Hotkey));
                    DockPanel.SetDock(k, Dock.Right);
                    row.Children.Add(k);
                }
                if (Unnamed.IsMatch(p.Name))
                {
                    var badge = U.Pill("İsimsiz", "WarnSoft", "AccentText");
                    DockPanel.SetDock(badge, Dock.Right);
                    row.Children.Add(badge);
                }
                row.Children.Add(texts);
                var b = new Button
                {
                    Content = row,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 6, 12, 6),
                    MinHeight = 48,
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = U.B(sel ? "AccentSoft" : "Item"),
                    BorderBrush = sel ? U.B("Accent") : U.Hex("#262B35"),
                };
                U.AutomationProps(b, p.Name, sel);
                var id = p.Id;
                b.Click += (_, _) => { _selectedId = id; Refresh(); };
                b.MouseDoubleClick += (_, _) => _app.Engine.ApplyCameraPreset(p);
                panel.Children.Add(b);
            }
        }
        return U.Card(panel);
    }

    // ---------------- Ayrıntı ----------------

    private FrameworkElement BuildDetail(AircraftProfile profile, CameraPreset p)
    {
        var panel = new StackPanel();
        panel.Children.Add(U.Overline("Seçili preset"));

        panel.Children.Add(U.Text("Ad", 13, "FgDim"));
        var name = new TextBox { Text = p.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) };
        void Commit()
        {
            var n = name.Text.Trim();
            if (n.Length == 0 || n == p.Name) return;
            p.Name = n;
            _app.Save();
            Refresh();
        }
        name.LostKeyboardFocus += (_, _) => Commit();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        panel.Children.Add(name);

        if (Unnamed.IsMatch(p.Name))
        {
            string suggestion = PresetNaming.Describe(p.Kind, p.X, p.Y, p.Z);
            var row = U.Row(8, U.Text($"Öneri: “{suggestion}”", 12.5, "AccentText"),
                U.Button("Kullan", () => { p.Name = suggestion; _app.Save(); Refresh(); }, "ghost").With(b => { b.MinHeight = 30; b.Margin = new Thickness(0); }));
            row.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(row);
        }

        string F(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0");
        var tiles = new UniformGrid2(2);
        tiles.Add(U.Tile("ChasePlane türü", p.Kind.Label()));
        tiles.Add(U.Tile("CinematicCam modu", p.Kind.ModeLabel(), valueColor: "AccentText"));
        tiles.Add(U.Tile("Konum (sağ · yukarı · ileri)", $"{F(p.X)} · {F(p.Y)} · {F(p.Z)} m", mono: true));
        tiles.Add(U.Tile("Bakış (pitch · yaw)", $"{F(p.PitchDeg)}° · {F(p.YawDeg)}°", mono: true));
        tiles.Add(U.Tile("Zoom (ChasePlane)", p.ChasePlaneZoom?.ToString("0.00") ?? "—", mono: true));
        tiles.Add(U.Tile("Referans", p.Kind.ReferenceLabel()));
        tiles.Panel.Margin = new Thickness(0, 4, -8, 6);
        panel.Children.Add(tiles.Panel);

        if (p.Kind is not (PresetKind.Chase or PresetKind.Orbit))
            panel.Children.Add(U.SliderRow("Görüş açısı", "Tele", "Geniş", 10, 110, 1, () => p.FovDeg, v => p.FovDeg = v, v => $"{v:0}°").With(e => ((FrameworkElement)e).Margin = new Thickness(0, 4, 0, 12)));

        var cine = new CheckBox { Content = "Sinematik modda bu açıyı da kullan", IsChecked = p.UseInCinematic };
        cine.Checked += (_, _) => { p.UseInCinematic = true; _app.Save(); };
        cine.Unchecked += (_, _) => { p.UseInCinematic = false; _app.Save(); };
        panel.Children.Add(cine);

        var keyRow = new DockPanel { Margin = new Thickness(0, 8, 0, 12) };
        var keyBox = U.KeyCaptureBox(p.Hotkey, combo =>
        {
            p.Hotkey = combo;
            _app.Save();
            _app.RegisterHotkeys();
            Refresh();
        });
        DockPanel.SetDock(keyBox, Dock.Right);
        keyRow.Children.Add(keyBox);
        keyRow.Children.Add(U.Text("Kısayol", 14));
        panel.Children.Add(keyRow);

        var buttons = new WrapPanel();
        buttons.Children.Add(U.Button("Kameraya uygula", () =>
        {
            if (!_app.Engine.Enabled) _app.Log("Kamera kapalı; açınca bu görünüm kullanılacak.");
            _app.Engine.ApplyCameraPreset(p);
        }, "primary"));
        buttons.Children.Add(U.Button("Sil", () =>
        {
            profile.Presets.Remove(p);
            _selectedId = null;
            _app.Save();
            _app.RegisterHotkeys();
            _app.Engine.RefreshProfile();
            Refresh();
        }, "danger"));
        panel.Children.Add(buttons);

        return U.Card(panel, 18);
    }
}

internal static class FluentExtensions
{
    public static T With<T>(this T item, Action<T> configure)
    {
        configure(item);
        return item;
    }
}
