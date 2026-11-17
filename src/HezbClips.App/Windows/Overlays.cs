using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HezbClips.App.Windows;

static class WinNative
{
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Hide the window from screenshots and recordings.</summary>
    public static void ExcludeFromCapture(Window w) => SetWindowDisplayAffinity(new WindowInteropHelper(w).EnsureHandle(), WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Mouse clicks pass through the window; it never takes focus or shows in Alt+Tab.</summary>
    public static void ClickThrough(Window w)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }
}

/// <summary>Small message near the bottom of the screen that fades by itself ("Đã copy vào clipboard").</summary>
public static class Toast
{
    public static void Show(string text)
    {
        var label = new TextBlock { Text = text, Foreground = Ui.Brush("Text"), FontSize = 14, FontWeight = FontWeights.SemiBold };
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
            Content = Ui.Card(label, 14), Focusable = false,
        };
        w.SourceInitialized += (_, _) => WinNative.ClickThrough(w);
        w.Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2; w.Top = wa.Bottom - w.ActualHeight - 24;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
            t.Tick += (_, _) => { t.Stop(); w.Close(); };
            t.Start();
        };
        w.Show();
    }
}

/// <summary>Big "3 … 2 … 1" before a delayed capture. Hidden from the capture itself.</summary>
public static class Countdown
{
    public static async Task RunAsync(int seconds)
    {
        var num = new TextBlock { FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock { Text = "Chuẩn bị màn hình để chụp…", Foreground = Ui.Brush("TextDim"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
        var col = new StackPanel(); col.Children.Add(num); col.Children.Add(sub);
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
            Content = Ui.Card(col, 22), WindowStartupLocation = WindowStartupLocation.Manual,
        };
        w.SourceInitialized += (_, _) => { WinNative.ClickThrough(w); WinNative.ExcludeFromCapture(w); };
        num.Text = seconds.ToString();
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2; w.Top = wa.Top + 60;
        for (int i = seconds; i >= 1; i--) { num.Text = i.ToString(); await Task.Delay(1000); }
        w.Close();
        await Task.Delay(200);
    }
}

/// <summary>While a recording region is chosen: grey the rest of every screen and outline the region.
/// Never appears in the recording (excluded from capture) and never blocks the mouse.</summary>
public sealed class RegionFocusOverlay
{
    readonly System.Collections.Generic.List<FocusWindow> _windows = new();

    public static RegionFocusOverlay Show(System.Drawing.Rectangle region)
    {
        var o = new RegionFocusOverlay();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var w = new FocusWindow(screen.Bounds, region);
            o._windows.Add(w); w.Show();
        }
        return o;
    }

    /// <summary>Outline colour: accent while ready, red while recording, orange while paused.</summary>
    public void SetState(bool recording, bool paused = false)
    {
        var c = paused ? Color.FromRgb(245, 165, 36) : recording ? Color.FromRgb(229, 72, 77) : Color.FromRgb(76, 141, 255);
        foreach (var w in _windows) w.SetColor(c);
    }

    public void Close() { foreach (var w in _windows) w.Close(); _windows.Clear(); }

    sealed class FocusWindow : Window
    {
        readonly System.Drawing.Rectangle _screen, _region;
        readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(150, 20, 22, 30)) };
        readonly System.Windows.Shapes.Rectangle _border = new() { StrokeThickness = 3, RadiusX = 2, RadiusY = 2, Stroke = new SolidColorBrush(Color.FromRgb(76, 141, 255)) };

        public FocusWindow(System.Drawing.Rectangle screen, System.Drawing.Rectangle region)
        {
            _screen = screen; _region = region;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.Manual; Focusable = false;
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(_dim); canvas.Children.Add(_border);
            Content = canvas;
            SourceInitialized += (_, _) => { WinNative.ClickThrough(this); WinNative.ExcludeFromCapture(this); Place(); };
            Loaded += (_, _) => { Place(); Layout(); };
        }

        void Place() => WinNative.SetWindowPos(new WindowInteropHelper(this).Handle, WinNative.HWND_TOPMOST, _screen.X, _screen.Y, _screen.Width, _screen.Height,
            WinNative.SWP_NOACTIVATE | WinNative.SWP_SHOWWINDOW);

        public void SetColor(Color c) => _border.Stroke = new SolidColorBrush(c);

        void Layout()
        {
            var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var full = new Rect(0, 0, _screen.Width / s, _screen.Height / s);
            var hole = new Rect((_region.X - _screen.X) / s, (_region.Y - _screen.Y) / s, _region.Width / s, _region.Height / s);
            var inter = Rect.Intersect(full, hole);
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(full));
            if (!inter.IsEmpty) g.Children.Add(new RectangleGeometry(inter));
            _dim.Data = g;
            if (inter.IsEmpty) { _border.Visibility = Visibility.Collapsed; return; }
            // frame sits just outside the region so the recorded pixels stay untouched
            Canvas.SetLeft(_border, hole.X - 3); Canvas.SetTop(_border, hole.Y - 3);
            _border.Width = hole.Width + 6; _border.Height = hole.Height + 6;
        }
    }
}
