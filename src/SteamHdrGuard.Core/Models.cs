namespace SteamHdrGuard.Core;

public sealed class AppConfig
{
    public bool ScanOnStart { get; set; } = true;
    public bool DefaultNewGamesEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 3;
    public int ExitDelaySeconds { get; set; } = 30;
    public bool RestorePreviousHdrState { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public List<GameEntry> Games { get; set; } = new();
}

public sealed class GameEntry
{
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string LibraryPath { get; set; } = "";
    public bool HdrEnabled { get; set; } = true;
    public string MatchMode { get; set; } = "path-under-install-dir";
}

public sealed class DisplayInfo
{
    public string MonitorName { get; set; } = "";
    public string MonitorDevicePath { get; set; } = "";
    public uint TargetId { get; set; }
    public uint AdapterLowPart { get; set; }
    public int AdapterHighPart { get; set; }
    public bool AdvancedColorSupported { get; set; }
    public bool AdvancedColorEnabled { get; set; }
    public bool WideColorEnforced { get; set; }
    public bool AdvancedColorForceDisabled { get; set; }
    public uint ColorEncoding { get; set; }
    public uint BitsPerColorChannel { get; set; }

    public string StableId => $"{AdapterHighPart:x8}:{AdapterLowPart:x8}:{TargetId}";
}

public sealed class GameProcessMatch
{
    public required GameEntry Game { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ExePath { get; init; }
}

public enum MonitorEventKind
{
    Started,
    GameDetected,
    GameExited,
    HdrEnabled,
    HdrDisabled,
    Info,
    Warning,
    Error
}

public sealed class MonitorEvent
{
    public MonitorEventKind Kind { get; init; }
    public string Message { get; init; } = "";
    public GameProcessMatch? Match { get; init; }
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;

    public override string ToString()
    {
        return $"[{Time:yyyy-MM-dd HH:mm:ss}] {Kind}: {Message}";
    }
}
