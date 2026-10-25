using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CinematicCam.Settings;

namespace CinematicCam.Import;

public enum UnnamedHandling { AutoName, Keep, Skip }

public sealed class ImportedPreset
{
    public required CameraPreset Preset { get; init; }
    /// <summary>"temp1" gibi ChasePlane'in otomatik verdiği isim.</summary>
    public bool Unnamed { get; init; }
    /// <summary>ChasePlane'deki tuş (değiştiricisiz), ör. "D6", "Space".</summary>
    public string? ChasePlaneKey { get; init; }
    public string OriginalName { get; init; } = "";
}

public sealed class ImportedAircraft
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? CfgPath { get; init; }
    public List<ImportedPreset> Presets { get; } = new();
    public List<string> Warnings { get; } = new();

    public int Count(PresetKind kind) => Presets.Count(p => p.Preset.Kind == kind);
}

public sealed class ImportOptions
{
    public HashSet<PresetKind> Kinds { get; init; } = Enum.GetValues<PresetKind>().ToHashSet();
    public UnnamedHandling Unnamed { get; init; } = UnnamedHandling.AutoName;
    public bool ImportHotkeys { get; init; } = true;
    /// <summary>ChasePlane tuşlarının önüne eklenecek değiştiriciler (yalın tuşlar sistem genelinde yazmayı engellerdi).</summary>
    public string HotkeyPrefix { get; init; } = "Alt+Shift";
    /// <summary>null: uçağın kendi profili (yoksa oluşturulur).</summary>
    public string? TargetProfileKey { get; init; }
    public bool AutoActivate { get; init; } = true;
}

/// <summary>
/// ChasePlane "Export" zip dosyasını okur. Beklenen yapı:
/// aircraft/&lt;uçak&gt;/metadata.txt, aircraft/&lt;uçak&gt;/outside/*.json, aircraft/&lt;uçak&gt;/onboard/*.json
/// </summary>
public static class ChasePlaneImporter
{
    private const int MaxEntries = 5000;
    private const long MaxJsonBytes = 256 * 1024;
    private static readonly Regex EntryPattern = new(@"^aircraft/([^/]+)/(outside|onboard)/[^/]+\.json$", RegexOptions.IgnoreCase);
    private static readonly Regex MetadataPattern = new(@"^aircraft/([^/]+)/metadata\.txt$", RegexOptions.IgnoreCase);
    private static readonly Regex UnnamedPattern = new(@"^temp\d*$", RegexOptions.IgnoreCase);

    public static List<ImportedAircraft> Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("Zip dosyası çok fazla öğe içeriyor.");

        var byKey = new Dictionary<string, ImportedAircraft>(StringComparer.OrdinalIgnoreCase);
        var cfgPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            var meta = MetadataPattern.Match(name);
            if (meta.Success && entry.Length < MaxJsonBytes)
            {
                using var reader = new StreamReader(entry.Open());
                var firstLine = reader.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(firstLine)) cfgPaths[meta.Groups[1].Value] = firstLine;
                continue;
            }

            var m = EntryPattern.Match(name);
            if (!m.Success || entry.Length > MaxJsonBytes) continue;

            ChasePlaneJson? json;
            try
            {
                using var stream = entry.Open();
                json = JsonSerializer.Deserialize<ChasePlaneJson>(stream);
            }
            catch (JsonException)
            {
                continue;
            }
            if (json?.position == null) continue;

            string key = json.aircraft ?? m.Groups[1].Value;
            if (!byKey.TryGetValue(key, out var aircraft))
            {
                aircraft = new ImportedAircraft { Key = key, DisplayName = json.aircraft_readable ?? key };
                byKey[key] = aircraft;
            }

            bool onboard = m.Groups[2].Value.Equals("onboard", StringComparison.OrdinalIgnoreCase);
            var kind = KindFrom(json.profile_theme, json.profile_physics_type, onboard);
            var p = json.position;
            string original = CleanName(json.name);
            aircraft.Presets.Add(new ImportedPreset
            {
                OriginalName = original,
                Unnamed = original.Length == 0 || UnnamedPattern.IsMatch(original),
                ChasePlaneKey = ParseShortcut(json.shortcuts),
                Preset = new CameraPreset
                {
                    Name = original,
                    Kind = kind,
                    X = p.x,
                    Y = p.y,
                    Z = p.z,
                    PitchDeg = p.pitch,
                    YawDeg = p.yaw,
                    RollDeg = p.roll,
                    ChasePlaneZoom = p.zoom,
                    FovDeg = PresetNaming.ZoomToFov(p.zoom),
                    SourceId = json.guid,
                },
            });
        }

        var result = new List<ImportedAircraft>();
        foreach (var aircraft in byKey.Values)
        {
            var withCfg = new ImportedAircraft
            {
                Key = aircraft.Key,
                DisplayName = aircraft.DisplayName,
                CfgPath = cfgPaths.GetValueOrDefault(aircraft.Key),
            };
            withCfg.Presets.AddRange(aircraft.Presets.OrderBy(p => p.Preset.Kind));
            AddWarnings(withCfg);
            result.Add(withCfg);
        }
        if (result.Count == 0) throw new InvalidDataException("Bu dosyada ChasePlane kamera preset'i bulunamadı.");
        return result;
    }

    /// <summary>Seçilen preset'leri ayarlara ekler; aynı kaynaktan gelenleri günceller. Eklenen sayıyı döndürür.</summary>
    public static int Apply(AppSettings settings, ImportedAircraft aircraft, ImportOptions options, out List<string> skippedHotkeys)
    {
        skippedHotkeys = new List<string>();
        string targetKey = options.TargetProfileKey ?? aircraft.Key;
        var profile = settings.Profiles.FirstOrDefault(p => p.Key.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = new AircraftProfile { Key = targetKey, DisplayName = aircraft.DisplayName, CfgPath = aircraft.CfgPath };
            settings.Profiles.Add(profile);
        }
        profile.AutoActivate = options.AutoActivate;
        profile.Source = "ChasePlane export";
        profile.CfgPath ??= aircraft.CfgPath;

        var usedKeys = new HashSet<string>(settings.Hotkeys.Values.Where(v => v.Length > 0), StringComparer.OrdinalIgnoreCase);
        foreach (var other in settings.Profiles.Where(p => p != profile))
            foreach (var hk in other.Presets.Select(p => p.Hotkey).Where(h => h.Length > 0)) usedKeys.Add(hk);

        int added = 0;
        foreach (var item in aircraft.Presets)
        {
            if (!options.Kinds.Contains(item.Preset.Kind)) continue;
            if (item.Unnamed && options.Unnamed == UnnamedHandling.Skip) continue;

            var preset = Clone(item.Preset);
            if (item.Unnamed && options.Unnamed == UnnamedHandling.AutoName)
                preset.Name = PresetNaming.Describe(preset.Kind, preset.X, preset.Y, preset.Z);
            if (preset.Name.Length == 0) preset.Name = preset.Kind.Label();

            if (options.ImportHotkeys && item.ChasePlaneKey != null)
            {
                string combo = options.HotkeyPrefix.Length > 0 ? options.HotkeyPrefix + "+" + item.ChasePlaneKey : item.ChasePlaneKey;
                if (usedKeys.Add(combo)) preset.Hotkey = combo;
                else skippedHotkeys.Add($"{preset.Name} ({combo})");
            }

            int existing = preset.SourceId == null ? -1 : profile.Presets.FindIndex(p => p.SourceId == preset.SourceId);
            if (existing >= 0)
            {
                preset.Id = profile.Presets[existing].Id;
                profile.Presets[existing] = preset;
            }
            else
            {
                preset.Name = UniqueName(profile, preset.Name);
                profile.Presets.Add(preset);
            }
            added++;
        }
        return added;
    }

    public static PresetKind KindFrom(string? theme, string? physics, bool onboard) => (theme?.ToUpperInvariant()) switch
    {
        "OUTSIDE_AIRFRAME" => PresetKind.Airframe,
        "OUTSIDE_CHASE" => PresetKind.Chase,
        "OUTSIDE_ORBIT" => PresetKind.Orbit,
        "ONBOARD_PIC" => PresetKind.Cockpit,
        "ONBOARD_CABIN" => PresetKind.Cabin,
        "ONBOARD_MOUNTED" => PresetKind.InsideMounted,
        _ => onboard
            ? (physics?.ToUpperInvariant() == "MOUNTED" ? PresetKind.InsideMounted : PresetKind.Cabin)
            : (physics?.ToUpperInvariant()) switch { "CHASE" => PresetKind.Chase, "ORBIT" => PresetKind.Orbit, _ => PresetKind.Airframe },
    };

    /// <summary>
    /// ChasePlane kısayol biçimi: "1|Keyboard|54|2|0;" — üçüncü alan Windows sanal tuş kodu.
    /// Değiştirici alanının anlamı bilinmediği için yalnızca tuşun kendisi alınır.
    /// </summary>
    public static string? ParseShortcut(string[]? shortcuts)
    {
        if (shortcuts == null) return null;
        foreach (var entry in shortcuts.SelectMany(s => s.Split(';', StringSplitOptions.RemoveEmptyEntries)))
        {
            var parts = entry.Split('|');
            if (parts.Length < 3 || !parts[1].Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(parts[2], out int vk)) continue;
            var key = KeyInterop.KeyFromVirtualKey(vk);
            if (key is Key.None or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System) continue;
            return key.ToString();
        }
        return null;
    }

    /// <summary>
    /// ChasePlane isimlerinde görünmez kontrol karakterleri (ör. 0x7F) bulunabiliyor;
    /// bunları atar ve boşlukları tekilleştirir.
    /// </summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var chars = name.Where(c => !char.IsControl(c) && c != '﻿' && c != '​').ToArray();
        return Regex.Replace(new string(chars), @"\s+", " ").Trim();
    }

    private static void AddWarnings(ImportedAircraft aircraft)
    {
        foreach (var dup in aircraft.Presets.GroupBy(p => p.OriginalName, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
            aircraft.Warnings.Add($"“{dup.Key}” {dup.Count()} kez geçiyor; tekrarlar numaralandırılacak.");
        int unnamed = aircraft.Presets.Count(p => p.Unnamed);
        if (unnamed > 0) aircraft.Warnings.Add($"{unnamed} preset isimsiz (temp…).");
        aircraft.Warnings.Add("ChasePlane zoom değerleri görüş açısına yaklaşık çevrilir; ilk uçuşta kontrol edin.");
    }

    private static string UniqueName(AircraftProfile profile, string name)
    {
        if (!profile.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} ({i})";
            if (!profile.Presets.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    private static CameraPreset Clone(CameraPreset p) => new()
    {
        Name = p.Name, Kind = p.Kind, X = p.X, Y = p.Y, Z = p.Z,
        PitchDeg = p.PitchDeg, YawDeg = p.YawDeg, RollDeg = p.RollDeg,
        ChasePlaneZoom = p.ChasePlaneZoom, FovDeg = p.FovDeg, SourceId = p.SourceId,
    };

    // ChasePlane JSON şemasının kullandığımız alanları (adlar dosyadakiyle aynı).
#pragma warning disable IDE1006
    private sealed class ChasePlaneJson
    {
        public string? name { get; set; }
        public string? guid { get; set; }
        public string? aircraft { get; set; }
        public string? aircraft_readable { get; set; }
        public string? profile_physics_type { get; set; }
        public string? profile_theme { get; set; }
        public string[]? shortcuts { get; set; }
        public ChasePlanePosition? position { get; set; }
    }

    private sealed class ChasePlanePosition
    {
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
        public double pitch { get; set; }
        public double yaw { get; set; }
        public double roll { get; set; }
        public double zoom { get; set; }
    }
#pragma warning restore IDE1006
}
