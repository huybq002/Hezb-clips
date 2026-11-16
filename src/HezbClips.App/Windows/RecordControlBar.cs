using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HezbClips.Core.Audio;
using HezbClips.Core.Settings;

namespace HezbClips.App.Windows;

/// <summary>Small bar shown after picking a region: choose audio sources and quality, press Record, then Stop.
/// Excluded from screen capture (WDA_EXCLUDEFROMCAPTURE) so it never appears in the video.</summary>
public sealed class RecordControlBar : Window
{
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    readonly ToggleButton _sys = new() { Content = "Âm thanh máy", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) };
    readonly ToggleButton _mic = new() { Content = "Mic", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) };
    readonly ComboBox _quality = new() { Margin = new Thickness(0, 0, 6, 0), Width = 70 };
    readonly TextBlock _timer = new() { Text = "00:00", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0), FontFamily = new FontFamily("Consolas"), FontSize = 15 };
    readonly Button _go = new() { Content = "Quay", Padding = new Thickness(14, 3, 14, 3), Background = Brushes.IndianRed, Foreground = Brushes.White };
    readonly Button _cancel = new() { Content = "Hủy", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly System.Drawing.Rectangle _region;
    DateTime _startedAt; bool _recording;

    public event Action<AudioSources, ExportQuality>? RecordClicked;
    public event Action? StopClicked;
    public event Action? Cancelled;

    public RecordControlBar(System.Drawing.Rectangle regionPhysical, AppSettings s)
    {
        _region = regionPhysical;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = false;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 36));
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone;
        _quality.Items.Add("1080p"); _quality.Items.Add("2K"); _quality.Items.Add("4K");
        _quality.SelectedIndex = s.Quality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        foreach (var c in new UIElement[] { _sys, _mic, _quality, _timer, _go, _cancel }) row.Children.Add(c);
        Content = new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), Child = row };

        _go.Click += (_, _) => OnGo();
        _cancel.Click += (_, _) => Cancelled?.Invoke();
        _tick.Tick += (_, _) => _timer.Text = (DateTime.UtcNow - _startedAt).ToString(@"mm\:ss");
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WDA_EXCLUDEFROMCAPTURE);
        Loaded += (_, _) => PlaceNearRegion();
    }

    void OnGo()
    {
        if (_recording) { StopClicked?.Invoke(); return; }
        var q = _quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
        var a = (_sys.IsChecked == true ? AudioSources.System : 0) | (_mic.IsChecked == true ? AudioSources.Microphone : 0);
        _recording = true; _startedAt = DateTime.UtcNow; _tick.Start();
        _sys.IsEnabled = _mic.IsEnabled = _quality.IsEnabled = _cancel.IsEnabled = false;
        _go.Content = "Dừng";
        RecordClicked?.Invoke(a, q);
    }

    void PlaceNearRegion()
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)(ActualWidth * scale), h = (int)(ActualHeight * scale);
        var screen = System.Windows.Forms.Screen.FromRectangle(_region).Bounds;
        int x = Math.Clamp(_region.X + _region.Width / 2 - w / 2, screen.Left, screen.Right - w);
        int below = _region.Bottom + 12;
        int y = below + h <= screen.Bottom ? below : Math.Max(screen.Top, _region.Top - h - 12);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e) { _tick.Stop(); base.OnClosed(e); }
}
