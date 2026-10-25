using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CinematicCam.Setup
{
    /// <summary>Kurulum ve kaldırma sihirbazı (uygulamayla aynı koyu tema).</summary>
    internal sealed class SetupWindow : Window
    {
        private readonly Payload _payload;
        private readonly ContentControl _body = new ContentControl();
        private readonly StackPanel _footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock _stepText = new TextBlock { FontSize = 12.5 };
        private string _installedDir;

        public SetupWindow(Payload payload)
        {
            _payload = payload;
            Title = payload != null ? $"CinematicCam {Installer.Version} Kurulumu" : "CinematicCam'i kaldır";
            Width = 660;
            Height = 590;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = B("Bg");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var icon = AppIcon();
            if (icon != null) Icon = icon;

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
            if (icon != null)
            {
                var img = new Image { Source = icon, Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0) };
                DockPanel.SetDock(img, Dock.Left);
                header.Children.Add(img);
            }
            _stepText.Foreground = B("FgDim");
            DockPanel.SetDock(_stepText, Dock.Right);
            _stepText.VerticalAlignment = VerticalAlignment.Top;
            header.Children.Add(_stepText);
            header.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "CinematicCam", FontSize = 22, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = $"MSFS 2024 sinematik kamera · sürüm {Installer.Version}", FontSize = 13, Foreground = B("FgDim") },
                },
            });

            var card = new Border
            {
                Background = B("Card"),
                BorderBrush = B("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22),
                Child = _body,
            };

            var root = new DockPanel { Margin = new Thickness(26, 24, 26, 22) };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(_footer, Dock.Bottom);
            _footer.Margin = new Thickness(0, 18, 0, 0);
            root.Children.Add(header);
            root.Children.Add(_footer);
            root.Children.Add(card);
            Content = root;

            if (payload != null) ShowWelcome();
            else ShowUninstallConfirm();
        }

        // ---------------- Kurulum ----------------

        private void ShowWelcome()
        {
            _stepText.Text = "1 / 3";
            var existing = Installer.FindExisting();
            var panel = new StackPanel();
            panel.Children.Add(Heading(existing != null ? "CinematicCam güncellenecek" : "CinematicCam'i kurmaya hazır"));
            panel.Children.Add(Note(existing != null
                ? $"Bu bilgisayarda {existing.Value.Version} sürümü kurulu. {Installer.Version} sürümüyle değiştirilecek; ayarlarınız, preset'leriniz ve kısayollarınız korunur."
                : "ChasePlane tarzı chase, flyby, orbit, iç kamera ve sinematik modlar. .NET dahil her şey kurulumun içinde; ek bir şey indirilmez."));

            panel.Children.Add(Label("Kurulum klasörü"));
            var pathBox = new TextBox { Text = existing?.Dir ?? Installer.DefaultInstallDir };
            var browse = Btn("Gözat…", () =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "CinematicCam'in kurulacağı klasörü seçin", SelectedPath = pathBox.Text })
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) pathBox.Text = Installer.NormalizeInstallDir(dialog.SelectedPath);
            });
            browse.Margin = new Thickness(8, 0, 0, 0);
            var pathRow = new DockPanel();
            DockPanel.SetDock(browse, Dock.Right);
            pathRow.Children.Add(browse);
            pathRow.Children.Add(pathBox);
            panel.Children.Add(pathRow);

            var desktop = new CheckBox { Content = "Masaüstüne kısayol koy", IsChecked = true, Margin = new Thickness(0, 14, 0, 0) };
            var start = new CheckBox { Content = "Başlat menüsüne ekle", IsChecked = true };
            panel.Children.Add(desktop);
            panel.Children.Add(start);
            panel.Children.Add(Note("Yönetici izni gerekmez; yalnızca sizin Windows hesabınız için kurulur. Kaldırmak için: Ayarlar › Uygulamalar.", 12.5, 10));
            _body.Content = panel;

            SetFooter(Btn("Vazgeç", Close), Btn(existing != null ? "Güncelle" : "Kur", () =>
            {
                string dir;
                try { dir = Installer.NormalizeInstallDir(pathBox.Text); }
                catch (Exception) { MessageBox.Show(this, "Geçerli bir klasör girin.", Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!CloseRunning(dir)) return;
                RunInstall(new InstallOptions { InstallDir = dir, DesktopShortcut = desktop.IsChecked == true, StartMenuShortcut = start.IsChecked == true });
            }, primary: true));
        }

        private bool CloseRunning(string dir)
        {
            var running = Installer.RunningInstances(dir);
            if (running.Length == 0) return true;
            var answer = MessageBox.Show(this, "CinematicCam şu anda açık. Devam etmek için kapatılsın mı?", Title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return false;
            foreach (var p in running)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(5000)) p.Kill();
                }
                catch (Exception) { }
            }
            return true;
        }

        private void RunInstall(InstallOptions options)
        {
            var (panel, bar, status) = ProgressPanel("Kuruluyor…");
            _body.Content = panel;
            _stepText.Text = "2 / 3";
            SetFooter();

            Task.Run(() => Installer.Install(_payload, options, (p, s) => Dispatcher.BeginInvoke(new Action(() => { bar.Value = p; status.Text = s; }))))
                .ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (t.Exception != null) ShowError("Kurulum tamamlanamadı", t.Exception.GetBaseException().Message);
                    else
                    {
                        _installedDir = options.InstallDir;
                        ShowDone();
                    }
                })));
        }

        private void ShowDone()
        {
            _stepText.Text = "3 / 3";
            var panel = new StackPanel();
            panel.Children.Add(Banner("✓  CinematicCam kuruldu", "OkSoft", "OkText"));
            panel.Children.Add(Note("Klasör: " + _installedDir, 12.5, 12));
            panel.Children.Add(Heading("Sonraki adımlar"));
            panel.Children.Add(Note("1. MSFS 2024'ü açıp bir uçuş başlatın; CinematicCam kendiliğinden bağlanır.\n" +
                                    "2. Simde Seçenekler › Genel › Kamera altında eklenti kameralarına izin verin.\n" +
                                    "3. ChasePlane preset'leriniz varsa Preset'ler › İçe aktar ile ekleyin.\n" +
                                    "4. İlk uçuşta Kalibrasyon sayfasındaki sihirbazı bir kez çalıştırın."));
            var launch = new CheckBox { Content = "CinematicCam'i şimdi başlat", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(launch);
            _body.Content = panel;

            SetFooter(Btn("Bitir", () =>
            {
                if (launch.IsChecked == true)
                {
                    try { Process.Start(new ProcessStartInfo(Path.Combine(_installedDir, Installer.ExeName)) { WorkingDirectory = _installedDir, UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show(this, "Başlatılamadı: " + ex.Message, Title); }
                }
                Close();
            }, primary: true));
        }

        // ---------------- Kaldırma ----------------

        private void ShowUninstallConfirm()
        {
            string dir = Installer.UninstallTarget();
            var panel = new StackPanel();
            if (dir == null)
            {
                panel.Children.Add(Heading("CinematicCam kurulu görünmüyor"));
                panel.Children.Add(Note("Bu bilgisayarda kayıtlı bir CinematicCam kurulumu bulunamadı."));
                _body.Content = panel;
                SetFooter(Btn("Kapat", Close, primary: true));
                return;
            }

            panel.Children.Add(Heading("CinematicCam kaldırılsın mı?"));
            panel.Children.Add(Note("Uygulama dosyaları, kısayollar ve Windows kaydı kaldırılacak.\nKlasör: " + dir));
            var settings = new CheckBox { Content = "Ayarlarımı, preset'lerimi ve kısayollarımı da sil", IsChecked = false, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(settings);
            panel.Children.Add(Note("İşaretlemezseniz ayarlar " + Installer.SettingsDir + " klasöründe kalır; yeniden kurarsanız kaldığınız yerden devam edersiniz.", 12.5, 4));
            _body.Content = panel;

            SetFooter(Btn("Vazgeç", Close), Btn("Kaldır", () =>
            {
                if (!CloseRunning(dir)) return;
                bool deleteSettings = settings.IsChecked == true;
                var (progressPanel, bar, status) = ProgressPanel("Kaldırılıyor…");
                _body.Content = progressPanel;
                SetFooter();
                Task.Run(() => Installer.Uninstall(dir, deleteSettings, (p, s) => Dispatcher.BeginInvoke(new Action(() => { bar.Value = p; status.Text = s; }))))
                    .ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (t.Exception != null) ShowError("Kaldırma tamamlanamadı", t.Exception.GetBaseException().Message);
                        else
                        {
                            var done = new StackPanel();
                            done.Children.Add(Banner("✓  CinematicCam kaldırıldı", "OkSoft", "OkText"));
                            if (!deleteSettings) done.Children.Add(Note("Ayarlarınız korundu: " + Installer.SettingsDir, 12.5, 12));
                            _body.Content = done;
                            SetFooter(Btn("Kapat", Close, primary: true));
                        }
                    })));
            }, primary: true));
        }

        // ---------------- Ortak ----------------

        private void ShowError(string title, string message)
        {
            var panel = new StackPanel();
            panel.Children.Add(Banner("✗  " + title, "Raised", "BadText"));
            panel.Children.Add(Note(message, 13, 12));
            panel.Children.Add(Note("Uygulama açıksa kapatıp tekrar deneyin. Sorun sürerse başka bir kurulum klasörü seçin."));
            _body.Content = panel;
            SetFooter(Btn("Kapat", Close, primary: true));
        }

        private (StackPanel Panel, ProgressBar Bar, TextBlock Status) ProgressPanel(string title)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 18, 0, 10) };
            var status = new TextBlock { Foreground = B("FgDim"), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(Heading(title));
            panel.Children.Add(bar);
            panel.Children.Add(status);
            return (panel, bar, status);
        }

        private void SetFooter(params Button[] buttons)
        {
            _footer.Children.Clear();
            foreach (var b in buttons)
            {
                b.Margin = new Thickness(10, 0, 0, 0);
                _footer.Children.Add(b);
            }
            buttons.LastOrDefault()?.Focus();
        }

        private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

        private static TextBlock Heading(string text) =>
            new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };

        private static TextBlock Label(string text) =>
            new TextBlock { Text = text, FontSize = 13, Foreground = B("FgDim"), Margin = new Thickness(0, 16, 0, 6) };

        private static TextBlock Note(string text, double size = 13.5, double top = 0) => new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = B("FgDim"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.55,
            Margin = new Thickness(0, top, 0, 0),
        };

        private static Border Banner(string text, string bg, string fg) => new Border
        {
            Background = B(bg),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = B(fg) },
        };

        private Button Btn(string text, Action onClick, bool primary = false)
        {
            var b = new Button { Content = text, MinWidth = 110 };
            if (primary) b.Style = (Style)FindResource("Primary");
            b.Click += (_, __) => onClick();
            return b;
        }

        private static ImageSource AppIcon()
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName))
                {
                    if (icon == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
                    src.Freeze();
                    return src;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
