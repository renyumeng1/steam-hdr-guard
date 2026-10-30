using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SteamHdrGuard.Core;

public static class InstalledGameScanner
{
    private static readonly string[] LibraryRelativePaths =
    [
        "网易游戏",
        "暴雪",
        "WeGameApps",
        "Epic Games",
        "GOG Games",
        "Riot Games",
        "EA Games",
        "Origin Games",
        "XboxGames",
        "miHoYo",
        "HoYoPlay",
        @"Program Files\Epic Games",
        @"Program Files (x86)\Epic Games",
        @"Program Files\Riot Games",
        @"Program Files (x86)\Riot Games",
        @"Program Files\EA Games",
        @"Program Files (x86)\Origin Games",
        @"Program Files\GOG Galaxy\Games",
        @"Program Files (x86)\GOG Galaxy\Games",
        @"Program Files\Ubisoft\Ubisoft Game Launcher\games",
        @"Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games",
        "Games"
    ];

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "redist", "redistributables", "_commonredist", "directxredist", "directx",
        "installer", "installers", "support", "crash", "crashpad", "errorreporting",
        "cache", "cachedata", "logs", "log", "update", "updater", "node_modules",
        "aclos", "webview", "ccplayer", "cc_bin", "cc", "d3d12", "easyanticheat",
        "battleye", "plugins", "plugin", "resources", "browser", "blizzardbrowser",
        "diagnose", "diagnosetool", "proxy", "recorder", "recorder-release", "patch",
        "localdata", "thirdparty", "intermediate", "saved", "deriveddatacache",
        ".git", ".egstore", "launcher"
    };

    private static readonly string[] BadFileTokens =
    [
        "launcher", "uninstall", "uninstaller", "updater", "setup", "installer",
        "crash", "reporter", "redist", "vcredist", "dxsetup", "dotnetfx",
        "easyanticheat", "battleye", "beservice", "unitycrash", "crashpad",
        "overlay", "bootstrap", "d3dconfig", "ffmpeg", "ffprobe", "bugreport",
        "neac", "anticheat", "webview", "browser", "proxy", "diagnose",
        "shortcut", "ccmini", "unins", "登录器", "启动器", "卸载"
    ];

    public static (int Added, int Updated) MergeIntoConfig(AppConfig config)
    {
        int added = 0;
        int updated = 0;

        foreach (GameEntry game in Scan())
        {
            GameEntry? existing = config.Games.FirstOrDefault(old => SameGame(old, game));
            if (existing is null)
            {
                game.HdrEnabled = config.DefaultNewGamesEnabled;
                config.Games.Add(game);
                added++;
                continue;
            }

            if (!GameMatcher.IsCustom(existing))
            {
                continue;
            }

            bool changed = false;
            changed |= Assign(existing.Name, game.Name, value => existing.Name = value);
            changed |= Assign(existing.InstallDir, game.InstallDir, value => existing.InstallDir = value);
            changed |= Assign(existing.InstallPath, game.InstallPath, value => existing.InstallPath = value);
            changed |= Assign(existing.ExePath, game.ExePath, value => existing.ExePath = value);
            changed |= Assign(existing.ProcessName, game.ProcessName, value => existing.ProcessName = value);
            changed |= Assign(existing.MatchMode, game.MatchMode, value => existing.MatchMode = value);
            if (!string.Equals(existing.AppId, game.AppId, StringComparison.OrdinalIgnoreCase) &&
                existing.AppId.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            {
                existing.AppId = game.AppId;
                changed = true;
            }

            if (changed)
            {
                updated++;
            }
        }

        config.Games = config.Games
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (added, updated);
    }

    public static List<GameEntry> Scan()
    {
        var registryNames = ReadRegistryDisplayNames();
        var found = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in EnumerateLibraryRoots())
        {
            try
            {
                ScanLibraryRoot(root, registryNames, found);
            }
            catch
            {
            }
        }

        foreach ((string gameRoot, string displayName) in ReadRegistryGameRoots())
        {
            try
            {
                AddGame(found, gameRoot, displayName);
            }
            catch
            {
            }
        }

        return found.Values
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static GameEntry CreateManual(string exePath, string? displayName = null)
    {
        string full = Path.GetFullPath(exePath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("找不到这个程序。", full);
        }

        string processName = Path.GetFileNameWithoutExtension(full);
        string folder = Path.GetDirectoryName(full) ?? "";
        string name = string.IsNullOrWhiteSpace(displayName)
            ? ChooseDisplayName(full, null, processName)
            : displayName.Trim();

        return new GameEntry
        {
            AppId = GameMatcher.CreateLocalAppId(processName, full),
            Name = name,
            InstallDir = Path.GetFileName(full),
            InstallPath = folder,
            LibraryPath = "",
            ExePath = full,
            ProcessName = processName,
            HdrEnabled = true,
            MatchMode = GameMatcher.ProcessMatchMode
        };
    }

    public static bool LooksLikeLauncher(string exePath)
    {
        string file = Path.GetFileNameWithoutExtension(exePath);
        return ContainsToken(file, "launcher") || ContainsToken(file, "updater") || ContainsToken(file, "unins");
    }

    private static void ScanLibraryRoot(string root, Dictionary<string, string> registryNames, Dictionary<string, GameEntry> found)
    {
        if (Path.GetFileName(root).Equals("WeGameApps", StringComparison.OrdinalIgnoreCase))
        {
            string railApps = Path.Combine(root, "rail_apps");
            if (!Directory.Exists(railApps))
            {
                return;
            }

            foreach (string gameDir in SafeDirectories(railApps))
            {
                AddGame(found, gameDir, LookupName(registryNames, gameDir));
            }

            return;
        }

        foreach (string child in SafeDirectories(root))
        {
            string name = Path.GetFileName(child);
            if (IsSkippedLibraryChild(name))
            {
                continue;
            }

            if (name.Equals("games", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("rail_apps", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string gameDir in SafeDirectories(child))
                {
                    AddGame(found, gameDir, LookupName(registryNames, gameDir));
                }

                continue;
            }

            AddGame(found, child, LookupName(registryNames, child));
        }
    }

    private static void AddGame(Dictionary<string, GameEntry> found, string gameRoot, string? displayName)
    {
        if (!Directory.Exists(gameRoot) || IsTooBroad(gameRoot) || IsSteamGamePath(gameRoot))
        {
            return;
        }

        string? exe = PickExecutable(gameRoot, displayName);
        if (exe is null || IsSteamGamePath(exe))
        {
            return;
        }

        string processName = Path.GetFileNameWithoutExtension(exe);
        string key = processName.ToLowerInvariant();
        var entry = new GameEntry
        {
            AppId = GameMatcher.CreateLocalAppId(processName, exe),
            Name = ChooseDisplayName(exe, displayName, Path.GetFileName(gameRoot)),
            InstallDir = Path.GetFileName(gameRoot),
            InstallPath = Path.GetFullPath(gameRoot).TrimEnd('\\'),
            LibraryPath = "",
            ExePath = Path.GetFullPath(exe),
            ProcessName = processName,
            HdrEnabled = true,
            MatchMode = GameMatcher.ProcessMatchMode
        };

        if (!found.TryGetValue(key, out GameEntry? existing))
        {
            found[key] = entry;
            return;
        }

        long existingSize = SafeSize(existing.ExePath);
        long newSize = SafeSize(entry.ExePath);
        if (newSize > existingSize)
        {
            entry.HdrEnabled = existing.HdrEnabled;
            found[key] = entry;
        }
    }

    private static string? PickExecutable(string gameRoot, string? displayName)
    {
        string folderName = Path.GetFileName(gameRoot);
        string? bestPath = null;
        int bestScore = 39;

        foreach (string exe in EnumerateExecutables(gameRoot))
        {
            int score = ScoreExecutable(exe, folderName, displayName);
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = exe;
            }
        }

        return bestPath;
    }

    private static IEnumerable<string> EnumerateExecutables(string gameRoot)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((gameRoot, 0));
        int seen = 0;

        while (queue.Count > 0 && seen < 800)
        {
            (string path, int depth) = queue.Dequeue();
            foreach (string exe in SafeFiles(path, "*.exe"))
            {
                seen++;
                yield return exe;
                if (seen >= 800)
                {
                    yield break;
                }
            }

            if (depth >= 8)
            {
                continue;
            }

            foreach (string dir in SafeDirectories(path))
            {
                if (SkipDirectoryNames.Contains(Path.GetFileName(dir)))
                {
                    continue;
                }

                queue.Enqueue((dir, depth + 1));
            }
        }
    }

    private static int ScoreExecutable(string exePath, string folderName, string? displayName)
    {
        string file = Path.GetFileNameWithoutExtension(exePath);
        if (HasBadFileToken(file))
        {
            return int.MinValue / 4;
        }

        long size = SafeSize(exePath);
        int score = 0;
        if (size >= 20L * 1024 * 1024) score += 80;
        else if (size >= 5L * 1024 * 1024) score += 40;
        else if (size >= 1024 * 1024) score += 15;
        else score -= 25;

        if (NameSimilar(file, folderName) || NameSimilar(file, displayName)) score += 70;
        if (file.Contains("shipping", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (exePath.Contains(@"\Binaries\Win64", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (exePath.Contains(@"\Win64r\", StringComparison.OrdinalIgnoreCase) &&
            !exePath.Contains(@"\Win64rh\", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static bool HasBadFileToken(string fileName)
    {
        foreach (string token in BadFileTokens)
        {
            if (ContainsToken(fileName, token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsToken(string text, string token)
    {
        return text.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NameSimilar(string fileName, string? label)
    {
        string left = NormalizeKey(fileName);
        string right = NormalizeKey(label);
        if (left.Length < 3 || right.Length < 3)
        {
            return false;
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        string text = value.ToLowerInvariant();
        text = Regex.Replace(text, @"[\s_\-\(\)（）\[\]【】\.]+", "");
        text = Regex.Replace(text, @"\d+$", "");
        return text;
    }

    private static string ChooseDisplayName(string exePath, string? preferredName, string fallback)
    {
        string cleaned = CleanFolderName(preferredName);
        if (IsUsableName(cleaned, fallback))
        {
            return cleaned;
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
            string description = info.FileDescription ?? "";
            string product = info.ProductName ?? "";
            if (IsUsableName(description, fallback))
            {
                return description.Trim();
            }

            if (IsUsableName(product, fallback))
            {
                return product.Trim();
            }
        }
        catch
        {
        }

        string folder = CleanFolderName(fallback);
        return string.IsNullOrWhiteSpace(folder) ? Path.GetFileNameWithoutExtension(exePath) : folder;
    }

    private static bool IsUsableName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string text = name.Trim();
        if (text.Length < 2 || text.Length > 80)
        {
            return false;
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            return false;
        }

        string lower = text.ToLowerInvariant();
        if (lower is "application" or "game" or "app" or "unreal engine" or "unity engine")
        {
            return false;
        }

        if (lower.Contains("microsoft") || lower.Contains("copyright"))
        {
            return false;
        }

        return !text.Equals(fallback, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanFolderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return Regex.Replace(value.Trim(), @"\s*[\(（]\d+[\)）]\s*$", "").Trim();
    }

    private static IEnumerable<string> EnumerateLibraryRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            foreach (string relative in LibraryRelativePaths)
            {
                string path = Path.Combine(drive.RootDirectory.FullName, relative);
                if (Directory.Exists(path) && seen.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }
    }

    private static Dictionary<string, string> ReadRegistryDisplayNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string gameRoot, string displayName) in ReadRegistryGameRoots())
        {
            names.TryAdd(Path.GetFullPath(gameRoot).TrimEnd('\\'), displayName);
        }

        return names;
    }

    private static IEnumerable<(string GameRoot, string DisplayName)> ReadRegistryGameRoots()
    {
        foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (string subKey in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                RegistryKey? key = null;
                try
                {
                    key = hive.OpenSubKey(subKey);
                }
                catch
                {
                    continue;
                }

                if (key is null)
                {
                    continue;
                }

                using (key)
                {
                    foreach (string childName in key.GetSubKeyNames())
                    {
                        using RegistryKey? app = key.OpenSubKey(childName);
                        if (app is null)
                        {
                            continue;
                        }

                        string? displayName = app.GetValue("DisplayName") as string;
                        string? publisher = app.GetValue("Publisher") as string;
                        string? installLocation = app.GetValue("InstallLocation") as string;
                        string? displayIcon = app.GetValue("DisplayIcon") as string;
                        if (string.IsNullOrWhiteSpace(displayName) || IsIgnoredProduct(displayName))
                        {
                            continue;
                        }

                        string? root = ResolveGameRoot(installLocation, displayIcon);
                        if (root is null || IsTooBroad(root))
                        {
                            continue;
                        }

                        if (!IsGamePublisher(publisher) && !IsInsideKnownLibrary(root))
                        {
                            continue;
                        }

                        yield return (root, displayName.Trim());
                    }
                }
            }
        }
    }

    private static string? LookupName(Dictionary<string, string> names, string gameRoot)
    {
        string full = Path.GetFullPath(gameRoot).TrimEnd('\\');
        return names.TryGetValue(full, out string? name) ? name : null;
    }

    private static string? ResolveGameRoot(string? installLocation, string? displayIcon)
    {
        string? start = null;
        if (TryNormalizeDirectory(installLocation, out string? installDir))
        {
            start = installDir;
        }
        else
        {
            string? icon = CleanIconPath(displayIcon);
            if (icon is not null)
            {
                start = File.Exists(icon) ? Path.GetDirectoryName(icon) : Directory.Exists(icon) ? icon : null;
            }
        }

        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        foreach (string library in EnumerateLibraryRoots())
        {
            if (!GameMatcher.IsPathUnder(start, library) || GameMatcher.PathsEqual(start, library))
            {
                continue;
            }

            string relative = Path.GetRelativePath(library, start);
            string child = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (child.Equals("games", StringComparison.OrdinalIgnoreCase) ||
                child.Equals("rail_apps", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length >= 2)
                {
                    return Path.Combine(library, parts[0], parts[1]);
                }
            }

            return Path.Combine(library, child);
        }

        return start;
    }

    private static bool TryNormalizeDirectory(string? path, out string? directory)
    {
        directory = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string cleaned = path.Trim().Trim('"');
        if (Directory.Exists(cleaned))
        {
            directory = cleaned;
            return true;
        }

        return false;
    }

    private static string? CleanIconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        string cleaned = displayIcon.Trim().Trim('"');
        int comma = cleaned.LastIndexOf(',');
        if (comma > 2 && cleaned[(comma + 1)..].All(char.IsDigit))
        {
            cleaned = cleaned[..comma].Trim().Trim('"');
        }

        return cleaned;
    }

    private static bool IsInsideKnownLibrary(string path)
    {
        foreach (string library in EnumerateLibraryRoots())
        {
            if (GameMatcher.IsPathUnder(path, library))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGamePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return false;
        }

        string[] tokens =
        [
            "netease", "网易", "blizzard", "暴雪", "tencent", "腾讯", "epic games", "ubisoft",
            "electronic arts", "riot", "mihoyo", "hoyoverse", "米哈游", "库洛", "kuro",
            "perfect world", "完美世界", "西山居", "鹰角", "hypergryph", "叠纸", "infold",
            "bethesda", "rockstar", "capcom", "sega", "bandai", "square enix", "activision",
            "bungie", "warner", "cd projekt", "xbox game", "雷火", "盛趣", "巨人网络", "畅游"
        ];

        foreach (string token in tokens)
        {
            if (publisher.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoredProduct(string name)
    {
        string text = name.Trim().ToLowerInvariant();
        if (text is "qq" or "微信" or "wechat" or "wegame" or "epic games" or "battle.net")
        {
            return true;
        }

        string[] tokens =
        [
            "会议", "音乐", "输入法", "浏览器", "远程", "安全", "管家", "压缩", "播放器",
            "prerequisite", "redistributable", "runtime", "启动器", "launcher", "战网",
            "battle.net", "卸载", "uninstall", "助手", "加速器", "网盘", "云音乐",
            "cloudmusic", "录屏", "直播", "wallpaper"
        ];

        foreach (string token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSkippedLibraryChild(string name)
    {
        return SkipDirectoryNames.Contains(name) ||
               name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Agent", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Engine", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Portal", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("common_apps", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("downloading", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("rail_user_data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTooBroad(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('\\');
        }
        catch
        {
            return true;
        }

        if (full.Length <= 3)
        {
            return true;
        }

        string name = Path.GetFileName(full);
        return name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Users", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSteamGamePath(string path)
    {
        return path.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameGame(GameEntry left, GameEntry right)
    {
        if (!string.IsNullOrWhiteSpace(left.AppId) &&
            left.AppId.Equals(right.AppId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (GameMatcher.PathsEqual(left.ExePath, right.ExePath))
        {
            return true;
        }

        return GameMatcher.IsCustom(left) &&
               GameMatcher.ProcessNamesEqual(left.ProcessName, right.ProcessName);
    }

    private static bool Assign(string current, string next, Action<string> set)
    {
        if (string.IsNullOrWhiteSpace(next) || string.Equals(current, next, StringComparison.Ordinal))
        {
            return false;
        }

        set(next);
        return true;
    }

    private static long SafeSize(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? 0 : new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeFiles(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
