using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace CinematicCam.Setup
{
    internal sealed class InstallOptions
    {
        public string InstallDir { get; set; }
        public bool DesktopShortcut { get; set; } = true;
        public bool StartMenuShortcut { get; set; } = true;
    }

    /// <summary>Kurulum ve kaldırma işlemleri. Kullanıcı başınadır; yönetici izni gerekmez.</summary>
    internal static class Installer
    {
        public const string AppName = "CinematicCam";
        public const string DisplayName = "CinematicCam for MSFS 2024";
        public const string ExeName = "CinematicCam.exe";
        public const string UninstallerName = "Uninstall.exe";
        /// <summary>Kurulan dosyaların listesi; kaldırıcı yalnızca bunları siler.</summary>
        private const string ManifestName = "install-files.txt";
        private static readonly string[] KnownFiles = { ExeName, "SimConnect.dll", UninstallerName, ManifestName };
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CinematicCam";

        public static string Version
        {
            get
            {
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
        }

        public static string DefaultInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

        public static string SettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

        private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
        private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        /// <summary>Daha önce kurulmuşsa (sürüm, klasör).</summary>
        public static (string Version, string Dir)? FindExisting()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                if (key == null) return null;
                var dir = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(dir)) return null;
                return (key.GetValue("DisplayVersion") as string ?? "?", dir);
            }
        }

        /// <summary>
        /// Kaldırılacak klasör: kaldırıcı kurulum klasöründen çalışıyorsa orası, değilse kayıttaki klasör.
        /// </summary>
        public static string UninstallTarget()
        {
            string selfDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            if (File.Exists(Path.Combine(selfDir, ExeName))) return selfDir;
            return FindExisting()?.Dir;
        }

        /// <summary>Seçilen klasörün sonuna gerekirse "CinematicCam" ekler; böylece kurulum hep kendi klasörüne yapılır.</summary>
        public static string NormalizeInstallDir(string path)
        {
            path = Path.GetFullPath(path.Trim().TrimEnd('\\'));
            return string.Equals(Path.GetFileName(path), AppName, StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, AppName);
        }

        /// <summary>Kurulum klasöründen çalışan CinematicCam süreçleri.</summary>
        public static Process[] RunningInstances(string dir)
        {
            string exe = Path.GetFullPath(Path.Combine(dir, ExeName));
            return Process.GetProcessesByName(AppName).Where(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return true; }
            }).ToArray();
        }

        public static void Install(Payload payload, InstallOptions options, Action<double, string> progress)
        {
            string dir = Path.GetFullPath(options.InstallDir);
            Directory.CreateDirectory(dir);
            string root = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            progress(0.02, "Dosyalar açılıyor…");
            var installed = new System.Collections.Generic.List<string>();
            using (var stream = payload.OpenZip())
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                installed.AddRange(entries.Select(e => e.FullName.Replace('/', '\\')));
                long total = Math.Max(1, entries.Sum(e => e.Length));
                long done = 0;
                foreach (var entry in entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Kurulum paketi geçersiz bir yol içeriyor: " + entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    progress(0.05 + 0.8 * done / total, "Kopyalanıyor: " + entry.FullName);
                    ExtractWithRetry(entry, target);
                    done += entry.Length;
                }
            }

            progress(0.88, "Kaldırıcı yazılıyor…");
            payload.WriteBareExe(Path.Combine(dir, UninstallerName));
            installed.Add(UninstallerName);
            installed.Add(ManifestName);
            File.WriteAllLines(Path.Combine(dir, ManifestName), installed.Distinct(StringComparer.OrdinalIgnoreCase));

            progress(0.92, "Kısayollar oluşturuluyor…");
            string exePath = Path.Combine(dir, ExeName);
            if (options.StartMenuShortcut) CreateShortcut(StartMenuLink, exePath, dir);
            else TryDelete(StartMenuLink);
            if (options.DesktopShortcut) CreateShortcut(DesktopLink, exePath, dir);
            else TryDelete(DesktopLink);

            progress(0.96, "Windows'a kaydediliyor…");
            Register(dir);
            progress(1.0, "Kurulum tamamlandı.");
        }

        private static void ExtractWithRetry(ZipArchiveEntry entry, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    entry.ExtractToFile(target, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    // Dosya kısa süreliğine kilitli olabilir (virüs tarayıcısı, kapanmakta olan uygulama).
                    Thread.Sleep(400);
                }
            }
        }

        private static void Register(string dir)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                string uninstaller = Path.Combine(dir, UninstallerName);
                key.SetValue("DisplayName", DisplayName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayIcon", Path.Combine(dir, ExeName));
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int)(DirectorySize(dir) / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Uninstall(string dir, bool deleteSettings, Action<double, string> progress)
        {
            dir = Path.GetFullPath(dir);
            progress(0.1, "Kısayollar kaldırılıyor…");
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);

            progress(0.3, "Dosyalar siliniyor…");
            string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string root = dir.TrimEnd('\\') + "\\";
            bool runningFromDir = self.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
            {
                // Yalnızca kurulumun yazdığı dosyalar silinir; kullanıcının klasöre koyduğu başka dosyalar kalır.
                string manifest = Path.Combine(dir, ManifestName);
                var files = File.Exists(manifest) ? File.ReadAllLines(manifest).Where(l => l.Trim().Length > 0).ToArray() : KnownFiles;
                foreach (var rel in files.Concat(KnownFiles).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string path = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) TryDelete(path);
                }
                foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    TryDeleteDir(sub);
                if (!runningFromDir) TryDeleteDir(dir);
            }

            if (deleteSettings)
            {
                progress(0.7, "Ayarlar siliniyor…");
                if (Directory.Exists(SettingsDir))
                {
                    try { Directory.Delete(SettingsDir, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            progress(0.9, "Windows kaydı kaldırılıyor…");
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

            if (runningFromDir)
            {
                // Çalışan kaldırıcı kendini silemez: kapandıktan sonra onu silip klasörü (boşsa) kaldıran gizli bir komut bırak.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{self}\" & rmdir \"{dir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            progress(1.0, "CinematicCam kaldırıldı.");
        }

        private static void CreateShortcut(string linkPath, string target, string workingDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Kısayol servisi bulunamadı.");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic link = shell.CreateShortcut(linkPath);
                link.TargetPath = target;
                link.WorkingDirectory = workingDir;
                link.IconLocation = target + ",0";
                link.Description = DisplayName;
                link.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static long DirectorySize(string dir) =>
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
