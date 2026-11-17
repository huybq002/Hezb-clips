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
    static readonly HashSet<IntPtr> Overlays = new();

    /// <param name="frozen">Screenshots taken just before selecting, keyed by screen bounds. When given, each overlay shows
    /// its screen's frozen picture (dimmed) so nothing moves while the user picks the region.</param>
    public static Task<System.Drawing.Rectangle?> SelectAsync(IReadOnlyDictionary<System.Drawing.Rectangle, System.Windows.Media.Imaging.BitmapSource>? frozen = null)
    {
        var tcs = new TaskCompletionSource<System.Drawing.Rectangle?>();
        var windows = new List<OverlayWindow>();
        Overlays.Clear();
        void Finish(System.Drawing.Rectangle? r)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(r);
            foreach (var w in windows) w.Close();
        }
        foreach (var screen in Forms.Screen.AllScreens)
        {
            System.Windows.Media.Imaging.BitmapSource? pic = null;
            frozen?.TryGetValue(screen.Bounds, out pic);
            var w = new OverlayWindow(screen.Bounds, Finish, Overlays, pic);
            windows.Add(w);
            w.Closed += (_, _) => Finish(null); // Alt+F4 must not leave the app stuck "busy"
            w.Show();
            Overlays.Add(new WindowInteropHelper(w).Handle);
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
        readonly Rectangle _sel = new() { Stroke = Ui.Brush("Accent"), StrokeThickness = 2, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), Visibility = Visibility.Collapsed };
        readonly TextBlock _label = new() { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 27, 28, 34)), FontSize = 12, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(8, 3, 8, 3), Visibility = Visibility.Collapsed };
        readonly Rectangle _hover = new() { Stroke = Ui.Brush("Accent"), StrokeThickness = 2, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.FromArgb(55, 76, 141, 255)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly HashSet<IntPtr> _ignore;
        System.Drawing.Rectangle? _hoverPhysical;
        Point _start; bool _dragging, _moved;

        public OverlayWindow(System.Drawing.Rectangle bounds, Action<System.Drawing.Rectangle?> finish, HashSet<IntPtr> ignore, System.Windows.Media.Imaging.BitmapSource? frozen = null)
        {
            _bounds = bounds; _finish = finish; _ignore = ignore;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            _canvas.Children.Add(_hover); _canvas.Children.Add(_sel); _canvas.Children.Add(_label);
            if (frozen is null) Content = _canvas;
            else
            {
                // frozen screen underneath, the usual dim on top of it
                Background = new ImageBrush(frozen) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
                var root = new Grid();
                root.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsHitTestVisible = false });
                root.Children.Add(_canvas);
                Content = root;
            }
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
            _start = e.GetPosition(_canvas); _dragging = true; _moved = false; CaptureMouse();
        }

        Rect Current(Point p) => new(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), Math.Abs(p.X - _start.X), Math.Abs(p.Y - _start.Y));

        void UpdateHover(Point dip)
        {
            var phys = DpiMath.ToPhysical(dip.X, dip.Y, 0, 0, Scale);
            var pt = new System.Drawing.Point(_bounds.X + phys.X, _bounds.Y + phys.Y);
            _hoverPhysical = WindowFinder.FromPoint(pt, _ignore);
            if (_hoverPhysical is not { } r) { _hover.Visibility = Visibility.Collapsed; return; }
            var vis = System.Drawing.Rectangle.Intersect(r, _bounds);
            if (vis.IsEmpty) { _hover.Visibility = Visibility.Collapsed; _hoverPhysical = null; return; }
            Canvas.SetLeft(_hover, (vis.X - _bounds.X) / Scale); Canvas.SetTop(_hover, (vis.Y - _bounds.Y) / Scale);
            _hover.Width = vis.Width / Scale; _hover.Height = vis.Height / Scale;
            _hover.Visibility = Visibility.Visible;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!_dragging) { UpdateHover(e.GetPosition(_canvas)); return; }
            var cur = e.GetPosition(_canvas);
            if (!_moved && (Math.Abs(cur.X - _start.X) > 4 || Math.Abs(cur.Y - _start.Y) > 4))
            {
                _moved = true; _hover.Visibility = Visibility.Collapsed;
                _sel.Visibility = _label.Visibility = Visibility.Visible;
            }
            if (!_moved) return;
            var r = Current(cur);
            Canvas.SetLeft(_sel, r.X); Canvas.SetTop(_sel, r.Y); _sel.Width = r.Width; _sel.Height = r.Height;
            Canvas.SetLeft(_label, r.X); Canvas.SetTop(_label, Math.Max(0, r.Y - 22));
            _label.Text = $"{Math.Round(r.Width * Scale)} x {Math.Round(r.Height * Scale)}";
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false; ReleaseMouseCapture();
            if (!_moved)
            {
                // plain click: take the auto-detected window/frame under the cursor
                if (_hoverPhysical is { } auto) _finish(auto);
                return;
            }
            var r = Current(e.GetPosition(_canvas));
            var phys = DpiMath.ToPhysical(r.X, r.Y, r.Width, r.Height, Scale);
            if (phys.Width < 16 || phys.Height < 16) { _sel.Visibility = _label.Visibility = Visibility.Collapsed; return; }
            _finish(new System.Drawing.Rectangle(_bounds.X + phys.X, _bounds.Y + phys.Y, phys.Width, phys.Height));
        }
    }
}
