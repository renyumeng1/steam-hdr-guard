using System.Collections.ObjectModel;
using System.Windows;
using SteamHdrGuard.Core;

namespace SteamHdrGuard.Gui;

public partial class MainWindow : Window
{
    private readonly HdrController _hdr = new();
    private AppConfig _config;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    public ObservableCollection<GameEntry> Games { get; } = new();
    public ObservableCollection<DisplayInfo> Displays { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigStore.Load();
        ConfigPathText.Text = "配置文件：" + ConfigStore.GetDefaultConfigPath();

        GamesGrid.ItemsSource = Games;
        DisplaysGrid.ItemsSource = Displays;

        LoadGamesFromConfig();
        RefreshDisplays();
        AppendLog("GUI 已启动。");
    }

    private void LoadGamesFromConfig()
    {
        Games.Clear();
        foreach (var game in _config.Games.OrderBy(g => g.Name))
        {
            Games.Add(game);
        }
    }

    private void PushGamesToConfig()
    {
        _config.Games = Games.ToList();
    }

    private void RefreshDisplays()
    {
        try
        {
            Displays.Clear();
            foreach (var display in _hdr.GetDisplays())
            {
                Displays.Add(display);
            }
        }
        catch (Exception ex)
        {
            AppendLog("刷新显示器失败：" + ex.Message);
        }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PushGamesToConfig();
            var (added, updated) = SteamLibraryScanner.MergeIntoConfig(_config);
            ConfigStore.Save(_config);
            LoadGamesFromConfig();
            AppendLog($"扫描完成：新增 {added} 个，更新 {updated} 个，总计 {_config.Games.Count} 个。");
        }
        catch (Exception ex)
        {
            AppendLog("扫描失败：" + ex.Message);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PushGamesToConfig();
            ConfigStore.Save(_config);
            AppendLog("配置已保存。");
        }
        catch (Exception ex)
        {
            AppendLog("保存失败：" + ex.Message);
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorCts is not null) return;

        PushGamesToConfig();
        ConfigStore.Save(_config);

        _monitorCts = new CancellationTokenSource();
        var monitor = new GameMonitor(_hdr);
        monitor.EventRaised += OnMonitorEvent;

        _monitorTask = Task.Run(async () =>
        {
            try
            {
                await monitor.RunAsync(_config, _monitorCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog("监控异常：" + ex.Message));
            }
        });

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        AppendLog("监控已启动。");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _monitorCts?.Cancel();
        _monitorCts = null;
        _monitorTask = null;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        AppendLog("监控已停止。");
    }

    private void HdrOnButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int count = _hdr.SetHdrForAllSupportedDisplays(true);
            AppendLog($"已开启 HDR，影响显示器数量：{count}");
            RefreshDisplays();
        }
        catch (Exception ex)
        {
            AppendLog("开启 HDR 失败：" + ex.Message);
        }
    }

    private void HdrOffButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int count = _hdr.SetHdrForAllSupportedDisplays(false);
            AppendLog($"已关闭 HDR，影响显示器数量：{count}");
            RefreshDisplays();
        }
        catch (Exception ex)
        {
            AppendLog("关闭 HDR 失败：" + ex.Message);
        }
    }

    private void RefreshDisplaysButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDisplays();
        AppendLog("显示器状态已刷新。");
    }

    private void OnMonitorEvent(MonitorEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            AppendLog(e.ToString());
            if (e.Kind is MonitorEventKind.HdrEnabled or MonitorEventKind.HdrDisabled)
            {
                RefreshDisplays();
            }
        });
    }

    private void AppendLog(string text)
    {
        LogBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCts?.Cancel();
        base.OnClosed(e);
    }
}
