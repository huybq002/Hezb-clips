using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using HezbClips.Core.Imaging;
using Forms = System.Windows.Forms;

namespace HezbClips.App.Windows;

/// <summary>Full-screen dim overlay (one window per monitor). Drag to pick a region; Esc cancels.
/// Returns the region in physical pixels (virtual-screen coordinates), converted per monitor DPI.</summary>
public static class RegionSelector
{
    public static Task<System.Drawing.Rectangle?> SelectAsync()
    {
        var tcs = new TaskCompletionSource<System.Drawing.Rectangle?>();
        var windows = new List<OverlayWindow>();
        void Finish(System.Drawing.Rectangle? r)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(r);
            foreach (var w in windows) w.Close();
        }
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var w = new OverlayWindow(screen.Bounds, Finish);
            windows.Add(w);
            w.Show();
        }
        windows[0].Activate();
        return tcs.Task;
    }

    sealed class OverlayWindow : Window
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HWND_TOPMOST = new(-1);
        const uint SWP_SHOWWINDOW = 0x0040;

        readonly System.Drawing.Rectangle _bounds;
        readonly Action<System.Drawing.Rectangle?> _finish;
        readonly Canvas _canvas = new();
        readonly Rectangle _sel = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Visibility = Visibility.Collapsed };
        readonly TextBlock _label = new() { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), Padding = new Thickness(4, 1, 4, 1), Visibility = Visibility.Collapsed };
        Point _start; bool _dragging;

        public OverlayWindow(System.Drawing.Rectangle bounds, Action<System.Drawing.Rectangle?> finish)
        {
            _bounds = bounds; _finish = finish;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            _canvas.Children.Add(_sel); _canvas.Children.Add(_label);
            Content = _canvas;
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => Place();
            MouseLeftButtonDown += OnDown; MouseMove += OnMove; MouseLeftButtonUp += OnUp;
            KeyDown += (_, e) => { if (e.Key == Key.Escape) _finish(null); };
        }

        void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowPos(h, HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SWP_SHOWWINDOW);
        }

        double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        void OnDown(object s, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(_canvas); _dragging = true; CaptureMouse();
            _sel.Visibility = _label.Visibility = Visibility.Visible;
        }

        Rect Current(Point p) => new(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), Math.Abs(p.X - _start.X), Math.Abs(p.Y - _start.Y));

        void OnMove(object s, MouseEventArgs e)
        {
            if (!_dragging) return;
            var r = Current(e.GetPosition(_canvas));
            Canvas.SetLeft(_sel, r.X); Canvas.SetTop(_sel, r.Y); _sel.Width = r.Width; _sel.Height = r.Height;
            Canvas.SetLeft(_label, r.X); Canvas.SetTop(_label, Math.Max(0, r.Y - 22));
            _label.Text = $"{Math.Round(r.Width * Scale)} x {Math.Round(r.Height * Scale)}";
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false; ReleaseMouseCapture();
            var r = Current(e.GetPosition(_canvas));
            var phys = DpiMath.ToPhysical(r.X, r.Y, r.Width, r.Height, Scale);
            if (phys.Width < 16 || phys.Height < 16) { _sel.Visibility = _label.Visibility = Visibility.Collapsed; return; }
            _finish(new System.Drawing.Rectangle(_bounds.X + phys.X, _bounds.Y + phys.Y, phys.Width, phys.Height));
        }
    }
}
