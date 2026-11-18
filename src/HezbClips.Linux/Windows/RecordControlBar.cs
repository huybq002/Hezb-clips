using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;

namespace HezbClips.Linux.Windows;

/// <summary>Recording controls. Wayland cannot hide a window from a screencast, so keep it outside the recorded area.</summary>
public class RecordControlBar : Window
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DateTime _t0 = DateTime.UtcNow;
    readonly TextBlock _clock = new() { Text = "00:00", VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };

    public event Action? StopRequested;
    public event Action<bool>? PauseChanged;

    public RecordControlBar()
    {
        Title = "Hezb Clips - Đang quay";
        SystemDecorations = SystemDecorations.BorderOnly;
        Topmost = true; ShowInTaskbar = false; CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var pause = new ToggleButton { Content = "⏸ Tạm dừng" };
        pause.IsCheckedChanged += (_, _) => PauseChanged?.Invoke(pause.IsChecked == true);
        var stop = new Button { Content = "■ Dừng", Background = Avalonia.Media.Brushes.Firebrick };
        stop.Click += (_, _) => StopRequested?.Invoke();

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 6),
            Children = { _clock, pause, stop },
        };

        Opened += (_, _) =>
        {
            var s = Screens.Primary;
            if (s != null) Position = new PixelPoint(s.WorkingArea.Right - (int)(Bounds.Width * s.Scaling) - 16, s.WorkingArea.Y + 16);
        };
        _timer.Tick += (_, _) => _clock.Text = (DateTime.UtcNow - _t0).ToString(@"mm\:ss");
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }
}
