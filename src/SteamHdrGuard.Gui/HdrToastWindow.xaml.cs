using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SteamHdrGuard.Gui;

public partial class HdrToastWindow : Window
{
    private static HdrToastWindow? _current;
    private readonly DispatcherTimer _closeTimer;

    public HdrToastWindow(string message, string position, int durationMs)
    {
        InitializeComponent();
        MessageText.Text = string.IsNullOrWhiteSpace(message) ? "HDR" : message.Trim();

        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 600, 6000))
        };
        _closeTimer.Tick += (_, _) => BeginClose();

        Loaded += (_, _) =>
        {
            Place(position);
            FadeTo(1, 130);
            _closeTimer.Start();
        };
    }

    public static void ShowToast(string message, string position, int durationMs)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _current?.CloseImmediately();

            var toast = new HdrToastWindow(message, position, durationMs);
            _current = toast;
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, toast))
                {
                    _current = null;
                }
            };
            toast.Show();
        });
    }

    private void Place(string position)
    {
        Rect area = SystemParameters.WorkArea;
        const double margin = 28;
        string normalized = (position ?? "top-center").Trim().ToLowerInvariant();

        Left = normalized switch
        {
            "top-left" or "left-top" or "bottom-left" or "left-bottom" => area.Left + margin,
            "top-right" or "right-top" or "bottom-right" or "right-bottom" => area.Right - ActualWidth - margin,
            _ => area.Left + (area.Width - ActualWidth) / 2
        };

        Top = normalized switch
        {
            "center" or "middle" => area.Top + (area.Height - ActualHeight) / 2,
            "bottom-center" or "bottom" or "bottom-left" or "left-bottom" or "bottom-right" or "right-bottom" => area.Bottom - ActualHeight - margin,
            _ => area.Top + margin
        };
    }

    private void BeginClose()
    {
        _closeTimer.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    private void CloseImmediately()
    {
        _closeTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Close();
    }

    private void FadeTo(double opacity, int milliseconds)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
