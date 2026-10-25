namespace CinematicCam.Settings;

/// <summary>Fixed: bir preset'ten gelen, gövdeye sabit dış kamera (mod kartı yok).</summary>
public enum CameraModeKind { Chase, Flyby, Orbit, Inside, Cinematic, Fixed }

/// <summary>Kameranın yönünü sime nasıl bildireceğimiz (kalibrasyon).</summary>
public enum RotationOutputMode
{
    /// <summary>Dünya referanslı yunuslama/yatış/baş açıları. Ufuk sabit kalır.</summary>
    WorldPbh,
    /// <summary>Uçak gövdesine göre "bakış noktası". Yatış kontrolü sime bırakılır.</summary>
    TargetedSimObject,
}

public sealed class ChaseSettings
{
    /// <summary>Uçağa uzaklık, kanat açıklığının katı olarak.</summary>
    public double DistanceSpans { get; set; } = 2.4;
    public double MinDistanceM { get; set; } = 12;
    /// <summary>Yükseklik, uzaklığın oranı olarak.</summary>
    public double HeightRatio { get; set; } = 0.22;
    public double LateralM { get; set; } = 0;
    /// <summary>Konum gecikmesi (sn). Büyük değer = daha "yaylı" kamera.</summary>
    public double PositionLagSec { get; set; } = 0.35;
    /// <summary>Baş takibinin gecikmesi (sn).</summary>
    public double HeadingLagSec { get; set; } = 0.6;
    /// <summary>Uçak yunuslamasını ne kadar takip etsin (0-1).</summary>
    public double FollowPitch { get; set; } = 0.6;
    /// <summary>Uçak yatışını kamera yatışına ne kadar aktarsın (0-1).</summary>
    public double FollowBank { get; set; } = 0.25;
    /// <summary>İvmenin kamerayı ne kadar ittiği (metre / g).</summary>
    public double GForceM { get; set; } = 2.5;
    /// <summary>Burnun ne kadar önüne bakılsın (kanat açıklığı katı).</summary>
    public double LookAheadSpans { get; set; } = 0.8;
    public double FovDeg { get; set; } = 55;
}

public sealed class FlybySettings
{
    /// <summary>Kameranın uçağın kaç saniye önüne yerleştirileceği.</summary>
    public double LeadSec { get; set; } = 6;
    public double SideOffsetM { get; set; } = 35;
    public double HeightOffsetM { get; set; } = 6;
    /// <summary>Uçak bu mesafeden uzaklaşınca yeni flyby noktası seçilir.</summary>
    public double ResetDistanceM { get; set; } = 900;
    /// <summary>Uzaklaştıkça zoom (tele) yapılsın mı.</summary>
    public bool ZoomFollow { get; set; } = true;
    /// <summary>Zoom takibinde uçağın kadrajı kaplama oranı (0-1).</summary>
    public double Framing { get; set; } = 0.45;
    public double MinFovDeg { get; set; } = 8;
    public double MaxFovDeg { get; set; } = 70;
    /// <summary>Kameramanın uçağı takip gecikmesi (sn).</summary>
    public double OperatorLagSec { get; set; } = 0.25;
}

public sealed class OrbitSettings
{
    public double RadiusSpans { get; set; } = 2.6;
    public double HeightRatio { get; set; } = 0.25;
    /// <summary>Dönüş hızı (derece/sn); negatif ters yön.</summary>
    public double SpeedDegS { get; set; } = 8;
    public double FovDeg { get; set; } = 50;
    public double PositionLagSec { get; set; } = 0.3;
}

public sealed class InsideSettings
{
    /// <summary>Preset'i olmayan uçaklarda kullanılan görüş açısı.</summary>
    public double FovDeg { get; set; } = 65;
    /// <summary>Preset'in kendi FOV'una eklenen fark (derece).</summary>
    public double FovOffsetDeg { get; set; }
    /// <summary>Pilotun başının hafif, sürekli hareketi (0-1).</summary>
    public double HeadMotion { get; set; } = 0.4;
    /// <summary>İvmeyle başın itilmesi (0-1).</summary>
    public double GForce { get; set; } = 0.35;
    /// <summary>Yatışta dönüş yönüne bakma (0-1).</summary>
    public double LookIntoTurn { get; set; } = 0.3;
    /// <summary>Görünümler arası geçiş (sn); 0 = anında.</summary>
    public double TransitionSec { get; set; } = 0.6;
    /// <summary>Gövde titreşimi (0-1), motor ve pistten.</summary>
    public double Vibration { get; set; } = 0.3;
}

public sealed class CinematicSettings
{
    public double MinShotSec { get; set; } = 8;
    public double MaxShotSec { get; set; } = 16;
    /// <summary>0 ise kesme (cut), değilse planlar arası yumuşak geçiş süresi.</summary>
    public double BlendSec { get; set; } = 0;
    public bool UseChase { get; set; } = true;
    public bool UseFlyby { get; set; } = true;
    public bool UseOrbit { get; set; } = true;
    public bool UseTracking { get; set; } = true;
    /// <summary>Kokpit ve kabin preset'lerini de plan olarak kullan.</summary>
    public bool UseInside { get; set; }
    /// <summary>Uçak profilindeki "Sinematikte kullan" işaretli dış preset'leri plan olarak kullan.</summary>
    public bool UsePresets { get; set; } = true;
}

public sealed class ShakeSettings
{
    public bool Enabled { get; set; } = true;
    public double Master { get; set; } = 1.0;
    public double Engine { get; set; } = 0.4;
    public double Turbulence { get; set; } = 1.0;
    public double GroundRoll { get; set; } = 0.8;
    public double Touchdown { get; set; } = 1.0;
    /// <summary>Elde tutulan kamera hissi veren hafif sürekli salınım.</summary>
    public double Handheld { get; set; } = 0.3;
}

public sealed class CalibrationSettings
{
    public bool InvertAircraftPitch { get; set; }
    public bool InvertAircraftBank { get; set; }
    public bool InvertCameraPitch { get; set; }
    public bool InvertCameraBank { get; set; }
    public RotationOutputMode RotationMode { get; set; } = RotationOutputMode.WorldPbh;
}

public sealed class Preset
{
    public string Name { get; set; } = "";
    public CameraModeKind Mode { get; set; } = CameraModeKind.Chase;
    public double ManualYawDeg { get; set; }
    public double ManualPitchDeg { get; set; }
    public double ManualZoom { get; set; } = 1;
    public ChaseSettings Chase { get; set; } = new();
    public OrbitSettings Orbit { get; set; } = new();
}

public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    /// <summary>Eski dosyalarda alan yok, 0 okunur ve göç (migration) çalışır.</summary>
    public int Version { get; set; }
    public CameraModeKind Mode { get; set; } = CameraModeKind.Chase;
    public InsideSettings Inside { get; set; } = new();
    public List<AircraftProfile> Profiles { get; set; } = new();
    /// <summary>Kullanıcının elle seçtiği profil; null ise yüklü uçağa göre otomatik.</summary>
    public string? PinnedProfileKey { get; set; }
    public ChaseSettings Chase { get; set; } = new();
    public FlybySettings Flyby { get; set; } = new();
    public OrbitSettings Orbit { get; set; } = new();
    public CinematicSettings Cinematic { get; set; } = new();
    public ShakeSettings Shake { get; set; } = new();
    public CalibrationSettings Calibration { get; set; } = new();

    /// <summary>9 slot; boş slotlar null.</summary>
    public Preset?[] Presets { get; set; } = new Preset?[9];
    public Dictionary<string, string> Hotkeys { get; set; } = HotkeyDefaults.Create();
}

public static class HotkeyDefaults
{
    public static Dictionary<string, string> Create()
    {
        var keys = Base();
        for (int i = 1; i <= 9; i++)
        {
            // Alt+Shift+rakam ChasePlane'den aktarılan iç kamera tuşlarına ayrıldı.
            keys[$"LoadPreset{i}"] = $"Ctrl+Shift+F{i}";
            keys[$"SavePreset{i}"] = $"Ctrl+Alt+Shift+F{i}";
        }
        return keys;
    }

    private static Dictionary<string, string> Base() => new()
    {
        ["ToggleCamera"] = "Ctrl+Shift+C",
        ["ModeChase"] = "Ctrl+Shift+D1",
        ["ModeFlyby"] = "Ctrl+Shift+D2",
        ["ModeOrbit"] = "Ctrl+Shift+D3",
        ["ModeInside"] = "Ctrl+Shift+D4",
        ["ModeCinematic"] = "Ctrl+Shift+D5",
        ["NextShot"] = "Ctrl+Shift+N",
        ["RotateLeft"] = "Ctrl+Shift+Left",
        ["RotateRight"] = "Ctrl+Shift+Right",
        ["RotateUp"] = "Ctrl+Shift+Up",
        ["RotateDown"] = "Ctrl+Shift+Down",
        ["ZoomIn"] = "Ctrl+Shift+PageUp",
        ["ZoomOut"] = "Ctrl+Shift+PageDown",
        ["ResetView"] = "Ctrl+Shift+Home",
        ["ToggleShake"] = "Ctrl+Shift+S",
    };
}
