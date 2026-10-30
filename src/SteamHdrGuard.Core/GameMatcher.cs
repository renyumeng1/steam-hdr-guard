using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SteamHdrGuard.Core;

public static class GameMatcher
{
    public const string SteamMatchMode = "path-under-install-dir";
    public const string ProcessMatchMode = "process";

    public static bool IsCustom(GameEntry game)
    {
        if (game.AppId?.StartsWith("local:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (game.AppId?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return string.Equals(game.MatchMode, ProcessMatchMode, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanMonitor(GameEntry game)
    {
        if (!game.HdrEnabled)
        {
            return false;
        }

        if (IsCustom(game))
        {
            return !string.IsNullOrWhiteSpace(game.ProcessName) || !string.IsNullOrWhiteSpace(game.ExePath);
        }

        return !string.IsNullOrWhiteSpace(game.InstallPath);
    }

    public static int Score(GameEntry game, string exePath, string processName)
    {
        if (IsCustom(game))
        {
            if (!string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(game.ExePath))
            {
                if (PathsEqual(exePath, game.ExePath))
                {
                    return 100;
                }

                if (FileNamesEqual(exePath, game.ExePath))
                {
                    return 80;
                }

                return 0;
            }

            return ProcessNamesEqual(processName, game.ProcessName) ? 50 : 0;
        }

        if (!string.IsNullOrWhiteSpace(exePath) &&
            !string.IsNullOrWhiteSpace(game.InstallPath) &&
            IsPathUnder(exePath, game.InstallPath))
        {
            return 10;
        }

        return 0;
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            string a = Path.GetFullPath(left).TrimEnd('\\', '/');
            string b = Path.GetFullPath(right).TrimEnd('\\', '/');
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool FileNamesEqual(string left, string right)
    {
        return Path.GetFileName(left).Equals(Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ProcessNamesEqual(string? left, string? right)
    {
        string a = NormalizeProcessName(left);
        string b = NormalizeProcessName(right);
        return a.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        string name = value.Trim().Trim('"');
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return name;
    }

    public static bool IsPathUnder(string childPath, string parentPath)
    {
        try
        {
            string child = Path.GetFullPath(childPath).TrimEnd('\\', '/');
            string parent = Path.GetFullPath(parentPath).TrimEnd('\\', '/');
            return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                   child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   child.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string CreateLocalAppId(string processName, string exePath)
    {
        string process = NormalizeProcessName(processName).ToLowerInvariant();
        if (process.Length == 0)
        {
            process = "game";
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())))[..8].ToLowerInvariant();
        return "local:" + process + ":" + hash;
    }
}
