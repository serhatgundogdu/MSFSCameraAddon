using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CinematicCam.Camera;
using CinematicCam.Settings;

namespace CinematicCam.Ui;

/// <summary>Gruplanmış kısayol listesi; satırdaki kutuya tıklayıp yeni tuşlara basılarak değiştirilir.</summary>
public sealed class HotkeysPage : IPage
{
    private static readonly (string Group, string Note, (string Action, string Label)[] Rows)[] Groups =
    {
        ("Genel", "En sık kullanılanlar", new[]
        {
            ("ToggleCamera", "Kamerayı aç / kapat"),
            ("NextShot", "Sonraki plan / görünüm / flyby noktası"),
            ("ToggleShake", "Sarsıntıyı aç / kapat"),
            ("ResetView", "Görünümü sıfırla"),
        }),
        ("Modlar", "Ana ekrandaki mod kartlarıyla aynı sıra", new[]
        {
            ("ModeChase", "Chase"),
            ("ModeFlyby", "Flyby"),
            ("ModeOrbit", "Orbit"),
            ("ModeInside", "İç kamera"),
            ("ModeCinematic", "Sinematik"),
        }),
        ("Kamera hareketi", "Her basışta bir adım", new[]
        {
            ("RotateLeft", "Sola döndür"),
            ("RotateRight", "Sağa döndür"),
            ("RotateUp", "Yukarı"),
            ("RotateDown", "Aşağı"),
            ("ZoomIn", "Yaklaş"),
            ("ZoomOut", "Uzaklaş"),
        }),
    };

    private readonly AppServices _app;
    private readonly ScrollViewer _root = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _status = U.Note("");
    private AircraftProfile? _shownProfile;

    public HotkeysPage(AppServices app)
    {
        _app = app;
        Refresh();
    }

    public FrameworkElement View => _root;

    public void Tick(Telemetry? telemetry)
    {
        if (!ReferenceEquals(_shownProfile, _app.Engine.Profile)) Refresh();
    }

    public void SetStatus(string text) => _status.Text = text;

    public void Refresh()
    {
        _shownProfile = _app.Engine.Profile;
        var s = _app.Settings;
        var page = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var reset = U.Button("Varsayılanlara dön", () =>
        {
            s.Hotkeys = HotkeyDefaults.Create();
            _app.Save();
            _app.RegisterHotkeys();
            Refresh();
        });
        DockPanel.SetDock(reset, Dock.Right);
        head.Children.Add(reset);
        head.Children.Add(new StackPanel
        {
            Children =
            {
                U.Title("Kısayollar", 24),
                U.Note("MSFS penceresi öndeyken de çalışır. Değiştirmek için kutuya tıklayıp yeni tuşlara basın; Backspace temizler, Esc vazgeçer.", 14),
            },
        });
        page.Children.Add(head);
        _status.Margin = new Thickness(0, 0, 0, 12);
        page.Children.Add(_status);

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (group, note, rows) in Groups)
            grid.Children.Add(GroupCard(group, note, rows.Select(r => Row(r.Label, s.Hotkeys.GetValueOrDefault(r.Action, ""), c => SetAction(r.Action, c)))));

        grid.Children.Add(GroupCard("Preset slotları", "Kamera ayarlarını 9 slota kaydedip geri yükleyin", Enumerable.Range(1, 9).SelectMany(i => new[]
        {
            Row($"Slot {i} yükle", s.Hotkeys.GetValueOrDefault($"LoadPreset{i}", ""), c => SetAction($"LoadPreset{i}", c)),
            Row($"Slot {i}'e kaydet", s.Hotkeys.GetValueOrDefault($"SavePreset{i}", ""), c => SetAction($"SavePreset{i}", c)),
        })));

        var profile = _app.Engine.Profile;
        if (profile != null)
        {
            var presetRows = profile.Presets.OrderBy(p => p.Kind.IsInside() ? 0 : 1).Select(p => Row($"{p.Name}  ·  {p.Kind.Label()}", p.Hotkey, c =>
            {
                p.Hotkey = c;
                _app.Save();
                _app.RegisterHotkeys();
            }));
            grid.Children.Add(GroupCard("Uçak görünümleri", $"{profile.DisplayName} — {profile.Source}", presetRows));
        }
        page.Children.Add(grid);
        _root.Content = page;
    }

    private void SetAction(string action, string combo)
    {
        _app.Settings.Hotkeys[action] = combo;
        _app.Save();
        _app.RegisterHotkeys();
    }

    private static FrameworkElement Row(string label, string combo, Action<string> onChanged)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), MinHeight = 40 };
        var box = U.KeyCaptureBox(combo, onChanged);
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(box);
        row.Children.Add(U.Text(label, 14).With(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        return row;
    }

    private static FrameworkElement GroupCard(string title, string note, IEnumerable<FrameworkElement> rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(U.Title(title, 16));
        panel.Children.Add(U.Note(note, 12.5).With(n => n.Margin = new Thickness(0, 2, 0, 10)));
        foreach (var r in rows) panel.Children.Add(r);
        var card = U.Card(panel);
        card.Margin = new Thickness(0, 0, 18, 18);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }
}

/// <summary>Günlük ve ayar dosyası.</summary>
public sealed class LogPage : IPage
{
    private readonly AppServices _app;
    private readonly TextBox _box;
    private readonly FrameworkElement _root;

    public LogPage(AppServices app)
    {
        _app = app;
        _box = new TextBox
        {
            IsReadOnly = true,
            FontFamily = U.Mono,
            FontSize = 12.5,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10),
            Height = 440,
        };
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(U.Button("Ayarları kaydet", () => { _app.Save(); _app.Log("Ayarlar kaydedildi: " + SettingsStore.FilePath); }));
        buttons.Children.Add(U.Button("Ayar klasörünü aç", () =>
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", SettingsStore.Folder) { UseShellExecute = true });
        }, "ghost"));
        var path = U.Note("Ayar dosyası: " + SettingsStore.FilePath);
        path.Margin = new Thickness(0, 8, 0, 0);
        _root = U.Card(new StackPanel { Children = { U.Title("Günlük"), _box.With(b => b.Margin = new Thickness(0, 12, 0, 0)), buttons, path } }, 18);
        _app.LogChanged += Refresh;
        Refresh();
    }

    public FrameworkElement View => _root;
    public void Tick(Telemetry? telemetry) { }

    public void Refresh()
    {
        _box.Text = _app.LogText;
        _box.ScrollToEnd();
    }
}
