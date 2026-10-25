using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CinematicCam.Camera;
using CinematicCam.Settings;

namespace CinematicCam.Ui;

/// <summary>Üç adımlı kalibrasyon sihirbazı: uçak açıları, kamera bakışı, yön gönderme yöntemi.</summary>
public sealed class CalibrationPage : IPage
{
    private readonly AppServices _app;
    private readonly ScrollViewer _root = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _steps = new();
    private readonly StackPanel _content = new();
    private int _step = 1;
    private string? _cameraAnswer;
    private bool _finished;

    // Canlı gösterge parçaları (1. adım)
    private readonly List<(TextBlock Value, Border Bar, TextBlock Verdict, Func<Telemetry, double> Read, string Name)> _gauges = new();

    public CalibrationPage(AppServices app)
    {
        _app = app;
        var title = U.Title("Kamera kalibrasyonu", 24);
        var note = U.Note("İlk uçuşta bir kez yapılır, yaklaşık bir dakika sürer. Havadayken, otopilot açıkken yapmanız en kolayı.", 14);
        note.Margin = new Thickness(0, 6, 0, 18);
        note.MaxWidth = 680;
        note.HorizontalAlignment = HorizontalAlignment.Left;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _steps.Margin = new Thickness(0, 0, 18, 0);
        var card = U.Card(_content, 22);
        card.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(card, 1);
        grid.Children.Add(_steps);
        grid.Children.Add(card);

        _root.Content = new StackPanel { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left, Children = { title, note, grid } };
        Refresh();
    }

    public FrameworkElement View => _root;
    private CalibrationSettings Cal => _app.Settings.Calibration;

    public void Refresh()
    {
        BuildSteps();
        BuildContent();
    }

    public void Tick(Telemetry? t)
    {
        if (_step != 1) return;
        foreach (var g in _gauges)
        {
            if (t == null)
            {
                g.Value.Text = "—";
                g.Verdict.Text = "Telemetri yok — bir uçuş başlatın.";
                g.Verdict.Foreground = U.B("FgDim");
                g.Bar.Width = 0;
                continue;
            }
            double v = g.Read(t);
            g.Value.Text = (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0") + "°";
            double pct = Math.Clamp(v / 30, -1, 1);
            g.Bar.Width = Math.Abs(pct) * 120;
            g.Bar.HorizontalAlignment = HorizontalAlignment.Left;
            g.Bar.Margin = new Thickness(pct >= 0 ? 120 : 120 + pct * 120, 0, 0, 0);
            bool ok = v > 0.5, flat = Math.Abs(v) <= 0.5;
            g.Bar.Background = U.B(flat ? "FgFaint" : ok ? "Ok" : "Bad");
            g.Verdict.Text = flat ? $"Şimdi {g.Name.ToLower()} yapın…" : ok ? $"✓ {g.Name} pozitif — doğru" : $"✗ {g.Name} negatif — ters, aşağıdan çevirin";
            g.Verdict.Foreground = U.B(flat ? "FgDim" : ok ? "OkText" : "BadText");
        }
    }

    private void BuildSteps()
    {
        _steps.Children.Clear();
        string[] titles = { "Uçak açıları", "Kamera bakışı", "Dönüş takibi" };
        string[] status =
        {
            _step > 1 ? "Tamam" : "Burnu kaldırın, sağa yatın",
            _cameraAnswer == "ok" ? "Tamam" : _cameraAnswer == "fixed" ? "Düzeltildi" : "Kontrol edilmedi",
            _finished ? "Tamam" : _step == 3 ? "Kontrol ediliyor" : "Bekliyor",
        };
        bool[] done = { _step > 1, _cameraAnswer != null, _finished };
        for (int i = 0; i < 3; i++)
        {
            int n = i + 1;
            bool cur = _step == n;
            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                Background = cur ? U.B("Accent") : done[i] ? U.B("OkSoft") : U.Hex("#232833"),
                Child = new TextBlock
                {
                    Text = done[i] && !cur ? "✓" : n.ToString(), FontWeight = FontWeights.Bold,
                    Foreground = cur ? U.B("AccentInk") : done[i] ? U.B("OkText") : U.B("Fg"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { U.Text(titles[i], 14, "Fg", FontWeights.SemiBold), U.Text(status[i], 12.5, "FgDim") } };
            var b = new Button
            {
                Content = U.Row(0, badge, texts),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 60,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = U.B(cur ? "AccentSoft" : "Card"),
                BorderBrush = cur ? U.B("Accent") : U.B("Line"),
            };
            U.AutomationProps(b, $"Adım {n}: {titles[i]}", cur);
            b.Click += (_, _) => { _step = n; Refresh(); };
            _steps.Children.Add(b);
        }
    }

    private void BuildContent()
    {
        _content.Children.Clear();
        _gauges.Clear();
        switch (_step)
        {
            case 1: Step1(); break;
            case 2: Step2(); break;
            default: Step3(); break;
        }

        var nav = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var back = U.Button("Geri", () => { _step = Math.Max(1, _step - 1); Refresh(); }, "ghost");
        back.IsEnabled = _step > 1;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        var next = U.Button(_step == 3 ? "Kalibrasyonu bitir" : "Sonraki adım", () =>
        {
            if (_step == 3)
            {
                _finished = true;
                _app.Save();
                _app.Log("Kalibrasyon kaydedildi.");
            }
            else _step++;
            Refresh();
        }, "primary");
        DockPanel.SetDock(next, Dock.Right);
        nav.Children.Add(next);
        nav.Children.Add(back);
        var sep = new Border { BorderBrush = U.B("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Child = nav };
        _content.Children.Add(sep);
    }

    private void Step1()
    {
        _content.Children.Add(U.Title("1 · Uçak açıları doğru okunuyor mu?", 19));
        _content.Children.Add(U.Note("Burnu biraz kaldırın ve sağa yatın. Aşağıdaki göstergeler yeşil olmalı.", 14).With(n => n.Margin = new Thickness(0, 8, 0, 16)));

        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        row.Children.Add(Gauge("Pitch (burun yukarı)", "Pitch", t => t.Aircraft.PitchDeg));
        row.Children.Add(Gauge("Bank (sağa yatış)", "Bank", t => t.Aircraft.BankDeg));
        _content.Children.Add(row);

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(FlipButton("Pitch ters → çevir", () => Cal.InvertAircraftPitch, v => Cal.InvertAircraftPitch = v));
        buttons.Children.Add(FlipButton("Bank ters → çevir", () => Cal.InvertAircraftBank, v => Cal.InvertAircraftBank = v));
        _content.Children.Add(buttons);
    }

    private Button FlipButton(string label, Func<bool> get, Action<bool> set)
    {
        string state = get() ? "  (çevrildi)" : "";
        return U.Button(label + state, () => { set(!get()); _app.Save(); BuildContent(); });
    }

    private FrameworkElement Gauge(string title, string name, Func<Telemetry, double> read)
    {
        var value = U.Text("—", 20);
        value.FontFamily = U.Mono;
        var head = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(U.Text(title, 14, "Fg", FontWeights.SemiBold));

        var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = U.B("FgFaint") };
        var track = new Grid { Width = 240, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 10) };
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = U.B("Bg") });
        track.Children.Add(new Rectangle { Width = 2, Height = 14, Fill = U.Hex("#3A4250") });
        track.Children.Add(bar);
        var verdict = U.Text("", 13, "FgDim", wrap: true);
        _gauges.Add((value, bar, verdict, read, name));

        return new Border
        {
            Background = U.B("Raised"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new StackPanel { Children = { head, track, verdict } },
        };
    }

    private void Step2()
    {
        _content.Children.Add(U.Title("2 · Kamera uçağa bakıyor mu?", 19));
        _content.Children.Add(U.Note("Chase modunu açın. Simde kameranın uçağı hafifçe yukarıdan gördüğünü kontrol edin.", 14).With(n => n.Margin = new Thickness(0, 8, 0, 16)));

        var pics = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        pics.Children.Add(Illustration(true));
        pics.Children.Add(Illustration(false));
        _content.Children.Add(pics);

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(U.Button("Chase'i aç", () =>
        {
            _app.Engine.SetMode(CameraModeKind.Chase);
            if (!_app.Engine.Enabled) _app.Engine.SetEnabled(true);
        }, "ghost"));
        buttons.Children.Add(U.Button("Evet, doğru görünüyor", () => { _cameraAnswer = "ok"; _step = 3; Refresh(); }, "primary"));
        buttons.Children.Add(U.Button("Hayır, yukarı/aşağı ters", () =>
        {
            Cal.InvertCameraPitch = !Cal.InvertCameraPitch;
            _cameraAnswer = "fixed";
            _app.Save();
            Refresh();
        }));
        _content.Children.Add(buttons);

        if (_cameraAnswer == "fixed")
            _content.Children.Add(new Border
            {
                Background = U.B("OkSoft"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 6, 0, 0),
                Child = U.Text("Kamera pitch'i çevrildi. Simde tekrar kontrol edin.", 14, "OkText"),
            });
    }

    private static FrameworkElement Illustration(bool correct)
    {
        var c = new Canvas { Width = 200, Height = 110, ClipToBounds = true };
        double horizon = correct ? 44 : 96;
        c.Children.Add(new Rectangle { Width = 200, Height = horizon, Fill = U.Hex("#1B2633") });
        var ground = new Rectangle { Width = 200, Height = 110 - horizon, Fill = U.Hex("#1C211B") };
        Canvas.SetTop(ground, horizon);
        c.Children.Add(ground);
        var plane = Geometry.Parse(correct ? "M100 54l-38 14v4l38-8 38 8v-4z M96 54a4 4 0 0 1 8 0v26a4 4 0 0 1 -8 0z M100 80l-12 6v3l12-3 12 3v-3z" : "M100 98l-30 10v2h60v-2z");
        c.Children.Add(new Path { Data = plane, Fill = U.Hex(correct ? "#C8CDD6" : "#8A919E") });
        System.Windows.Automation.AutomationProperties.SetName(c, correct ? "Doğru: uçak ortada, ufuk üstte" : "Yanlış: kamera gökyüzüne bakıyor");

        return new Border
        {
            Background = U.B("Bg"),
            BorderBrush = correct ? U.B("Ok") : U.B("Line"),
            BorderThickness = new Thickness(correct ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new Viewbox { Child = c, Height = 110 },
                    U.Text(correct ? "Böyle görünmeli" : "Gökyüzüne bakıyorsa: ters", 14, correct ? "OkText" : "BadText", FontWeights.SemiBold).With(t => t.Margin = new Thickness(0, 10, 0, 0)),
                },
            },
        };
    }

    private void Step3()
    {
        _content.Children.Add(U.Title("3 · Kamera uçakla birlikte dönüyor mu?", 19));
        _content.Children.Add(U.Note("Hafif bir dönüş yapın. Kamera uçağın arkasında kalmalı, ufuk düz durmalı.", 14).With(n => n.Margin = new Thickness(0, 8, 0, 16)));
        _content.Children.Add(U.Text("Yön gönderme yöntemi", 14, "Fg", FontWeights.Medium).With(t => t.Margin = new Thickness(0, 0, 0, 8)));

        void Method(RotationOutputMode mode, string name, string note)
        {
            bool on = Cal.RotationMode == mode;
            var radio = new RadioButton
            {
                GroupName = "rotation",
                IsChecked = on,
                Content = new StackPanel { Margin = new Thickness(6, 0, 0, 0), Children = { U.Text(name, 14, "Fg", FontWeights.SemiBold), U.Text(note, 13, "FgDim", wrap: true) } },
            };
            radio.Checked += (_, _) => { Cal.RotationMode = mode; _app.Save(); BuildContent(); };
            _content.Children.Add(new Border
            {
                Background = U.B(on ? "AccentSoft" : "Item"),
                BorderBrush = on ? U.B("Accent") : U.Hex("#262B35"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = radio,
            });
        }
        Method(RotationOutputMode.WorldPbh, "Dünya açıları (önerilen)", "Ufuk her zaman düz kalır; yatışı kamera ayarı belirler.");
        Method(RotationOutputMode.TargetedSimObject, "Uçağa göre bakış noktası (yedek)", "Kamera ters veya yanlış yöne bakıyorsa bunu deneyin.");

        var bank = new CheckBox { Content = "Kamera yatışı ters (ufuk yanlış yöne eğiliyor)", IsChecked = Cal.InvertCameraBank, Margin = new Thickness(0, 6, 0, 0) };
        bank.Checked += (_, _) => { Cal.InvertCameraBank = true; _app.Save(); };
        bank.Unchecked += (_, _) => { Cal.InvertCameraBank = false; _app.Save(); };
        _content.Children.Add(bank);

        if (_finished)
            _content.Children.Add(new Border
            {
                Background = U.B("OkSoft"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0),
                Child = U.Text("Kalibrasyon tamamlandı ve kaydedildi.", 14, "OkText"),
            });
    }
}
