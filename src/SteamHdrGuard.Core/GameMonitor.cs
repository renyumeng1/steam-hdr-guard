using System.Diagnostics;

namespace SteamHdrGuard.Core;

public sealed class GameMonitor
{
    private readonly HdrController _hdrController;

    public GameMonitor(HdrController? hdrController = null)
    {
        _hdrController = hdrController ?? new HdrController();
    }

    public event Action<MonitorEvent>? EventRaised;

    public async Task RunAsync(AppConfig config, CancellationToken cancellationToken)
    {
        Raise(MonitorEventKind.Started, "开始监控 Steam 游戏进程。");

        bool initialHdrState = SafeGetHdrState();
        bool changedByThisProcess = false;
        GameProcessMatch? active = null;
        DateTimeOffset? inactiveSince = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            GameProcessMatch? match = FindActiveGame(config);

            if (match is not null)
            {
                inactiveSince = null;

                if (active?.Game.AppId != match.Game.AppId)
                {
                    active = match;
                    Raise(MonitorEventKind.GameDetected, $"检测到游戏启动：{match.Game.Name} ({match.ProcessName}, PID {match.ProcessId})", match);
                }

                if (!SafeGetHdrState())
                {
                    try
                    {
                        int count = _hdrController.SetHdrForAllSupportedDisplays(true);
                        changedByThisProcess = true;
                        Raise(MonitorEventKind.HdrEnabled, $"已开启 HDR，影响显示器数量：{count}", match);
                    }
                    catch (Exception ex)
                    {
                        Raise(MonitorEventKind.Error, $"开启 HDR 失败：{ex.Message}", match);
                    }
                }
            }
            else
            {
                if (active is not null && inactiveSince is null)
                {
                    inactiveSince = DateTimeOffset.Now;
                    Raise(MonitorEventKind.GameExited, $"游戏已退出：{active.Game.Name}，等待 {config.ExitDelaySeconds} 秒后恢复。", active);
                }

                if (active is not null && inactiveSince is not null && DateTimeOffset.Now - inactiveSince.Value >= TimeSpan.FromSeconds(Math.Max(0, config.ExitDelaySeconds)))
                {
                    if (ShouldDisableHdr(config, initialHdrState, changedByThisProcess))
                    {
                        try
                        {
                            int count = _hdrController.SetHdrForAllSupportedDisplays(false);
                            Raise(MonitorEventKind.HdrDisabled, $"已关闭 HDR，影响显示器数量：{count}", active);
                        }
                        catch (Exception ex)
                        {
                            Raise(MonitorEventKind.Error, $"关闭 HDR 失败：{ex.Message}", active);
                        }
                    }

                    active = null;
                    inactiveSince = null;
                    changedByThisProcess = false;
                    initialHdrState = SafeGetHdrState();
                }
            }

            int delay = Math.Max(1, config.PollIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
        }
    }

    public GameProcessMatch? FindActiveGame(AppConfig config)
    {
        var enabledGames = config.Games.Where(g => g.HdrEnabled && !string.IsNullOrWhiteSpace(g.InstallPath)).ToList();
        if (enabledGames.Count == 0)
        {
            return null;
        }

        foreach (Process process in Process.GetProcesses())
        {
            string exePath;
            try
            {
                exePath = process.MainModule?.FileName ?? "";
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                continue;
            }

            foreach (GameEntry game in enabledGames)
            {
                if (IsPathUnder(exePath, game.InstallPath))
                {
                    return new GameProcessMatch
                    {
                        Game = game,
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        ExePath = exePath
                    };
                }
            }
        }

        return null;
    }

    private bool SafeGetHdrState()
    {
        try
        {
            return _hdrController.IsAnyHdrEnabled();
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldDisableHdr(AppConfig config, bool initialHdrState, bool changedByThisProcess)
    {
        if (!changedByThisProcess) return false;
        if (!config.RestorePreviousHdrState) return true;
        return initialHdrState == false;
    }

    private void Raise(MonitorEventKind kind, string message, GameProcessMatch? match = null)
    {
        EventRaised?.Invoke(new MonitorEvent { Kind = kind, Message = message, Match = match });
    }

    private static bool IsPathUnder(string childPath, string parentPath)
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
}
