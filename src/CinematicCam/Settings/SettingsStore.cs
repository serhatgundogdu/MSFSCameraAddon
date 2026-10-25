using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CinematicCam.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>CINEMATICCAM_SETTINGS_DIR ortam değişkeniyle değiştirilebilir (test ve taşınabilir kurulum için).</summary>
    public static string Folder { get; } = Environment.GetEnvironmentVariable("CINEMATICCAM_SETTINGS_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CinematicCam");
    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (settings != null)
                {
                    if (settings.Presets.Length != 9)
                    {
                        var presets = settings.Presets;
                        Array.Resize(ref presets, 9);
                        settings.Presets = presets;
                    }
                    if (settings.Version < 2)
                    {
                        // v2: iç kamera Ctrl+Shift+4'e geldi, preset slotları F tuşlarına taşındı.
                        var defaults = HotkeyDefaults.Create();
                        foreach (var key in defaults.Keys.Where(k => k.StartsWith("LoadPreset") || k.StartsWith("SavePreset") || k is "ModeCinematic" or "ModeInside"))
                            settings.Hotkeys[key] = defaults[key];
                        settings.Version = AppSettings.CurrentVersion;
                    }
                    foreach (var (action, key) in HotkeyDefaults.Create())
                        settings.Hotkeys.TryAdd(action, key);
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Bozuk dosya: varsayılanlarla devam et.
        }
        return new AppSettings { Version = AppSettings.CurrentVersion };
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
