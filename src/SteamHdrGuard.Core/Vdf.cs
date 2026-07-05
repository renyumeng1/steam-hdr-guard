using System.Text.RegularExpressions;

namespace SteamHdrGuard.Core;

internal static class Vdf
{
    public static string? GetValue(string text, string key)
    {
        var match = Regex.Match(
            text,
            "\"" + Regex.Escape(key) + "\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        return Unescape(match.Groups[1].Value);
    }

    public static IEnumerable<string> GetValues(string text, string key)
    {
        foreach (Match match in Regex.Matches(
                     text,
                     "\"" + Regex.Escape(key) + "\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return Unescape(match.Groups[1].Value);
        }
    }

    public static string Unescape(string value)
    {
        return value
            .Replace("\\\\", "\\")
            .Replace("\\\"", "\"")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t");
    }
}
