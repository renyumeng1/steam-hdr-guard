using Microsoft.Win32;
using System.Diagnostics;

namespace SteamHdrGuard.Core;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Steam HDR Guard";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled, string? executablePath = null, string arguments = "--start-minimized")
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 当前用户开机自启注册表项。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定当前程序路径，不能设置开机自启。");
        }

        string command = Quote(executablePath);
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            command += " " + arguments.Trim();
        }

        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "") + "\"";
    }
}
