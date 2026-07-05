using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteamHdrGuard.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SteamHdrGuard.Gui;

public partial class MainWindow : Window
{
    private readonly HdrController _hdr = new();
    private readonly Drawing.Icon _appIcon;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly ImageSource _defaultGameIcon;
    private readonly Dictionary<string, ImageSource> _gameIconCache = new(StringComparer.OrdinalIgnoreCase);
    private AppConfig _config;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private bool _loadingSettings;
    private bool _allowRealClose;

    public ObservableCollection<GameRow> Games { get; } = new();
    public ObservableCollection<DisplayInfo> Displays { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        _appIcon = CreateMinimalIcon();
        Icon = CreateImageSourceFromIcon(_appIcon, 32);
        _defaultGameIcon = CreateDefaultGameIconSource();
        _trayIcon = CreateTrayIcon(_appIcon);

        _config = ConfigStore.Load();
        ConfigPathText.Text = "配置文件：" + ConfigStore.GetDefaultConfigPath();

        GamesGrid.ItemsSource = Games;
        DisplaysGrid.ItemsSource = Displays;

        LoadSettingsToUi();
        LoadGamesFromConfig();
        RefreshDisplays();
        AppendLog("GUI 已启动。");

        Loaded += (_, _) =>
        {
            bool startMinimized = Environment.GetCommandLineArgs().Any(x => x.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase));

            if (_config.StartMonitoringOnLaunch)
            {
                StartMonitoring();
            }

            if (startMinimized)
            {
                HideToTray(showTip: false);
            }
        };
    }

    private void LoadSettingsToUi()
    {
        _loadingSettings = true;
        ExitDelayBox.Text = Math.Max(0, _config.ExitDelaySeconds).ToString();
        StartupCheckBox.IsChecked = StartupManager.IsEnabled();
        AutoStartMonitoringCheckBox.IsChecked = _config.StartMonitoringOnLaunch;
        MinimizeToTrayCheckBox.IsChecked = _config.MinimizeToTrayOnClose;
        RestorePreviousCheckBox.IsChecked = _config.RestorePreviousHdrState;
        SettingsStatusText.Text = "设置已加载。";
        _loadingSettings = false;
    }

    private bool ApplySettingsFromUi(bool persistStartup = true)
    {
        if (!int.TryParse(ExitDelayBox.Text.Trim(), out int exitDelay))
        {
            SettingsStatusText.Text = "退出延迟必须是数字。";
            return false;
        }

        exitDelay = Math.Clamp(exitDelay, 0, 3600);
        ExitDelayBox.Text = exitDelay.ToString();
        _config.ExitDelaySeconds = exitDelay;
        _config.StartWithWindows = StartupCheckBox.IsChecked == true;
        _config.StartMonitoringOnLaunch = AutoStartMonitoringCheckBox.IsChecked == true;
        _config.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
        _config.RestorePreviousHdrState = RestorePreviousCheckBox.IsChecked == true;

        if (persistStartup)
        {
            StartupManager.SetEnabled(_config.StartWithWindows);
        }

        return true;
    }

    private void LoadGamesFromConfig()
    {
        Games.Clear();
        foreach (var game in _config.Games.OrderBy(g => g.Name))
        {
            Games.Add(new GameRow(game, GetGameIcon(game)));
        }
    }

    private void PushGamesToConfig()
    {
        _config.Games = Games.Select(g => g.Game).ToList();
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
            if (!ApplySettingsFromUi()) return;
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
        SaveConfigFromUi();
    }

    private void SaveConfigFromUi()
    {
        try
        {
            if (!ApplySettingsFromUi()) return;
            PushGamesToConfig();
            ConfigStore.Save(_config);
            SettingsStatusText.Text = _config.StartWithWindows ? "设置已保存，开机自启已开启。" : "设置已保存。";
            AppendLog("配置已保存。");
        }
        catch (Exception ex)
        {
            AppendLog("保存失败：" + ex.Message);
            SettingsStatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (_monitorCts is not null) return;

        if (!ApplySettingsFromUi()) return;
        PushGamesToConfig();
        ConfigStore.Save(_config);

        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        var monitor = new GameMonitor(_hdr);
        monitor.EventRaised += OnMonitorEvent;

        _monitorTask = Task.Run(async () =>
        {
            try
            {
                await monitor.RunAsync(_config, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog("监控异常：" + ex.Message));
            }
            finally
            {
                monitor.EventRaised -= OnMonitorEvent;
            }
        }, token);

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        UpdateTrayMenuText();
        AppendLog("监控已启动。");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoring();
    }

    private void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        UpdateTrayMenuText();
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

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;

        try
        {
            bool enabled = StartupCheckBox.IsChecked == true;
            StartupManager.SetEnabled(enabled);
            _config.StartWithWindows = enabled;
            ConfigStore.Save(_config);
            SettingsStatusText.Text = enabled ? "开机自启已开启。" : "开机自启已关闭。";
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "开机自启设置失败：" + ex.Message;
            AppendLog(SettingsStatusText.Text);
        }
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

    private Forms.NotifyIcon CreateTrayIcon(Drawing.Icon icon)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("开始监控", null, (_, _) => Dispatcher.Invoke(StartMonitoring));
        menu.Items.Add("停止监控", null, (_, _) => Dispatcher.Invoke(StopMonitoring));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Steam HDR Guard",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        return tray;
    }

    private void UpdateTrayMenuText()
    {
        if (_trayIcon.ContextMenuStrip is null) return;
        _trayIcon.Text = _monitorCts is null ? "Steam HDR Guard" : "Steam HDR Guard - 监控中";
    }

    private void HideToTray(bool showTip = true)
    {
        ShowInTaskbar = false;
        Hide();
        if (showTip)
        {
            _trayIcon.ShowBalloonTip(1800, "Steam HDR Guard", "程序已最小化到托盘，监控会继续运行。", Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowRealClose = true;
        Close();
    }

    private ImageSource GetGameIcon(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.AppId))
        {
            return _defaultGameIcon;
        }

        if (_gameIconCache.TryGetValue(game.AppId, out var cached))
        {
            return cached;
        }

        var icon = TryLoadAssociatedGameIcon(game) ?? _defaultGameIcon;
        _gameIconCache[game.AppId] = icon;
        return icon;
    }

    private ImageSource? TryLoadAssociatedGameIcon(GameEntry game)
    {
        foreach (string exe in FindCandidateExecutables(game).Take(8))
        {
            try
            {
                using Drawing.Icon? icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is null) continue;
                return CreateImageSourceFromIcon(icon, 18);
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> FindCandidateExecutables(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath))
        {
            yield break;
        }

        var found = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((game.InstallPath, 0));

        while (queue.Count > 0 && found.Count < 64)
        {
            var current = queue.Dequeue();
            foreach (string exe in SafeEnumerateFiles(current.Path, "*.exe"))
            {
                found.Add(exe);
                if (found.Count >= 64) break;
            }

            if (current.Depth >= 3) continue;

            foreach (string dir in SafeEnumerateDirectories(current.Path))
            {
                if (ShouldSkipIconDirectory(dir)) continue;
                queue.Enqueue((dir, current.Depth + 1));
            }
        }

        foreach (var item in found
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Select(path => new { Path = path, Score = ScoreExecutable(path, game) })
                     .OrderByDescending(x => x.Score)
                     .ThenBy(x => x.Path.Length))
        {
            yield return item.Path;
        }
    }

    private static int ScoreExecutable(string path, GameEntry game)
    {
        string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string install = game.InstallDir.ToLowerInvariant().Replace(" ", "");
        string name = game.Name.ToLowerInvariant().Replace(" ", "");
        string full = path.ToLowerInvariant();
        int score = 0;

        if (file.Replace(" ", "").Contains(install)) score += 40;
        if (name.Length > 0 && file.Replace(" ", "").Contains(name)) score += 35;
        if (string.Equals(Path.GetDirectoryName(path)?.TrimEnd('\\'), game.InstallPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) score += 15;
        if (file.Contains("launcher")) score -= 6;
        if (file.Contains("crash") || file.Contains("report")) score -= 25;
        if (file.Contains("setup") || file.Contains("install") || file.Contains("unins")) score -= 40;
        if (full.Contains("redist") || full.Contains("support") || full.Contains("installer")) score -= 20;

        return score;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern)
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

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
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

    private static bool ShouldSkipIconDirectory(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        return name.Contains("redist") || name.Contains("installer") || name.Contains("support") || name.Contains("crash") || name is "_commonredist";
    }

    private static Drawing.Icon CreateMinimalIcon()
    {
        using var bitmap = new Drawing.Bitmap(64, 64);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);

        using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 17, 17, 17));
        using var ring = new Drawing.Pen(Drawing.Color.White, 3.2f);
        using var textBrush = new Drawing.SolidBrush(Drawing.Color.White);
        using var font = new Drawing.Font("Segoe UI", 12, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);

        g.FillEllipse(bg, 5, 5, 54, 54);
        g.DrawEllipse(ring, 15, 15, 34, 34);
        var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        g.DrawString("HDR", font, textBrush, new Drawing.RectangleF(5, 5, 54, 54), format);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private ImageSource CreateDefaultGameIconSource()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);
        using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 245, 247));
        using var stroke = new Drawing.Pen(Drawing.Color.FromArgb(142, 142, 147), 1.6f);
        using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(142, 142, 147));
        g.FillRectangle(bg, 6, 10, 20, 14);
        g.DrawRectangle(stroke, 6, 10, 20, 14);
        g.FillEllipse(dot, 10, 14, 4, 4);
        g.FillEllipse(dot, 18, 14, 4, 4);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
            return CreateImageSourceFromIcon(icon, 18);
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static ImageSource CreateImageSourceFromIcon(Drawing.Icon icon, int size)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
        source.Freeze();
        return source;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose && MinimizeToTrayCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            SaveConfigFromUi();
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        base.OnClosed(e);
    }
}

public sealed class GameRow : INotifyPropertyChanged
{
    public GameRow(GameEntry game, ImageSource icon)
    {
        Game = game;
        Icon = icon;
    }

    public GameEntry Game { get; }
    public ImageSource Icon { get; }
    public string AppId => Game.AppId;
    public string Name => Game.Name;
    public string InstallPath => Game.InstallPath;

    public bool HdrEnabled
    {
        get => Game.HdrEnabled;
        set
        {
            if (Game.HdrEnabled == value) return;
            Game.HdrEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HdrEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
