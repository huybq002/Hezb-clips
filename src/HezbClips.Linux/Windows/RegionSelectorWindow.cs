using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HezbClips.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDRect = System.Drawing.Rectangle;
using Point = Avalonia.Point;
using Color = Avalonia.Media.Color;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Image = Avalonia.Controls.Image;

namespace HezbClips.Linux.Windows;

/// <summary>Full-screen overlay showing a frozen screenshot; the user drags a region on it.</summary>
public class RegionSelectorWindow : Window
{
    readonly SDRect _shown;                 // part of the screenshot displayed here (pixels)
    readonly TaskCompletionSource<SDRect?> _tcs = new();
    readonly Canvas _canvas = new();
    readonly Rectangle _sel = new() { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), IsVisible = false };
    readonly TextBlock _label = new() { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Padding = new Thickness(4, 2), IsVisible = false };
    Point _start;
    bool _drag;

    RegionSelectorWindow(Image<Rgba32> screenshot, SDRect shown)
    {
        _shown = shown;
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true; ShowInTaskbar = false;
        Cursor = new Cursor(StandardCursorType.Cross);

        using var crop = screenshot.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(shown.X, shown.Y, shown.Width, shown.Height)));
        using var ms = new MemoryStream();
        crop.Save(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        ms.Position = 0;

        var root = new Grid();
        root.Children.Add(new Image { Source = new Bitmap(ms), Stretch = Stretch.Fill });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsHitTestVisible = false });
        _canvas.Background = Brushes.Transparent;
        _canvas.Children.Add(_sel); _canvas.Children.Add(_hint());
        _canvas.Children.Add(_label);
        root.Children.Add(_canvas);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        _canvas.PointerPressed += OnDown;
        _canvas.PointerMoved += OnMove;
        _canvas.PointerReleased += OnUp;
        Opened += (_, _) => Activate();
    }

    static Control _hint() => new TextBlock
    {
        Text = "Kéo để chọn vùng · Esc để huỷ", Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Padding = new Thickness(6, 3),
        Margin = new Thickness(16),
    };

    SDRect ToImage(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return CropMath.SelectionToImage(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y),
            _canvas.Bounds.Width, _canvas.Bounds.Height, _shown);
    }

    void OnDown(object? s, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
        _start = e.GetPosition(_canvas); _drag = true;
        _sel.IsVisible = true; _label.IsVisible = true;
        e.Pointer.Capture(_canvas);
        Update(_start);
    }

    void OnMove(object? s, PointerEventArgs e) { if (_drag) Update(e.GetPosition(_canvas)); }

    void Update(Point p)
    {
        double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
        Canvas.SetLeft(_sel, x); Canvas.SetTop(_sel, y);
        _sel.Width = Math.Abs(p.X - _start.X); _sel.Height = Math.Abs(p.Y - _start.Y);
        var r = ToImage(_start, p);
        _label.Text = $"{r.Width} × {r.Height}";
        Canvas.SetLeft(_label, x); Canvas.SetTop(_label, Math.Max(0, y - 24));
    }

    void OnUp(object? s, PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null);
        var r = ToImage(_start, e.GetPosition(_canvas));
        if (r.Width < 8 || r.Height < 8) { _sel.IsVisible = false; _label.IsVisible = false; return; }
        Finish(r);
    }

    void Finish(SDRect? r)
    {
        if (_tcs.Task.IsCompleted) return;
        _tcs.SetResult(r);
        Close();
    }

    /// <summary>Returns the selection in screenshot pixel coordinates, or null when cancelled.</summary>
    public static Task<SDRect?> SelectAsync(Image<Rgba32> screenshot, SDRect shownRegion)
    {
        var w = new RegionSelectorWindow(screenshot, shownRegion);
        w.Closed += (_, _) => w._tcs.TrySetResult(null);
        w.Show();
        return w._tcs.Task;
    }
}
