namespace CinematicCam.Settings;

/// <summary>Bir kamera preset'inin türü. ChasePlane'deki profile_theme alanından türetilir.</summary>
public enum PresetKind
{
    /// <summary>Gövdeye sabit dış kamera (ChasePlane OUTSIDE_AIRFRAME).</summary>
    Airframe,
    Chase,
    Orbit,
    /// <summary>Pilot koltuğu görünümleri (ChasePlane ONBOARD_PIC).</summary>
    Cockpit,
    Cabin,
    /// <summary>İçeride sabit kamera, ör. GoPro (ChasePlane ONBOARD_MOUNTED).</summary>
    InsideMounted,
}

public static class PresetKinds
{
    public static bool IsInside(this PresetKind k) => k is PresetKind.Cockpit or PresetKind.Cabin or PresetKind.InsideMounted;

    public static string Label(this PresetKind k) => k switch
    {
        PresetKind.Airframe => "Gövdeye sabit",
        PresetKind.Chase => "Chase",
        PresetKind.Orbit => "Orbit",
        PresetKind.Cockpit => "Kokpit",
        PresetKind.Cabin => "Kabin",
        PresetKind.InsideMounted => "GoPro / sabit iç",
        _ => k.ToString(),
    };

    public static string ModeLabel(this PresetKind k) => k switch
    {
        PresetKind.Airframe => "Sabit dış kamera",
        PresetKind.Chase => "Chase",
        PresetKind.Orbit => "Orbit",
        PresetKind.Cockpit => "İç kamera · Kokpit",
        PresetKind.Cabin => "İç kamera · Kabin",
        PresetKind.InsideMounted => "İç kamera · Sabit",
        _ => k.ToString(),
    };

    public static string ReferenceLabel(this PresetKind k) => k.IsInside() ? "Pilot gözü (Eyepoint)" : "Uçak (SimObject)";
}

/// <summary>
/// Uçağa bağlı bir kamera görünümü. Konum metre (x sağ, y yukarı, z ileri); dış kameralarda
/// uçağa, iç kameralarda pilot gözü noktasına göre. Açılar derece: pitch yukarı pozitif, yaw sağa pozitif.
/// </summary>
public sealed class CameraPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public PresetKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double PitchDeg { get; set; }
    public double YawDeg { get; set; }
    public double RollDeg { get; set; }
    /// <summary>ChasePlane'den gelen ham zoom değeri (bilgi amaçlı).</summary>
    public double? ChasePlaneZoom { get; set; }
    public double FovDeg { get; set; } = 60;
    /// <summary>Doğrudan bu görünüme geçiren kısayol (boş = yok).</summary>
    public string Hotkey { get; set; } = "";
    public bool UseInCinematic { get; set; } = true;
    /// <summary>İçe aktarılan dosyadaki kimlik; aynı dosya tekrar aktarılınca güncellemek için.</summary>
    public string? SourceId { get; set; }
}

public sealed class AircraftProfile
{
    /// <summary>Uçak klasör adı, ör. "fnx_320_cfm_sl". Yüklenen aircraft.cfg yolunda aranır.</summary>
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>ChasePlane metadata'sındaki aircraft.cfg yolu (varsa).</summary>
    public string? CfgPath { get; set; }
    public bool AutoActivate { get; set; } = true;
    public string Source { get; set; } = "";
    public List<CameraPreset> Presets { get; set; } = new();

    /// <summary>Yüklü uçağın aircraft.cfg yolu bu profile ait mi?</summary>
    public bool Matches(string loadedCfgPath)
    {
        var loaded = Normalize(loadedCfgPath);
        if (CfgPath != null && loaded.EndsWith(Normalize(CfgPath), StringComparison.Ordinal)) return true;
        return Key.Length > 0 && loaded.Contains("/" + Key.ToLowerInvariant() + "/", StringComparison.Ordinal);
    }

    private static string Normalize(string path) => "/" + path.Replace('\\', '/').Trim('/').ToLowerInvariant();

    /// <summary>aircraft.cfg yolundan profil anahtarı: "...\fnx_320_cfm_sl\config\aircraft.cfg" → "fnx_320_cfm_sl".</summary>
    public static string KeyFromCfgPath(string cfgPath)
    {
        var parts = cfgPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int i = parts.Length - 2;
        if (i >= 0 && parts[i].Equals("config", StringComparison.OrdinalIgnoreCase)) i--;
        return i >= 0 ? parts[i].ToLowerInvariant() : "aircraft";
    }
}

public static class PresetNaming
{
    /// <summary>Konumdan okunabilir bir ad üretir: "Sol ön, yukarıdan", "Kabin arka, sağ".</summary>
    public static string Describe(PresetKind kind, double x, double y, double z)
    {
        if (kind.IsInside())
        {
            if (Math.Abs(z) < 1) return kind == PresetKind.InsideMounted ? "Kokpit sabit" : "Kokpit";
            string part = z > -8 ? "ön" : z > -20 ? "orta" : "arka";
            string seat = x > 0.5 ? "sağ" : x < -0.5 ? "sol" : "koridor";
            return $"Kabin {part}, {seat}";
        }

        string side = x > 3 ? "Sağ" : x < -3 ? "Sol" : "Merkez";
        string fore = z > 6 ? "ön" : z < -15 ? "arka" : "orta";
        string height = y > 4 ? ", yukarıdan" : y < -0.5 ? ", alttan" : "";
        return $"{side} {fore}{height}";
    }

    /// <summary>ChasePlane zoom değerini yaklaşık görüş açısına çevirir (doğrulanmamış bir eşleme).</summary>
    public static double ZoomToFov(double zoom) => Math.Clamp(100 - 70 * zoom, 20, 100);
}
