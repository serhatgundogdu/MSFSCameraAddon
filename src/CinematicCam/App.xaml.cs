using System.IO;
using System.Windows;
using System.Windows.Threading;
using CinematicCam.Settings;

namespace CinematicCam;

public partial class App : Application
{
    public static string CrashLogPath => Path.Combine(SettingsStore.Folder, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Arayüzdeki beklenmedik hatalar uygulamayı kapatmasın: günlüğe yaz, kullanıcıya bildir, devam et.
        DispatcherUnhandledException += OnDispatcherException;
        // Arka plan iş parçacığındaki hatalar süreci yine sonlandırır; en azından nedeni kaydedilir.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception, "arka plan");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash(args.Exception, "görev");
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception, "arayüz");
        e.Handled = true;
        MessageBox.Show(
            "Beklenmedik bir hata oluştu, ancak CinematicCam çalışmaya devam ediyor.\n\n" +
            e.Exception.Message + "\n\nAyrıntılar: " + CrashLogPath,
            "CinematicCam", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static void WriteCrash(Exception? ex, string where)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            var version = typeof(App).Assembly.GetName().Version;
            File.AppendAllText(CrashLogPath,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · v{version} · {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Günlük yazılamazsa yapacak bir şey yok.
        }
    }
}
