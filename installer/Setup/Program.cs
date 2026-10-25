using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace CinematicCam.Setup
{
    internal static class Program
    {
        /// <summary>
        /// Yük içeren exe: kurulum sihirbazı. Yüksüz exe (kurulu Uninstall.exe): kaldırma.
        /// /S sessiz çalışır; /uninstall yüklü exe'yi de kaldırma moduna zorlar.
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("/silent", StringComparison.OrdinalIgnoreCase));
            bool forceUninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            string self = Process.GetCurrentProcess().MainModule.FileName;
            var payload = forceUninstall ? null : Payload.TryOpen(self);

            if (silent) return RunSilent(payload);

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/CinematicCam-Setup;component/Theme.xaml", UriKind.Relative));
            var window = new SetupWindow(payload);
            return app.Run(window);
        }

        private static int RunSilent(Payload payload)
        {
            try
            {
                if (payload != null)
                {
                    var existing = Installer.FindExisting();
                    Installer.Install(payload, new InstallOptions { InstallDir = existing?.Dir ?? Installer.DefaultInstallDir }, (_, __) => { });
                }
                else
                {
                    string dir = Installer.UninstallTarget();
                    if (dir == null) return 1;
                    Installer.Uninstall(dir, deleteSettings: false, (_, __) => { });
                }
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
