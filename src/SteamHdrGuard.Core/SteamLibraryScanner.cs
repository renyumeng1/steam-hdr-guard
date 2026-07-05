using Microsoft.Win32;

namespace SteamHdrGuard.Core;

public static class SteamLibraryScanner
{
    public static List<GameEntry> Scan()
    {
        var result = new List<GameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in DiscoverSteamRoots())
        {
            foreach (string library in DiscoverLibraries(steamRoot))
            {
                string steamApps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamApps))
                {
                    continue;
                }

                foreach (string manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    GameEntry? game = ParseManifest(manifest, library);
                    if (game is null)
                    {
                        continue;
                    }

                    if (seen.Add(game.AppId))
                    {
                        result.Add(game);
                    }
                }
            }
        }

        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static (int Added, int Updated) MergeIntoConfig(AppConfig config)
    {
        var scanned = Scan();
        var existing = config.Games
            .Where(g => !string.IsNullOrWhiteSpace(g.AppId))
            .ToDictionary(g => g.AppId, StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int updated = 0;

        foreach (var game in scanned)
        {
            if (existing.TryGetValue(game.AppId, out var old))
            {
                bool changed = false;

                if (!string.Equals(old.Name, game.Name, StringComparison.Ordinal)) { old.Name = game.Name; changed = true; }
                if (!string.Equals(old.InstallDir, game.InstallDir, StringComparison.Ordinal)) { old.InstallDir = game.InstallDir; changed = true; }
                if (!string.Equals(old.InstallPath, game.InstallPath, StringComparison.Ordinal)) { old.InstallPath = game.InstallPath; changed = true; }
                if (!string.Equals(old.LibraryPath, game.LibraryPath, StringComparison.Ordinal)) { old.LibraryPath = game.LibraryPath; changed = true; }

                if (string.IsNullOrWhiteSpace(old.MatchMode))
                {
                    old.MatchMode = "path-under-install-dir";
                    changed = true;
                }

                if (changed)
                {
                    updated++;
                }
            }
            else
            {
                game.HdrEnabled = config.DefaultNewGamesEnabled;
                game.MatchMode = "path-under-install-dir";
                existing.Add(game.AppId, game);
                added++;
            }
        }

        config.Games = existing.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return (added, updated);
    }

    private static IEnumerable<string> DiscoverSteamRoots()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("STEAM_PATH"),
            ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath"),
            ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string path = NormalizePath(raw);
            if (!Directory.Exists(Path.Combine(path, "steamapps")))
            {
                continue;
            }

            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> DiscoverLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        string libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

        if (File.Exists(libraryFolders))
        {
            string text = File.ReadAllText(libraryFolders);
            foreach (string path in Vdf.GetValues(text, "path"))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    libraries.Add(path);
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in libraries)
        {
            string path = NormalizePath(raw);
            if (Directory.Exists(Path.Combine(path, "steamapps")) && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static GameEntry? ParseManifest(string manifestPath, string libraryPath)
    {
        string text = File.ReadAllText(manifestPath);

        string? appId = Vdf.GetValue(text, "appid");
        string? name = Vdf.GetValue(text, "name");
        string? installDir = Vdf.GetValue(text, "installdir");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        string installPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
        if (!Directory.Exists(installPath))
        {
            return null;
        }

        return new GameEntry
        {
            AppId = appId,
            Name = name,
            InstallDir = installDir,
            InstallPath = NormalizePath(installPath),
            LibraryPath = NormalizePath(libraryPath),
            HdrEnabled = true,
            MatchMode = "path-under-install-dir"
        };
    }

    private static string? ReadRegistryString(RegistryKey root, string subKey, string name)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim().Replace('/', '\\')).TrimEnd('\\');
        }
        catch
        {
            return path.Trim().Replace('/', '\\').TrimEnd('\\');
        }
    }
}
