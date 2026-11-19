using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HezbClips.Core.Audio;
using HezbClips.Core.Settings;

namespace HezbClips.App.Windows;

/// <summary>Everything the user picked on the record bar.</summary>
public record RecordChoices(AudioSources Audio, ExportQuality Quality, bool Gif, int GifFps, int GifWidth, bool Clicks, bool Keys, bool Webcam);

/// <summary>Bar shown after picking a region: choose audio, quality, GIF options and effects, press Record,
/// then Pause / Stop. Excluded from screen capture (WDA_EXCLUDEFROMCAPTURE) so it never appears in the video.</summary>
public sealed class RecordControlBar : Window
{
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    static readonly int[] GifFpsValues = { 10, 15, 20, 30 };
    static readonly int[] GifWidthValues = { 480, 720, 960, 0 };

    readonly ToggleButton _sys = new() { Content = Ui.Content("\uE767 Âm thanh máy"), Margin = new Thickness(0, 0, 6, 0) };
    readonly ToggleButton _mic = new() { Content = Ui.Content("\uE720 Mic"), Margin = new Thickness(0, 0, 6, 0) };
    readonly ToggleButton _gif = new() { Content = "GIF ≤60s", Margin = new Thickness(0, 0, 10, 0), ToolTip = "Quay thành ảnh GIF, tối đa 60 giây, không có âm thanh" };
    readonly ComboBox _quality = new() { Margin = new Thickness(0, 0, 10, 0), Width = 76 };
    readonly ComboBox _gifFps = new() { Margin = new Thickness(0, 0, 6, 0), Width = 74, Visibility = Visibility.Collapsed, ToolTip = "Số khung hình/giây của GIF" };
    readonly ComboBox _gifWidth = new() { Margin = new Thickness(0, 0, 10, 0), Width = 92, Visibility = Visibility.Collapsed, ToolTip = "Chiều rộng tối đa của GIF" };
    readonly ToggleButton _clicks = new() { Content = "Click", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Hiện vòng tròn khi bấm chuột trong video" };
    readonly ToggleButton _keys = new() { Content = Ui.Content("\uE765 Phím"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Hiện phím bấm trong video" };
    readonly ToggleButton _cam = new() { Content = Ui.Content("\uE8B8 Webcam"), ToolTip = "Hiện webcam ở góc vùng quay" };
    readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 9, Height = 9, Fill = Ui.Brush("Disabled"), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _timer = new() { Text = "00:00", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0), FontFamily = new FontFamily("Consolas"), FontSize = 15, Foreground = Ui.Brush("Text") };
    readonly Button _go = new() { Content = Ui.Content("\uE7C8 Quay"), Padding = new Thickness(16, 6, 16, 6), Style = Ui.Style("DangerButton") };
    readonly Button _pause = new() { Content = Ui.Content("\uE769 Tạm dừng"), Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button _cancel = new() { Content = "Hủy", Margin = new Thickness(6, 0, 0, 0) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Stopwatch _sw = new();
    readonly System.Drawing.Rectangle _region;
    bool _recording, _paused;

    public event Action<RecordChoices>? RecordClicked;
    public event Action? StopClicked;
    public event Action<bool>? PauseToggled;
    public event Action? Cancelled;

    public RecordControlBar(System.Drawing.Rectangle regionPhysical, AppSettings s)
    {
        _region = regionPhysical;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        // names for screen readers and UI automation (the visible content is icon + text)
        AutomationProperties.SetName(_sys, "Âm thanh máy"); AutomationProperties.SetName(_mic, "Mic"); AutomationProperties.SetName(_gif, "GIF");
        AutomationProperties.SetName(_clicks, "Hiện click"); AutomationProperties.SetName(_keys, "Hiện phím"); AutomationProperties.SetName(_cam, "Webcam");
        AutomationProperties.SetName(_go, "Quay"); AutomationProperties.SetName(_pause, "Tạm dừng"); AutomationProperties.SetName(_cancel, "Hủy");
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone;
        _clicks.IsChecked = s.ShowClicks; _keys.IsChecked = s.ShowKeys; _cam.IsChecked = s.Webcam;
        _quality.Items.Add("1080p"); _quality.Items.Add("2K"); _quality.Items.Add("4K");
        _quality.SelectedIndex = s.VideoQuality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };
        foreach (var f in GifFpsValues) _gifFps.Items.Add($"{f} fps");
        _gifFps.SelectedIndex = Math.Max(0, Array.IndexOf(GifFpsValues, s.GifFps));
        foreach (var w in GifWidthValues) _gifWidth.Items.Add(w == 0 ? "Gốc" : $"{w}px");
        _gifWidth.SelectedIndex = Math.Max(0, Array.IndexOf(GifWidthValues, s.GifWidth));

        var options = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new UIElement[] { _sys, _mic, _gif, _quality, _gifFps, _gifWidth, _clicks, _keys, _cam }) options.Children.Add(c);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in new UIElement[] { _dot, _timer, _go, _pause, _cancel }) actions.Children.Add(c);
        var col = new StackPanel(); col.Children.Add(options); col.Children.Add(actions);
        Content = Ui.Card(col, 10);

        _gif.Click += (_, _) =>
        {
            var g = _gif.IsChecked == true;
            ApplyGifState(g);
        };
        _go.Click += (_, _) => OnGo();
        _pause.Click += (_, _) => { SetPaused(!_paused); PauseToggled?.Invoke(_paused); };
        _cancel.Click += (_, _) => Cancelled?.Invoke();
        _tick.Tick += (_, _) =>
        {
            var t = _sw.Elapsed.ToString(@"mm\:ss");
            _timer.Text = _gif.IsChecked == true ? t + " / 01:00" : t;
        };
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WDA_EXCLUDEFROMCAPTURE);
        Loaded += (_, _) => PlaceNearRegion();
    }

    void ApplyGifState(bool g)
    {
        _sys.IsEnabled = _mic.IsEnabled = _quality.IsEnabled = !g;
        _gifFps.Visibility = _gifWidth.Visibility = g ? Visibility.Visible : Visibility.Collapsed;
        _quality.Visibility = g ? Visibility.Collapsed : Visibility.Visible;
        _timer.Text = g ? "00:00 / 01:00" : "00:00";
        Dispatcher.BeginInvoke(PlaceNearRegion, DispatcherPriority.Loaded);
    }

    /// <summary>Open with GIF mode already on (GIF hotkey).</summary>
    public void PresetGif() { _gif.IsChecked = true; ApplyGifState(true); }

    public void SetPaused(bool paused)
    {
        if (!_recording || _paused == paused) return;
        _paused = paused;
        if (paused) _sw.Stop(); else _sw.Start();
        _pause.Content = Ui.Content(paused ? "\uE768 Tiếp tục" : "\uE769 Tạm dừng");
        AutomationProperties.SetName(_pause, paused ? "Tiếp tục" : "Tạm dừng");
        _dot.Fill = Ui.Brush(paused ? "Disabled" : "Danger");
    }

    void OnGo()
    {
        if (_recording) { StopClicked?.Invoke(); return; }
        var q = _quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
        var a = (_sys.IsChecked == true ? AudioSources.System : 0) | (_mic.IsChecked == true ? AudioSources.Microphone : 0);
        var isGif = _gif.IsChecked == true;
        var choices = new RecordChoices(a, q, isGif, GifFpsValues[Math.Max(0, _gifFps.SelectedIndex)], GifWidthValues[Math.Max(0, _gifWidth.SelectedIndex)],
            _clicks.IsChecked == true, _keys.IsChecked == true, _cam.IsChecked == true);
        _recording = true; _sw.Restart(); _tick.Start();
        foreach (var c in new Control[] { _sys, _mic, _quality, _gif, _gifFps, _gifWidth, _clicks, _keys, _cam, _cancel }) c.IsEnabled = false;
        _go.Content = Ui.Content("\uE71A Dừng"); AutomationProperties.SetName(_go, "Dừng"); _dot.Fill = Ui.Brush("Danger"); _pause.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(PlaceNearRegion, DispatcherPriority.Loaded);
        RecordClicked?.Invoke(choices);
    }

    void PlaceNearRegion()
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)(ActualWidth * scale), h = (int)(ActualHeight * scale);
        var screen = System.Windows.Forms.Screen.FromRectangle(_region).Bounds;
        int x = Math.Clamp(_region.X + _region.Width / 2 - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        int below = _region.Bottom + 12;
        int y = below + h <= screen.Bottom ? below : Math.Max(screen.Top, _region.Top - h - 12);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e) { _tick.Stop(); base.OnClosed(e); }
}
