using System.Text;
using CinematicCam.Camera;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Ui;

public enum PageId { Camera, Presets, Calibration, Hotkeys, Log }

/// <summary>Sayfaların ortak kullandığı servisler. Tüm üyeler arayüz iş parçacığından çağrılır.</summary>
public sealed class AppServices
{
    private readonly StringBuilder _log = new();

    public required AppSettings Settings { get; init; }
    public required SimConnectClient Client { get; init; }
    public required CameraEngine Engine { get; init; }
    public required Action Save { get; init; }
    public required Action RegisterHotkeys { get; init; }
    public required Action<PageId> Navigate { get; init; }
    public required Action OpenImport { get; init; }

    public string LogText => _log.ToString();
    public event Action? LogChanged;

    public void Log(string msg)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        LogChanged?.Invoke();
    }
}

/// <summary>Ana penceredeki bir sayfa.</summary>
public interface IPage
{
    System.Windows.FrameworkElement View { get; }
    /// <summary>Arayüz zamanlayıcısıyla (~7 Hz) çağrılır.</summary>
    void Tick(Telemetry? telemetry);
    /// <summary>Ayarlar dışarıdan değişince (preset, profil, kısayol) yeniden çizim.</summary>
    void Refresh();
}
