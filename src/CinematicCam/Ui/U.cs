using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CinematicCam.Ui;

/// <summary>Arayüzü koddan kurmak için küçük, temaya uygun yapı taşları.</summary>
public static class U
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    public static FontFamily Mono => (FontFamily)Application.Current.FindResource("MonoFont");

    public static Brush Hex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    // ---------------- Metin ----------------

    public static TextBlock Text(string text, double size = 14, string color = "Fg", FontWeight? weight = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = B(color),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Kart başlığı gibi küçük, büyük harfli etiket.</summary>
    public static TextBlock Overline(string text) => new()
    {
        Text = text.ToUpper(new System.Globalization.CultureInfo("tr-TR")),
        FontSize = 11.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = B("FgDim"),
        Margin = new Thickness(0, 0, 0, 10),
    };

    public static TextBlock Title(string text, double size = 17) => Text(text, size, "Fg", FontWeights.SemiBold);

    public static TextBlock Note(string text, double size = 13) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = B("FgDim"),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.5,
    };

    public static Border Kbd(string text) => new()
    {
        Background = B("Bg"),
        BorderBrush = B("Line"),
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(6, 1, 6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontFamily = Mono, FontSize = 11.5, Foreground = B("FgDim") },
    };

    // ---------------- Kaplar ----------------

    public static Border Card(UIElement child, double padding = 16) => new()
    {
        Background = B("Card"),
        BorderBrush = B("Line"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding),
        Child = child,
    };

    public static StackPanel Stack(double gap = 0, params UIElement[] children) => Stack(Orientation.Vertical, gap, children);

    public static StackPanel Row(double gap = 0, params UIElement[] children) => Stack(Orientation.Horizontal, gap, children);

    private static StackPanel Stack(Orientation o, double gap, UIElement[] children)
    {
        var panel = new StackPanel { Orientation = o };
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0 && gap > 0)
            {
                var m = fe.Margin;
                fe.Margin = o == Orientation.Vertical ? new Thickness(m.Left, m.Top + gap, m.Right, m.Bottom) : new Thickness(m.Left + gap, m.Top, m.Right, m.Bottom);
            }
            panel.Children.Add(children[i]);
        }
        return panel;
    }

    /// <summary>Kartın içindeki gri "kutucuk": etiket üstte, değer altta.</summary>
    public static Border Tile(string label, string value, bool mono = false, string valueColor = "Fg", TextBlock? valueBlock = null)
    {
        var v = valueBlock ?? new TextBlock();
        v.Text = value;
        v.FontSize = mono ? 13 : 16;
        v.FontWeight = mono ? FontWeights.Normal : FontWeights.SemiBold;
        v.Foreground = B(valueColor);
        v.Margin = new Thickness(0, 2, 0, 0);
        if (mono)
        {
            v.FontFamily = Mono;
            v.TextWrapping = TextWrapping.Wrap;
        }
        else v.TextTrimming = TextTrimming.CharacterEllipsis;
        return new Border
        {
            Background = B("Raised"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new StackPanel { Children = { Text(label, 12, "FgDim"), v } },
        };
    }

    public static Border Pill(string text, string bg, string fg, string? dot = null, bool mono = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot != null) row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = B(dot), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var t = Text(text, mono ? 12 : 13, fg);
        if (mono) t.FontFamily = Mono;
        row.Children.Add(t);
        return new Border { Background = B(bg), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), Child = row };
    }

    // ---------------- Düğmeler ----------------

    public static Button Button(object content, Action onClick, string style = "default")
    {
        var b = new Button { Content = content is string s ? new TextBlock { Text = s, FontSize = 14 } : content, Margin = new Thickness(0, 0, 8, 8) };
        switch (style)
        {
            case "primary":
                b.Background = B("Accent");
                b.BorderBrush = B("Accent");
                b.Foreground = B("AccentInk");
                if (b.Content is TextBlock tb) { tb.Foreground = B("AccentInk"); tb.FontWeight = FontWeights.Bold; }
                break;
            case "ghost":
                b.Background = Brushes.Transparent;
                break;
            case "danger":
                b.Background = Brushes.Transparent;
                b.BorderBrush = Hex("#4A2A2D");
                if (b.Content is TextBlock td) td.Foreground = B("BadText");
                break;
            case "link":
                b.Background = Brushes.Transparent;
                b.BorderThickness = new Thickness(0);
                b.Padding = new Thickness(0);
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                if (b.Content is TextBlock tl) tl.Foreground = B("Info");
                break;
        }
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Seçili/seçisiz görünen hap düğme.</summary>
    public static Button Chip(string text, bool selected, Action onClick, string? prefix = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (prefix != null)
        {
            var p = Text(prefix, 11, selected ? "AccentInk" : "FgFaint");
            p.FontFamily = Mono;
            p.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(p);
        }
        row.Children.Add(Text(text, 13, selected ? "AccentInk" : "Fg", selected ? FontWeights.SemiBold : FontWeights.Normal));
        var b = new Button
        {
            Content = row,
            Background = B(selected ? "Accent" : "Raised"),
            BorderBrush = B(selected ? "Accent" : "Line"),
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 8),
        };
        AutomationProps(b, text, selected);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Tek seçimli segment grubu (ör. sarsıntı seviyesi, Dış/İç).</summary>
    public static Border Segmented<T>(IEnumerable<(T Value, string Label)> options, T selected, Action<T> onPick) where T : notnull
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in options)
        {
            bool sel = EqualityComparer<T>.Default.Equals(value, selected);
            var b = new Button
            {
                Content = Text(label, 13, sel ? "AccentInk" : "FgDim", FontWeights.SemiBold),
                Background = sel ? B("Accent") : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 88,
                Margin = new Thickness(0, 0, 4, 0),
            };
            AutomationProps(b, label, sel);
            var v = value;
            b.Click += (_, _) => onPick(v);
            row.Children.Add(b);
        }
        return new Border
        {
            Background = B("Bg"),
            BorderBrush = B("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(4, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
        };
    }

    public static void AutomationProps(FrameworkElement e, string name, bool? selected = null)
    {
        System.Windows.Automation.AutomationProperties.SetName(e, selected == true ? name + " (seçili)" : name);
    }

    // ---------------- Kaydırıcı satırı ----------------

    /// <summary>Etiket + değer, kaydırıcı ve iki uçta sade açıklama ("Yakın — Uzak").</summary>
    public static FrameworkElement SliderRow(string label, string lo, string hi, double min, double max, double step,
        Func<double> get, Action<double> set, Func<double, string> format)
    {
        var value = Text(format(get()), 13, "AccentText");
        value.FontFamily = Mono;
        value.HorizontalAlignment = HorizontalAlignment.Right;

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = (max - min) / 10,
            TickFrequency = step,
            IsSnapToTickEnabled = step > 0,
            Value = Math.Clamp(get(), min, max),
            Margin = new Thickness(0, 6, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = format(e.NewValue);
        };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Text(label, 14, "Fg", FontWeights.Medium));
        Grid.SetColumn(value, 1);
        head.Children.Add(value);

        var ends = new Grid();
        ends.ColumnDefinitions.Add(new ColumnDefinition());
        ends.ColumnDefinitions.Add(new ColumnDefinition());
        ends.Children.Add(Text(lo, 12, "FgFaint"));
        var hiText = Text(hi, 12, "FgFaint");
        hiText.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(hiText, 1);
        ends.Children.Add(hiText);

        return new StackPanel { Margin = new Thickness(0, 0, 28, 20), Children = { head, slider, ends } };
    }

    // ---------------- Simgeler ----------------

    /// <summary>24×24 görünüm kutusunda çizgi simge (SVG yolu söz dizimi).</summary>
    public static System.Windows.Shapes.Path Icon(string data, string color = "Fg", double size = 22, double stroke = 1.8) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = B(color),
        StrokeThickness = stroke,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static class Icons
    {
        public const string Camera = "M4 6h11a1.5 1.5 0 0 1 1.5 1.5v9A1.5 1.5 0 0 1 15 18H4a1.5 1.5 0 0 1-1.5-1.5v-9A1.5 1.5 0 0 1 4 6z M16.5 10.5l5-3v9l-5-3";
        public const string Chase = "M12 3v9 M5 9l7 3 7-3 M10 15h4 M12 18.4a1.6 1.6 0 1 0 0.01 0";
        public const string Flyby = "M3 20C8 8 16 8 21 4 M18 14a2 2 0 1 0 0.01 0 M16.5 14.5L13 11";
        public const string Orbit = "M3 12a9 5 0 1 0 18 0a9 5 0 1 0 -18 0 M12 10.2a1.8 1.8 0 1 0 0.01 0 M19 8.5l2.2-.6-.4 2.3";
        public const string Inside = "M3 15c2-6 6-9 9-9s7 3 9 9 M3 15h18 M12 6v9 M8 17.5a1.5 1.5 0 1 0 0.01 0 M16 17.5a1.5 1.5 0 1 0 0.01 0";
        public const string Cinematic = "M4.5 9h15A1.5 1.5 0 0 1 21 10.5v8A1.5 1.5 0 0 1 19.5 20h-15A1.5 1.5 0 0 1 3 18.5v-8A1.5 1.5 0 0 1 4.5 9z M3 9l2.5-5h15L18 9 M9 4l-2 5 M14 4l-2 5";
        public const string Download = "M12 4v11 M7 10l5 5 5-5 M4 20h16";
        public const string Plane = "M12 2l2 7h7l-6 4 2 8-5-5-5 5 2-8-6-4h7z";
    }

    // ---------------- Klavye kısayolu yakalama ----------------

    /// <summary>Tıklayıp tuşlara basınca kombinasyonu yakalayan kutu. Esc iptal, Backspace/Delete temizler.</summary>
    public static TextBox KeyCaptureBox(string current, Action<string> onChanged)
    {
        var box = new TextBox
        {
            Text = PrettyKey(current),
            IsReadOnly = true,
            FontFamily = Mono,
            FontSize = 12.5,
            Width = 190,
            Cursor = Cursors.Hand,
            IsReadOnlyCaretVisible = false,
            ToolTip = "Tıklayın ve yeni tuş kombinasyonuna basın. Backspace temizler.",
        };
        string before = box.Text;
        box.GotKeyboardFocus += (_, _) => { before = box.Text; box.Text = "Tuşlara basın…"; };
        box.LostKeyboardFocus += (_, _) => { if (box.Text == "Tuşlara basın…") box.Text = before; };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key == Key.Escape) { box.Text = before; Keyboard.ClearFocus(); return; }
            string combo = key is Key.Back or Key.Delete ? "" : Combo(Keyboard.Modifiers, key);
            box.Text = PrettyKey(combo);
            before = box.Text;
            onChanged(combo);
            Keyboard.ClearFocus();
        };
        return box;
    }

    public static string Combo(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>"Ctrl+Shift+D1" → "Ctrl+Shift+1" gibi okunur hâle getirir.</summary>
    public static string PrettyKey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return "—";
        return string.Join("+", combo.Split('+').Select(p => p switch
        {
            _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1].ToString(),
            "PageUp" or "Prior" => "PgUp",
            "PageDown" or "Next" => "PgDn",
            "OemMinus" => "−",
            "OemPlus" => "=",
            "Left" => "←",
            "Right" => "→",
            "Up" => "↑",
            "Down" => "↓",
            _ => p,
        }));
    }
}
