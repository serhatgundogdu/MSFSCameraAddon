using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CinematicCam.Camera;
using CinematicCam.Input;
using CinematicCam.Interop;
using CinematicCam.Settings;
using CinematicCam.Sim;
using CinematicCam.Ui;

namespace CinematicCam;

public partial class MainWindow : Window
{
    private const double RotateStepDeg = 7.5;
    private const double ZoomStep = 1.12;
    private const string PresetActionPrefix = "Preset:";

    private static readonly (PageId Id, string Label)[] NavItems =
    {
        (PageId.Camera, "Kamera"),
        (PageId.Presets, "Preset'ler"),
        (PageId.Calibration, "Kalibrasyon"),
        (PageId.Hotkeys, "Kısayollar"),
        (PageId.Log, "Günlük"),
    };

    private readonly AppSettings _settings;
    private readonly SimConnectClient _client;
    private readonly CameraEngine _engine;
    private readonly AppServices _app;
    private readonly DispatcherTimer _uiTimer;
    private readonly Dictionary<PageId, IPage> _pages = new();
    private HotkeyManager? _hotkeys;
    private PageId _page = PageId.Camera;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        _client = new SimConnectClient();
        _engine = new CameraEngine(_client, _settings);
        _app = new AppServices
        {
            Settings = _settings,
            Client = _client,
            Engine = _engine,
            Save = TrySave,
            RegisterHotkeys = RegisterHotkeys,
            Navigate = Navigate,
            OpenImport = OpenImport,
        };

        _client.ConnectionChanged += s => Dispatcher.BeginInvoke(() => UpdateHeader());
        _client.CameraStateChanged += _ => Dispatcher.BeginInvoke(() => UpdateHeader());
        _client.Log += msg => Dispatcher.BeginInvoke(() => _app.Log(msg));
        _engine.EnabledChanged += _ => Dispatcher.BeginInvoke(UpdateHeader);
        _engine.ProfileChanged += () => Dispatcher.BeginInvoke(() =>
        {
            UpdateHeader();
            RegisterHotkeys();
            if (_engine.Profile != null) _app.Log($"Uçak profili etkin: {_engine.Profile.DisplayName} ({_engine.Profile.Presets.Count} preset)");
            CurrentPage.Refresh();
        });

        Navigate(PageId.Camera);
        UpdateHeader();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _uiTimer.Tick += (_, _) =>
        {
            var t = _client.State == ConnectionState.Connected ? _engine.Latest : null;
            CurrentPage.Tick(t);
            UpdateTelemetry(t);
        };
        _uiTimer.Start();

        SourceInitialized += (_, _) =>
        {
            _hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle);
            _hotkeys.Pressed += OnHotkey;
            RegisterHotkeys();
        };

        _client.Start();
        _app.Log("CinematicCam başlatıldı. MSFS 2024 bekleniyor…");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _uiTimer.Stop();
        _hotkeys?.Dispose();
        _engine.SetEnabled(false);
        _client.Dispose();
        TrySave();
        base.OnClosing(e);
    }

    private IPage CurrentPage => _pages[_page];

    // ---------------- Gezinme ----------------

    private void Navigate(PageId id)
    {
        _page = id;
        if (!_pages.TryGetValue(id, out var page))
        {
            page = id switch
            {
                PageId.Camera => new CameraPage(_app),
                PageId.Presets => new PresetsPage(_app),
                PageId.Calibration => new CalibrationPage(_app),
                PageId.Hotkeys => new HotkeysPage(_app),
                _ => new LogPage(_app),
            };
            _pages[id] = page;
        }
        else page.Refresh();

        PageHost.Content = page.View is ScrollViewer ? page.View : new ScrollViewer { Content = page.View, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        BuildNav();
    }

    private void BuildNav()
    {
        Nav.Children.Clear();
        foreach (var (id, label) in NavItems)
        {
            bool sel = id == _page;
            var b = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 14, FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal, Foreground = U.B(sel ? "Fg" : "FgDim") },
                Background = sel ? U.B("Raised") : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                MinHeight = 38,
            };
            U.AutomationProps(b, label, sel);
            var target = id;
            b.Click += (_, _) => Navigate(target);
            Nav.Children.Add(b);
        }
    }

    private void OpenImport()
    {
        var path = ImportWindow.PickFile(this);
        if (path == null) return;
        ImportWindow.Open(_app, path, this);
        CurrentPage.Refresh();
        UpdateHeader();
    }

    // ---------------- Üst bar ----------------

    private void UpdateHeader()
    {
        switch (_client.State)
        {
            case ConnectionState.Connected:
                ConnPill.Background = U.B("OkSoft");
                ConnDot.Fill = U.B("Ok");
                ConnText.Foreground = U.B("OkText");
                ConnText.Text = _client.CameraState switch
                {
                    CameraAvailability.AcquiredByOther => "Bağlı · kamera başka eklentide",
                    CameraAvailability.UserDisabled => "Bağlı · eklenti kamerası simde kapalı",
                    _ => "MSFS 2024 bağlı",
                };
                break;
            default:
                ConnPill.Background = U.B("WarnSoft");
                ConnDot.Fill = U.B("Accent");
                ConnText.Foreground = U.B("AccentText");
                ConnText.Text = _client.State == ConnectionState.Connecting ? "Bağlanıyor…" : "MSFS bekleniyor…";
                break;
        }

        var profile = _engine.Profile;
        string? aircraft = profile?.DisplayName ?? (_engine.LoadedAircraftPath != null ? AircraftProfile.KeyFromCfgPath(_engine.LoadedAircraftPath) : null);
        AircraftPill.Visibility = aircraft != null && _client.State == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
        AircraftText.Text = "✈  " + aircraft;

        bool on = _engine.Enabled;
        ToggleButton.IsEnabled = on || _client.State == ConnectionState.Connected;
        ToggleButton.Background = U.B(on ? "Accent" : "Raised");
        ToggleButton.BorderBrush = U.B(on ? "Accent" : "Line");
        ToggleText.Text = on ? (_client.CameraAcquired ? "Kamera açık" : "Açılıyor…") : "Kamerayı Aç";
        ToggleText.Foreground = U.B(on ? "AccentInk" : "Fg");
        ToggleDot.Fill = on ? U.B("AccentInk") : Brushes.Transparent;
        ToggleDot.Stroke = on ? null : U.B("FgFaint");
        ToggleDot.StrokeThickness = on ? 0 : 2;
    }

    private void UpdateTelemetry(Telemetry? t)
    {
        TelemetryText.Inlines.Clear();
        if (t == null)
        {
            TelemetryText.Text = "Telemetri yok — MSFS'te bir uçuş başlatın.";
            return;
        }
        var a = t.Aircraft;
        void Item(string label, string value)
        {
            TelemetryText.Inlines.Add(new Run(label + " "));
            TelemetryText.Inlines.Add(new Run(value) { Foreground = U.B("Fg") });
            TelemetryText.Inlines.Add(new Run("     "));
        }
        string Sign(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0") + "°";
        Item("Baş", $"{a.HeadingDeg:000}°");
        Item("Pitch", Sign(a.PitchDeg));
        Item("Bank", Sign(a.BankDeg));
        Item("TAS", $"{a.TrueAirspeedKts:0} kt");
        Item("AGL", $"{a.AltAboveGroundM:#,0} m");
        Item("Durum", a.OnGround ? "Yerde" : "Havada");
        Item("Güncelleme", $"{t.FrameRate:0} Hz");
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => ToggleCamera();

    private void ToggleCamera()
    {
        if (!_engine.Enabled && _client.State != ConnectionState.Connected)
        {
            _app.Log("Önce MSFS'e bağlanılmalı.");
            return;
        }
        _engine.SetEnabled(!_engine.Enabled);
        UpdateHeader();
    }

    // ---------------- Kısayollar ----------------

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        var bindings = new Dictionary<string, string>(_settings.Hotkeys);
        foreach (var p in _engine.Profile?.Presets ?? new List<CameraPreset>())
            if (p.Hotkey.Length > 0) bindings[PresetActionPrefix + p.Id] = p.Hotkey;

        var failed = _hotkeys.RegisterAll(bindings);
        string msg = failed.Count == 0
            ? $"Tüm kısayollar etkin ({bindings.Count(b => b.Value.Length > 0)})."
            : "Kaydedilemeyen kısayollar (başka uygulama kullanıyor veya geçersiz): " + string.Join(", ", failed);
        if (_pages.TryGetValue(PageId.Hotkeys, out var page) && page is HotkeysPage hp) hp.SetStatus(msg);
        if (failed.Count > 0) _app.Log(msg);
    }

    private void OnHotkey(string action)
    {
        switch (action)
        {
            case "ToggleCamera": ToggleCamera(); break;
            case "ModeChase": _engine.SetMode(CameraModeKind.Chase); break;
            case "ModeFlyby": _engine.SetMode(CameraModeKind.Flyby); break;
            case "ModeOrbit": _engine.SetMode(CameraModeKind.Orbit); break;
            case "ModeInside": _engine.SetMode(CameraModeKind.Inside); break;
            case "ModeCinematic": _engine.SetMode(CameraModeKind.Cinematic); break;
            case "NextShot": _engine.Next(); break;
            case "RotateLeft": _engine.Rotate(-RotateStepDeg, 0); break;
            case "RotateRight": _engine.Rotate(RotateStepDeg, 0); break;
            case "RotateUp": _engine.Rotate(0, RotateStepDeg * 0.6); break;
            case "RotateDown": _engine.Rotate(0, -RotateStepDeg * 0.6); break;
            case "ZoomIn": _engine.Zoom(1 / ZoomStep); break;
            case "ZoomOut": _engine.Zoom(ZoomStep); break;
            case "ResetView": _engine.Manual.Reset(); break;
            case "ToggleShake":
                _settings.Shake.Enabled = !_settings.Shake.Enabled;
                _app.Log("Sarsıntı " + (_settings.Shake.Enabled ? "açık" : "kapalı"));
                CurrentPage.Refresh();
                break;
            default:
                if (action.StartsWith(PresetActionPrefix))
                {
                    var id = action[PresetActionPrefix.Length..];
                    var preset = _engine.Profile?.Presets.FirstOrDefault(p => p.Id == id);
                    if (preset != null) _engine.ApplyCameraPreset(preset);
                }
                else if (action.StartsWith("LoadPreset") && int.TryParse(action[10..], out int load)) LoadSlot(load - 1);
                else if (action.StartsWith("SavePreset") && int.TryParse(action[10..], out int save)) SaveSlot(save - 1);
                break;
        }
    }

    private void SaveSlot(int slot)
    {
        var name = _settings.Presets[slot]?.Name ?? $"Slot {slot + 1}";
        _settings.Presets[slot] = _engine.CapturePreset(name);
        _app.Log($"Slot {slot + 1} kaydedildi.");
        TrySave();
    }

    private void LoadSlot(int slot)
    {
        var preset = _settings.Presets[slot];
        if (preset == null)
        {
            _app.Log($"Slot {slot + 1} boş.");
            return;
        }
        _engine.ApplyPreset(preset);
        _app.Log($"Slot {slot + 1} yüklendi: {preset.Name}");
        CurrentPage.Refresh();
    }

    private void TrySave()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) { _app.Log("Ayarlar kaydedilemedi: " + ex.Message); }
    }
}
