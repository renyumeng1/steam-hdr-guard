using SteamHdrGuard.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "config" => ShowConfigPath(),
                "scan" => Scan(),
                "list" => ListGames(),
                "enable" => SetGame(args.Skip(1).ToArray(), true),
                "disable" => SetGame(args.Skip(1).ToArray(), false),
                "hdr" => Hdr(args.Skip(1).ToArray()),
                "displays" => Displays(),
                "watch" => await Watch(),
                _ => Unknown(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 1;
        }
    }

    private static int ShowConfigPath()
    {
        Console.WriteLine(ConfigStore.GetDefaultConfigPath());
        return 0;
    }

    private static int Scan()
    {
        var config = ConfigStore.Load();
        var (added, updated) = SteamLibraryScanner.MergeIntoConfig(config);
        ConfigStore.Save(config);
        int enabled = config.Games.Count(g => g.HdrEnabled);
        Console.WriteLine($"扫描完成：总计 {config.Games.Count} 个游戏，新增 {added} 个，更新 {updated} 个，HDR 监控启用 {enabled} 个。");
        Console.WriteLine($"配置文件：{ConfigStore.GetDefaultConfigPath()}");
        return 0;
    }

    private static int ListGames()
    {
        var config = ConfigStore.Load();
        if (config.Games.Count == 0)
        {
            Console.WriteLine("配置中还没有游戏。请先运行 scan。");
            return 0;
        }
        foreach (var game in config.Games)
        {
            string mark = game.HdrEnabled ? "ON " : "OFF";
            Console.WriteLine($"[{mark}] {game.AppId,10}  {game.Name}");
        }
        return 0;
    }

    private static int SetGame(string[] args, bool enabled)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(enabled ? "用法：enable <appid 或名称关键字>" : "用法：disable <appid 或名称关键字>");
            return 1;
        }
        string target = string.Join(' ', args).Trim();
        var config = ConfigStore.Load();
        int changed = 0;
        foreach (var game in config.Games)
        {
            if (game.AppId.Equals(target, StringComparison.OrdinalIgnoreCase) || game.Name.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                game.HdrEnabled = enabled;
                changed++;
            }
        }
        ConfigStore.Save(config);
        Console.WriteLine($"已{(enabled ? "启用" : "禁用")} {changed} 个匹配游戏。");
        return 0;
    }

    private static int Hdr(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法：hdr status|on|off");
            return 1;
        }
        var hdr = new HdrController();
        switch (args[0].ToLowerInvariant())
        {
            case "status":
                var displays = hdr.GetDisplays();
                if (displays.Count == 0)
                {
                    Console.WriteLine("没有读取到活动显示器 HDR 信息。");
                    return 0;
                }
                foreach (var display in displays)
                {
                    Console.WriteLine($"{display.MonitorName}: supported={display.AdvancedColorSupported}, enabled={display.AdvancedColorEnabled}, forceDisabled={display.AdvancedColorForceDisabled}, bpc={display.BitsPerColorChannel}");
                }
                return 0;
            case "on":
                Console.WriteLine($"已开启 HDR，影响显示器数量：{hdr.SetHdrForAllSupportedDisplays(true)}");
                return 0;
            case "off":
                Console.WriteLine($"已关闭 HDR，影响显示器数量：{hdr.SetHdrForAllSupportedDisplays(false)}");
                return 0;
            default:
                Console.WriteLine("用法：hdr status|on|off");
                return 1;
        }
    }

    private static int Displays()
    {
        var hdr = new HdrController();
        foreach (var display in hdr.GetDisplays())
        {
            Console.WriteLine($"{display.MonitorName}");
            Console.WriteLine($"  id: {display.StableId}");
            Console.WriteLine($"  supported: {display.AdvancedColorSupported}");
            Console.WriteLine($"  enabled: {display.AdvancedColorEnabled}");
            Console.WriteLine($"  force disabled: {display.AdvancedColorForceDisabled}");
            Console.WriteLine($"  bits/channel: {display.BitsPerColorChannel}");
            Console.WriteLine($"  path: {display.MonitorDevicePath}");
        }
        return 0;
    }

    private static async Task<int> Watch()
    {
        var config = ConfigStore.Load();
        if (config.ScanOnStart)
        {
            var (added, updated) = SteamLibraryScanner.MergeIntoConfig(config);
            if (added > 0 || updated > 0)
            {
                ConfigStore.Save(config);
                Console.WriteLine($"启动扫描完成：新增 {added} 个，更新 {updated} 个。");
            }
        }
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var monitor = new GameMonitor();
        monitor.EventRaised += e => Console.WriteLine(e.ToString());
        await monitor.RunAsync(config, cts.Token);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.WriteLine($"未知命令：{command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Steam HDR Guard

用法：
  steam-hdr-guard config
  steam-hdr-guard scan
  steam-hdr-guard list
  steam-hdr-guard enable <appid 或名称关键字>
  steam-hdr-guard disable <appid 或名称关键字>
  steam-hdr-guard displays
  steam-hdr-guard hdr status
  steam-hdr-guard hdr on
  steam-hdr-guard hdr off
  steam-hdr-guard watch
""");
    }
}
