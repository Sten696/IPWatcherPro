using System.Text.Json;

namespace IPWatcherPro.Core;

public sealed record AppConfiguration
{
    // ── General Settings ──────────────────────────────────────────────────────
    public string TargetCountryCode { get; init; } = "US";
    public int RefreshIntervalMinutes { get; init; } = 5;
    public bool NotifyOnCountryChange { get; init; } = true;
    public bool AutoStart { get; init; } = false;
    public int HunterActivityMaxItems { get; set; } = 500;
    public int LogMaxFileMb { get; set; } = 10;

    // ── Hunter Mode ───────────────────────────────────────────────────────────
    public bool HunterMode_AggressivePolling { get; init; } = false;
    public bool HunterMode_LogAllChanges { get; init; } = false;
    public bool HunterMode_ExtendedScan { get; init; } = false;

    // ── Paths ─────────────────────────────────────────────────────────────────
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPWatcherPro");

    public static string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    // ── Load / Save ───────────────────────────────────────────────────────────
    public static AppConfiguration Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new AppConfiguration();

            string json = File.ReadAllText(ConfigFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<AppConfiguration>(json, options);
            return result ?? new AppConfiguration();
        }
        catch
        {
            return new AppConfiguration();
        }
    }

    public static void Save(AppConfiguration config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // Ignore save errors to prevent crashes
        }
    }
}