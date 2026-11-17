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

    readonly ToggleButton _sys = new() { Content = "🔊 Âm thanh máy", Margin = new Thickness(0, 0, 6, 0) };
    readonly ToggleButton _mic = new() { Content = "🎤 Mic", Margin = new Thickness(0, 0, 8, 0) };
    readonly ToggleButton _gif = new() { Content = "GIF ≤60s", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Quay thành ảnh GIF, tối đa 60 giây, không có âm thanh" };
    readonly ComboBox _quality = new() { Margin = new Thickness(0, 0, 10, 0), Width = 76 };
    readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 9, Height = 9, Fill = Ui.Brush("Disabled"), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _timer = new() { Text = "00:00", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0), FontFamily = new FontFamily("Consolas"), FontSize = 15, Foreground = Ui.Brush("Text") };
    readonly Button _go = new() { Content = "⏺ Quay", Padding = new Thickness(16, 6, 16, 6), Style = Ui.Style("DangerButton") };
    readonly Button _cancel = new() { Content = "Hủy", Margin = new Thickness(6, 0, 0, 0) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly System.Drawing.Rectangle _region;
    DateTime _startedAt; bool _recording;

    public event Action<AudioSources, ExportQuality, bool>? RecordClicked;
    public event Action? StopClicked;
    public event Action? Cancelled;

    public RecordControlBar(System.Drawing.Rectangle regionPhysical, AppSettings s)
    {
        _region = regionPhysical;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone;
        _quality.Items.Add("1080p"); _quality.Items.Add("2K"); _quality.Items.Add("4K");
        _quality.SelectedIndex = s.VideoQuality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new UIElement[] { _sys, _mic, _gif, _quality, _dot, _timer, _go, _cancel }) row.Children.Add(c);
        Content = Ui.Card(row, 10);

        _gif.Click += (_, _) =>
        {
            var g = _gif.IsChecked == true;
            _sys.IsEnabled = _mic.IsEnabled = _quality.IsEnabled = !g;
            _timer.Text = g ? "00:00 / 01:00" : "00:00";
        };
        _go.Click += (_, _) => OnGo();
        _cancel.Click += (_, _) => Cancelled?.Invoke();
        _tick.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - _startedAt).ToString(@"mm\:ss");
            _timer.Text = _gif.IsChecked == true ? t + " / 01:00" : t;
        };
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WDA_EXCLUDEFROMCAPTURE);
        Loaded += (_, _) => PlaceNearRegion();
    }

    /// <summary>Open with GIF mode already on (GIF hotkey).</summary>
    public void PresetGif()
    {
        _gif.IsChecked = true;
        _sys.IsEnabled = _mic.IsEnabled = _quality.IsEnabled = false;
        _timer.Text = "00:00 / 01:00";
    }

    void OnGo()
    {
        if (_recording) { StopClicked?.Invoke(); return; }
        var q = _quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
        var a = (_sys.IsChecked == true ? AudioSources.System : 0) | (_mic.IsChecked == true ? AudioSources.Microphone : 0);
        _recording = true; _startedAt = DateTime.UtcNow; _tick.Start();
        var isGif = _gif.IsChecked == true;
        _sys.IsEnabled = _mic.IsEnabled = _quality.IsEnabled = _gif.IsEnabled = _cancel.IsEnabled = false;
        _go.Content = "⏹ Dừng"; _dot.Fill = Ui.Brush("Danger");
        RecordClicked?.Invoke(a, q, isGif);
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
