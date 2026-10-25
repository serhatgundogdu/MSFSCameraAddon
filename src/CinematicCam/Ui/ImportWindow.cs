using System.IO;
using System.Windows;
using System.Windows.Controls;
using CinematicCam.Import;
using CinematicCam.Settings;

namespace CinematicCam.Ui;

/// <summary>ChasePlane export zip'ini gözden geçirip içe aktarma penceresi.</summary>
public sealed class ImportWindow : Window
{
    private readonly AppServices _app;
    private readonly string _path;
    private readonly List<ImportedAircraft> _aircraft;
    private readonly StackPanel _body = new();
    private ImportedAircraft _current;
    private readonly HashSet<PresetKind> _kinds = Enum.GetValues<PresetKind>().ToHashSet();
    private UnnamedHandling _unnamed = UnnamedHandling.AutoName;
    private bool _hotkeys = true;
    private string _prefix = "Alt+Shift";
    private bool _autoActivate = true;
    private string? _targetKey;

    /// <summary>İçe aktarılan preset sayısı (iptalde 0).</summary>
    public int Imported { get; private set; }

    public ImportWindow(AppServices app, string zipPath, List<ImportedAircraft> aircraft)
    {
        _app = app;
        _path = zipPath;
        _aircraft = aircraft;
        _current = aircraft[0];

        Title = "ChasePlane preset'lerini içe aktar";
        Width = 1040;
        Height = 820;
        MinWidth = 820;
        MinHeight = 600;
        Background = U.B("Bg");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = _body, Padding = new Thickness(24), Background = U.B("Bg"), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Build();
    }

    private void Build()
    {
        _body.Children.Clear();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var steps = U.Row(6, U.Pill("✓ 1 · Dosya", "OkSoft", "OkText"), U.Pill("2 · Gözden geçir", "Accent", "AccentInk"), U.Pill("3 · Bitti", "Raised", "FgDim"));
        DockPanel.SetDock(steps, Dock.Right);
        head.Children.Add(steps);
        head.Children.Add(U.Title("ChasePlane preset'lerini içe aktar", 24));
        _body.Children.Add(head);

        // Dosya
        var file = new DockPanel();
        var change = U.Button("Başka dosya seç", ChooseOther);
        change.Margin = new Thickness(0);
        DockPanel.SetDock(change, Dock.Right);
        file.Children.Add(change);
        long size = new FileInfo(_path).Length;
        file.Children.Add(new StackPanel
        {
            Children =
            {
                U.Text(Path.GetFileName(_path), 14, "Fg", FontWeights.SemiBold),
                U.Text($"{size / 1024.0:0} KB · {_aircraft.Count} uçak · {_aircraft.Sum(a => a.Presets.Count)} preset", 13, "FgDim"),
            },
        });
        var fileCard = U.Card(file);
        fileCard.BorderBrush = U.Hex("#3A4250");
        fileCard.Margin = new Thickness(0, 0, 0, 18);
        _body.Children.Add(fileCard);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        var left = U.Stack(18, AircraftCard(), MappingCard(), OptionsCard());
        left.Margin = new Thickness(0, 0, 18, 0);
        var right = SummaryCard();
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        _body.Children.Add(grid);
    }

    private FrameworkElement AircraftCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(U.Title("Uçak"));
        if (_aircraft.Count > 1)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var a in _aircraft)
            {
                var item = a;
                chips.Children.Add(U.Chip($"{a.DisplayName} · {a.Presets.Count}", a == _current, () => { _current = item; _targetKey = null; Build(); }));
            }
            panel.Children.Add(chips);
        }

        var info = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        info.Children.Add(U.Text(_current.DisplayName, 18, "Fg", FontWeights.SemiBold));
        var meta = U.Text(_current.Key + (_current.CfgPath != null ? " · " + _current.CfgPath : ""), 12, "FgDim", wrap: true);
        meta.FontFamily = U.Mono;
        info.Children.Add(meta);
        panel.Children.Add(info);

        var loaded = _app.Engine.LoadedAircraftPath;
        var probe = new AircraftProfile { Key = _current.Key, CfgPath = _current.CfgPath };
        if (loaded != null)
            panel.Children.Add((probe.Matches(loaded)
                ? U.Pill("Simdeki uçakla aynı", "OkSoft", "OkText", "Ok")
                : U.Pill("Simdeki uçak farklı: " + AircraftProfile.KeyFromCfgPath(loaded), "WarnSoft", "AccentText", "Accent")).With(p => p.HorizontalAlignment = HorizontalAlignment.Left));

        panel.Children.Add(U.Text("Hangi profile eklensin?", 13, "FgDim").With(t => t.Margin = new Thickness(0, 12, 0, 4)));
        var existingOwn = _app.Settings.Profiles.FirstOrDefault(p => p.Key.Equals(_current.Key, StringComparison.OrdinalIgnoreCase));
        AddTarget(panel, null, existingOwn == null ? $"Yeni profil: {_current.DisplayName}" : $"{_current.DisplayName} profiline ekle / güncelle");
        foreach (var other in _app.Settings.Profiles.Where(p => p != existingOwn))
            AddTarget(panel, other.Key, $"{other.DisplayName} profiline ekle");

        var auto = new CheckBox { Content = "Bu uçak simde yüklendiğinde profili otomatik etkinleştir", IsChecked = _autoActivate, Margin = new Thickness(0, 6, 0, 0) };
        auto.Checked += (_, _) => _autoActivate = true;
        auto.Unchecked += (_, _) => _autoActivate = false;
        panel.Children.Add(auto);
        return U.Card(panel, 18);
    }

    private void AddTarget(Panel panel, string? key, string label)
    {
        var r = new RadioButton { Content = label, GroupName = "target", IsChecked = _targetKey == key };
        r.Checked += (_, _) => _targetKey = key;
        panel.Children.Add(r);
    }

    private FrameworkElement MappingCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(U.Title("Neler aktarılacak?"));
        panel.Children.Add(U.Note("Her ChasePlane türü, CinematicCam'deki karşılığına dönüştürülür.").With(n => n.Margin = new Thickness(0, 4, 0, 12)));

        foreach (var kind in Enum.GetValues<PresetKind>())
        {
            int count = _current.Count(kind);
            if (count == 0) continue;
            bool on = _kinds.Contains(kind);
            var check = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            U.AutomationProps(check, kind.Label() + " preset'lerini aktar");
            var k = kind;
            check.Checked += (_, _) => { _kinds.Add(k); Build(); };
            check.Unchecked += (_, _) => { _kinds.Remove(k); Build(); };

            var row = new Grid { MinHeight = 52 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            var from = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { U.Text(kind.Label(), 14, "Fg", FontWeights.SemiBold), U.Text(kind.IsInside() ? "İç kamera" : "Dış kamera", 12, "FgDim") } };
            var arrow = U.Text("→", 14, "FgFaint");
            var to = U.Text(kind.ModeLabel(), 14, "AccentText", FontWeights.SemiBold);
            var n = U.Text(count.ToString(), 14);
            n.FontFamily = U.Mono;
            n.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(from, 1);
            Grid.SetColumn(arrow, 2);
            Grid.SetColumn(to, 3);
            Grid.SetColumn(n, 4);
            row.Children.Add(check);
            row.Children.Add(from);
            row.Children.Add(arrow);
            row.Children.Add(to);
            row.Children.Add(n);
            panel.Children.Add(new Border
            {
                Background = U.B(on ? "Item" : "Bg"),
                Opacity = on ? 1 : 0.55,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 6),
                Child = row,
            });
        }
        return U.Card(panel, 18);
    }

    private FrameworkElement OptionsCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(U.Title("Seçenekler"));

        int unnamed = _current.Presets.Count(p => p.Unnamed);
        if (unnamed > 0)
        {
            panel.Children.Add(U.Text($"İsimsiz preset'ler ({unnamed})", 14, "Fg", FontWeights.Medium).With(t => t.Margin = new Thickness(0, 12, 0, 4)));
            void Opt(UnnamedHandling value, string label, string note)
            {
                var r = new RadioButton { GroupName = "unnamed", IsChecked = _unnamed == value, Content = U.Row(6, U.Text(label), U.Text(note, 13, "FgDim")) };
                r.Checked += (_, _) => { _unnamed = value; Build(); };
                panel.Children.Add(r);
            }
            Opt(UnnamedHandling.AutoName, "Konuma göre ad ver", "— ör. “Sol ön, yukarıdan”");
            Opt(UnnamedHandling.Keep, "Olduğu gibi bırak", "— temp1, temp2 …");
            Opt(UnnamedHandling.Skip, "Aktarma", $"— {unnamed} preset atlanır");
        }

        int keys = _current.Presets.Count(p => p.ChasePlaneKey != null);
        if (keys > 0)
        {
            var keysCheck = new CheckBox { Content = $"ChasePlane kısayollarını da al ({keys})", IsChecked = _hotkeys, Margin = new Thickness(0, 12, 0, 0) };
            keysCheck.Checked += (_, _) => _hotkeys = true;
            keysCheck.Unchecked += (_, _) => _hotkeys = false;
            panel.Children.Add(keysCheck);
            var sample = string.Join(", ", _current.Presets.Where(p => p.ChasePlaneKey != null).Select(p => U.PrettyKey(p.ChasePlaneKey!)).Distinct().Take(14));
            panel.Children.Add(U.Note($"ChasePlane tuşları: {sample}. Yalın tuşlar sistem genelinde yazmayı engelleyeceği için önlerine aşağıdaki değiştirici eklenir. Çakışanlar atlanıp listelenir.").With(n => n.Margin = new Thickness(28, 0, 0, 6)));
            var prefixRow = U.Row(10, U.Text("Önek", 14), new TextBox { Text = _prefix, Width = 160, FontFamily = U.Mono }.With(t => t.TextChanged += (_, _) => _prefix = t.Text.Trim().Trim('+')));
            prefixRow.Margin = new Thickness(28, 0, 0, 0);
            panel.Children.Add(prefixRow);
        }
        return U.Card(panel, 18);
    }

    private FrameworkElement SummaryCard()
    {
        var selected = _current.Presets.Where(p => _kinds.Contains(p.Preset.Kind) && !(p.Unnamed && _unnamed == UnnamedHandling.Skip)).ToList();
        int outCount = selected.Count(p => !p.Preset.Kind.IsInside());
        int inCount = selected.Count - outCount;

        var panel = new StackPanel();
        panel.Children.Add(U.Overline("Özet"));
        var tiles = new UniformGrid2(2);
        tiles.Add(U.Tile("Dış kamera", outCount.ToString()).With(t => ((TextBlock)((StackPanel)t.Child).Children[1]).FontSize = 24));
        tiles.Add(U.Tile("İç kamera", inCount.ToString()).With(t => ((TextBlock)((StackPanel)t.Child).Children[1]).FontSize = 24));
        tiles.Panel.Margin = new Thickness(0, 0, -8, 6);
        panel.Children.Add(tiles.Panel);

        foreach (var w in _current.Warnings)
        {
            var line = U.Row(10, U.Text("!", 14, "AccentText", FontWeights.Bold), U.Note(w).With(n => n.MaxWidth = 260));
            line.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(line);
        }
        if (_current.Presets.Any(p => p.Preset.Kind.IsInside()))
        {
            var info = U.Row(10, U.Text("i", 14, "Info", FontWeights.Bold), U.Note("İç kameralar pilot gözü noktasına göre kaydedilmiş; uçağın kendi kokpit konumunda doğru yerde kalırlar.").With(n => n.MaxWidth = 260));
            info.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(info);
        }

        var go = U.Button(selected.Count > 0 ? $"{selected.Count} preset'i içe aktar" : "Seçili preset yok", DoImport, "primary");
        go.IsEnabled = selected.Count > 0;
        go.MinHeight = 48;
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Margin = new Thickness(0, 18, 0, 8);
        var cancel = U.Button("Vazgeç", Close, "ghost");
        cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancel.Margin = new Thickness(0);
        panel.Children.Add(go);
        panel.Children.Add(cancel);
        var card = U.Card(panel, 18);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private void DoImport()
    {
        var options = new ImportOptions
        {
            Kinds = _kinds.ToHashSet(),
            Unnamed = _unnamed,
            ImportHotkeys = _hotkeys,
            HotkeyPrefix = _prefix,
            TargetProfileKey = _targetKey,
            AutoActivate = _autoActivate,
        };
        Imported = ChasePlaneImporter.Apply(_app.Settings, _current, options, out var skipped);
        _app.Save();
        _app.Engine.RefreshProfile();
        _app.RegisterHotkeys();
        _app.Log($"{_current.DisplayName}: {Imported} preset içe aktarıldı ({Path.GetFileName(_path)}).");
        string msg = $"{Imported} preset içe aktarıldı.";
        if (skipped.Count > 0)
        {
            msg += "\n\nÇakıştığı için atlanan kısayollar:\n" + string.Join("\n", skipped);
            _app.Log("Atlanan kısayollar: " + string.Join(", ", skipped));
        }
        MessageBox.Show(this, msg, "İçe aktarma tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void ChooseOther()
    {
        var path = PickFile(this);
        if (path == null) return;
        Close();
        Open(_app, path, Owner);
    }

    public static string? PickFile(Window? owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ChasePlane export veya CinematicCam profili seçin",
            Filter = "ChasePlane export (*.zip)|*.zip|CinematicCam profili (*.json)|*.json|Tüm dosyalar|*.*",
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>Dosya türüne göre içe aktarma: zip → önizleme penceresi, json → doğrudan profil.</summary>
    public static void Open(AppServices app, string path, Window? owner)
    {
        try
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                var profile = System.Text.Json.JsonSerializer.Deserialize<AircraftProfile>(File.ReadAllText(path), options)
                              ?? throw new InvalidDataException("Profil okunamadı.");
                app.Settings.Profiles.RemoveAll(p => p.Key.Equals(profile.Key, StringComparison.OrdinalIgnoreCase));
                app.Settings.Profiles.Add(profile);
                app.Save();
                app.Engine.RefreshProfile();
                app.RegisterHotkeys();
                app.Log($"Profil içe aktarıldı: {profile.DisplayName} ({profile.Presets.Count} preset).");
                return;
            }

            var aircraft = ChasePlaneImporter.Read(path);
            new ImportWindow(app, path, aircraft) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            string text = "Dosya içe aktarılamadı:\n" + ex.Message;
            if (owner != null) MessageBox.Show(owner, text, "İçe aktarma", MessageBoxButton.OK, MessageBoxImage.Warning);
            else MessageBox.Show(text, "İçe aktarma", MessageBoxButton.OK, MessageBoxImage.Warning);
            app.Log("İçe aktarma hatası: " + ex.Message);
        }
    }
}
