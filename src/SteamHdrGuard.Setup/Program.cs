using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace SteamHdrGuard.Setup;

internal static class Program
{
    private const string AppName = "Steam HDR Guard";
    private const string Publisher = "renyumeng1";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Steam HDR Guard";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                return 0;
            }

            Install(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Steam HDR Guard installer failed:");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Install(string[] args)
    {
        string installDir = GetInstallDir(args);
        string packageDir = Path.Combine(installDir, "app");

        Console.WriteLine($"Installing {AppName} to {installDir}");
        StopKnownProcesses();

        Directory.CreateDirectory(packageDir);
        ExtractPayload(packageDir);
        CopySelfToInstallDir(installDir);
        CreateShortcuts(installDir, packageDir);
        RegisterUninstall(installDir, packageDir);

        Console.WriteLine("Installation completed.");
        Console.WriteLine($"GUI: {Path.Combine(packageDir, "gui", "SteamHdrGuard.Gui.exe")}");
    }

    private static string GetInstallDir(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--install-dir", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SteamHdrGuard");
    }

    private static void ExtractPayload(string packageDir)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("Installer payload not found.");

        Directory.CreateDirectory(packageDir);
        ZipFile.ExtractToDirectory(stream, packageDir, overwriteFiles: true);
    }

    private static void CopySelfToInstallDir(string installDir)
    {
        string? self = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(self) || !File.Exists(self)) return;

        string target = Path.Combine(installDir, "SteamHdrGuardSetup.exe");
        if (self.Equals(target, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(installDir);
        File.Copy(self, target, overwrite: true);
    }

    private static void CreateShortcuts(string installDir, string packageDir)
    {
        string guiExe = Path.Combine(packageDir, "gui", "SteamHdrGuard.Gui.exe");
        string cliExe = Path.Combine(packageDir, "cli", "steam-hdr-guard.exe");
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string group = Path.Combine(programs, AppName);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string uninstaller = Path.Combine(installDir, "SteamHdrGuardSetup.exe");

        CreateShortcut(Path.Combine(group, "Steam HDR Guard.lnk"), guiExe, Path.GetDirectoryName(guiExe)!, "Open Steam HDR Guard");
        CreateShortcut(Path.Combine(group, "Steam HDR Guard CLI.lnk"), cliExe, Path.GetDirectoryName(cliExe)!, "Open Steam HDR Guard CLI");
        CreateShortcut(Path.Combine(group, "Uninstall Steam HDR Guard.lnk"), uninstaller, installDir, "Uninstall Steam HDR Guard", "/uninstall");
        CreateShortcut(Path.Combine(desktop, "Steam HDR Guard.lnk"), guiExe, Path.GetDirectoryName(guiExe)!, "Open Steam HDR Guard");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description, string arguments = "")
    {
        if (!File.Exists(targetPath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = description;
        shortcut.Arguments = arguments;
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Save();
    }

    private static void RegisterUninstall(string installDir, string packageDir)
    {
        string setupExe = Path.Combine(installDir, "SteamHdrGuardSetup.exe");
        string guiExe = Path.Combine(packageDir, "gui", "SteamHdrGuard.Gui.exe");

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to create uninstall registry key.");

        key.SetValue("DisplayName", AppName, RegistryValueKind.String);
        key.SetValue("Publisher", Publisher, RegistryValueKind.String);
        key.SetValue("DisplayVersion", "1.0.0", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", guiExe, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{setupExe}\" /uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{setupExe}\" /uninstall /quiet", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void Uninstall()
    {
        string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SteamHdrGuard");

        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: false))
        {
            if (key?.GetValue("InstallLocation") is string value && !string.IsNullOrWhiteSpace(value))
            {
                installDir = value;
            }
        }

        Console.WriteLine($"Uninstalling {AppName} from {installDir}");
        StopKnownProcesses();
        RemoveShortcuts();
        RemoveStartupEntry();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        string? self = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(self) && self.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
        {
            ScheduleDeleteDirectory(installDir);
        }
        else if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
        }

        Console.WriteLine("Uninstall completed.");
    }

    private static void RemoveShortcuts()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string group = Path.Combine(programs, AppName);
        string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Steam HDR Guard.lnk");

        TryDeleteFile(desktopShortcut);
        if (Directory.Exists(group))
        {
            Directory.Delete(group, recursive: true);
        }
    }

    private static void RemoveStartupEntry()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue("Steam HDR Guard", throwOnMissingValue: false);
    }

    private static void StopKnownProcesses()
    {
        foreach (string name in new[] { "SteamHdrGuard.Gui", "steam-hdr-guard" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500)) process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private static void ScheduleDeleteDirectory(string installDir)
    {
        string cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        string args = $"/C timeout /T 2 /NOBREAK >NUL & rmdir /S /Q \"{installDir}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
