using System.Text.Json;

namespace SteamHdrGuard.Core;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string GetDefaultConfigPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("STEAM_HDR_GUARD_CONFIG");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SteamHdrGuard", "config.json");
    }

    public static AppConfig Load(string? path = null)
    {
        path ??= GetDefaultConfigPath();
        if (!File.Exists(path))
        {
            var config = new AppConfig();
            Save(config, path);
            return config;
        }

        string json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        loaded.Games ??= new List<GameEntry>();
        foreach (GameEntry game in loaded.Games)
        {
            game.AppId ??= "";
            game.Name ??= "";
            game.InstallDir ??= "";
            game.InstallPath ??= "";
            game.LibraryPath ??= "";
            game.MatchMode ??= "";
            game.ExePath ??= "";
            game.ProcessName ??= "";
        }

        return loaded;
    }

    public static void Save(AppConfig config, string? path = null)
    {
        path ??= GetDefaultConfigPath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }
}
