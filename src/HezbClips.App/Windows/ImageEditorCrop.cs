using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HezbClips.App.Windows;

/// <summary>Crop (a visible, adjustable selection that is only applied on Enter / the button) and the select-move-delete tool.</summary>
public sealed partial class ImageEditorWindow
{
    // ===================================================================== crop

    enum CropDrag { None, New, Move, NW, N, NE, E, SE, S, SW, W }

    Rect? _crop;
    CropDrag _cropDrag;
    Point _cropFrom; Rect _cropStart;
    readonly System.Windows.Shapes.Path _cropDim = new() { Fill = new SolidColorBrush(Color.FromArgb(165, 8, 10, 18)), Visibility = Visibility.Collapsed };
    readonly Rectangle _cropBorder = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed, Fill = Brushes.Transparent };
    readonly Line[] _thirds = Enumerable.Range(0, 4).Select(_ => new Line { Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), Visibility = Visibility.Collapsed }).ToArray();
    readonly Rectangle[] _handles = Enumerable.Range(0, 8).Select(_ => new Rectangle { Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(76, 141, 255)), Visibility = Visibility.Collapsed }).ToArray();
    readonly Border _cropBar = new()
    {
        Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 72),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromArgb(240, 22, 24, 32)),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.55 },
    };
    readonly TextBlock _cropSize = new() { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) };

    // handle order matches CropDrag.NW..W
    static readonly CropDrag[] HandleKinds = { CropDrag.NW, CropDrag.N, CropDrag.NE, CropDrag.E, CropDrag.SE, CropDrag.S, CropDrag.SW, CropDrag.W };

    void BuildCropUi()
    {
        Panel.SetZIndex(_cropDim, 10); Panel.SetZIndex(_cropBorder, 11);
        _ui.Children.Add(_cropDim); _ui.Children.Add(_cropBorder);
        foreach (var l in _thirds) { Panel.SetZIndex(l, 11); _ui.Children.Add(l); }
        foreach (var h in _handles) { Panel.SetZIndex(h, 12); _ui.Children.Add(h); }
    }

    UIElement BuildCropBar()
    {
        var ok = new Button { Content = Ui.Content(" Cắt (Enter)"), Style = Ui.Style("PrimaryButton"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Ui.Content(" Hủy (Esc)"), Padding = new Thickness(14, 7, 14, 7) };
        ok.Click += (_, _) => ApplyCrop();
        cancel.Click += (_, _) => { CancelCrop(); Hud("", "Đã hủy vùng cắt", ok: false); };
        _cropBar.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _cropSize, ok, cancel } };
        return _cropBar;
    }

    static Point Clamp(Point p, double w, double h) => new(Math.Clamp(p.X, 0, w), Math.Clamp(p.Y, 0, h));

    Rect HandleRect(int i, Rect r)
    {
        var s = 11 / Zoom;
        var x = i switch { 0 or 6 or 7 => r.Left, 1 or 5 => r.Left + r.Width / 2, _ => r.Right };
        var y = i switch { 0 or 1 or 2 => r.Top, 3 or 7 => r.Top + r.Height / 2, _ => r.Bottom };
        return new Rect(x - s / 2, y - s / 2, s, s);
    }

    CropDrag HitCrop(Point p, Rect r)
    {
        for (int i = 0; i < 8; i++) { var hr = HandleRect(i, r); hr.Inflate(5 / Zoom, 5 / Zoom); if (hr.Contains(p)) return HandleKinds[i]; }
        return r.Contains(p) ? CropDrag.Move : CropDrag.None;
    }

    static Cursor CursorFor(CropDrag d) => d switch
    {
        CropDrag.NW or CropDrag.SE => Cursors.SizeNWSE, CropDrag.NE or CropDrag.SW => Cursors.SizeNESW,
        CropDrag.N or CropDrag.S => Cursors.SizeNS, CropDrag.E or CropDrag.W => Cursors.SizeWE,
        CropDrag.Move => Cursors.SizeAll, _ => Cursors.Cross,
    };

    void CropDown(MouseButtonEventArgs e)
    {
        var p = Clamp(P(e), _canvas.Width, _canvas.Height);
        _stage.CaptureMouse();
        if (_crop is { } r)
        {
            var hit = HitCrop(p, r);
            if (hit != CropDrag.None) { _cropDrag = hit; _cropFrom = p; _cropStart = r; e.Handled = true; return; }
        }
        _cropDrag = CropDrag.New; _cropFrom = p; _crop = new Rect(p, new Size(0, 0));
        _cropBar.Visibility = Visibility.Collapsed;
        DrawCrop();
        e.Handled = true;
    }

    void CropMove(MouseEventArgs e)
    {
        var p = Clamp(P(e), _canvas.Width, _canvas.Height);
        if (_cropDrag == CropDrag.None)
        {
            _stage.Cursor = _crop is { } rr ? CursorFor(HitCrop(p, rr)) : Cursors.Cross;
            return;
        }
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var W = _canvas.Width; var H = _canvas.Height;
        switch (_cropDrag)
        {
            case CropDrag.New:
            {
                var w = p.X - _cropFrom.X; var h = p.Y - _cropFrom.Y;
                if (shift) { var m = Math.Max(Math.Abs(w), Math.Abs(h)); w = Math.Sign(w) * m; h = Math.Sign(h) * m; }
                var q = Clamp(new Point(_cropFrom.X + w, _cropFrom.Y + h), W, H);
                _crop = new Rect(Math.Min(_cropFrom.X, q.X), Math.Min(_cropFrom.Y, q.Y), Math.Abs(q.X - _cropFrom.X), Math.Abs(q.Y - _cropFrom.Y));
                break;
            }
            case CropDrag.Move:
            {
                var d = p - _cropFrom;
                var x = Math.Clamp(_cropStart.X + d.X, 0, W - _cropStart.Width); var y = Math.Clamp(_cropStart.Y + d.Y, 0, H - _cropStart.Height);
                _crop = new Rect(x, y, _cropStart.Width, _cropStart.Height);
                break;
            }
            default:
            {
                double l = _cropStart.Left, t = _cropStart.Top, r = _cropStart.Right, b = _cropStart.Bottom;
                if (_cropDrag is CropDrag.NW or CropDrag.W or CropDrag.SW) l = p.X;
                if (_cropDrag is CropDrag.NE or CropDrag.E or CropDrag.SE) r = p.X;
                if (_cropDrag is CropDrag.NW or CropDrag.N or CropDrag.NE) t = p.Y;
                if (_cropDrag is CropDrag.SW or CropDrag.S or CropDrag.SE) b = p.Y;
                _crop = new Rect(Math.Min(l, r), Math.Min(t, b), Math.Abs(r - l), Math.Abs(b - t));
                break;
            }
        }
        DrawCrop();
    }

    void CropUp(MouseButtonEventArgs e)
    {
        if (_cropDrag == CropDrag.None) return;
        _stage.ReleaseMouseCapture();
        _cropDrag = CropDrag.None;
        if (_crop is { } r && (r.Width < 8 || r.Height < 8)) { CancelCrop(); return; }
        DrawCrop();
    }

    /// <summary>Draw the crop selection: everything outside is dimmed, the kept area stays bright with a frame, thirds and handles.</summary>
    void DrawCrop()
    {
        if (_crop is not { } r || r.Width < 1 || r.Height < 1)
        {
            _cropDim.Visibility = _cropBorder.Visibility = Visibility.Collapsed;
            foreach (var l in _thirds) l.Visibility = Visibility.Collapsed;
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            _cropBar.Visibility = Visibility.Collapsed;
            return;
        }
        var z = Zoom;
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(new Rect(0, 0, _canvas.Width, _canvas.Height)));
        g.Children.Add(new RectangleGeometry(r));
        _cropDim.Data = g; _cropDim.Visibility = Visibility.Visible;

        _cropBorder.StrokeThickness = 2 / z; Canvas.SetLeft(_cropBorder, r.X); Canvas.SetTop(_cropBorder, r.Y);
        _cropBorder.Width = r.Width; _cropBorder.Height = r.Height; _cropBorder.Visibility = Visibility.Visible;

        for (int i = 0; i < 4; i++)
        {
            var l = _thirds[i]; l.StrokeThickness = 1 / z; l.Visibility = Visibility.Visible;
            if (i < 2) { var x = r.X + r.Width * (i + 1) / 3; l.X1 = l.X2 = x; l.Y1 = r.Top; l.Y2 = r.Bottom; }
            else { var y = r.Y + r.Height * (i - 1) / 3; l.Y1 = l.Y2 = y; l.X1 = r.Left; l.X2 = r.Right; }
        }
        for (int i = 0; i < 8; i++)
        {
            var hr = HandleRect(i, r); var h = _handles[i];
            Canvas.SetLeft(h, hr.X); Canvas.SetTop(h, hr.Y); h.Width = hr.Width; h.Height = hr.Height; h.StrokeThickness = 1.5 / z; h.Visibility = Visibility.Visible;
        }
        ShowDragLabel(r);
        _cropSize.Text = $"{Math.Round(r.Width)} × {Math.Round(r.Height)} px";
        _cropBar.Visibility = _cropDrag == CropDrag.None ? Visibility.Visible : Visibility.Collapsed;
    }

    void CancelCrop()
    {
        _crop = null; _cropDrag = CropDrag.None;
        HideDragLabel();
        DrawCrop();
    }

    void ApplyCrop()
    {
        if (_crop is not { } r) return;
        var x = (int)Math.Clamp(Math.Round(r.X), 0, _bmp.PixelWidth - 1);
        var y = (int)Math.Clamp(Math.Round(r.Y), 0, _bmp.PixelHeight - 1);
        var w = (int)Math.Min(Math.Round(r.Width), _bmp.PixelWidth - x);
        var h = (int)Math.Min(Math.Round(r.Height), _bmp.PixelHeight - y);
        if (w < 8 || h < 8) { Hud("", "Vùng cắt quá nhỏ", ok: false); return; }
        var flat = Render(); // annotations inside the crop are kept
        var old = _bmp; var items = _items.ToList();
        var cropped = new CroppedBitmap(flat, new Int32Rect(x, y, w, h)); cropped.Freeze();
        Run(new BaseCmd(this, old, cropped, items, "cắt ảnh"));
        Done(null, "", $"Đã cắt còn {w} × {h} px (Ctrl+Z để hoàn tác)");
    }

    // ===================================================================== select / move / delete

    Rect Bounds(UIElement el)
    {
        Rect local = el is Shape s ? s.RenderedGeometry.Bounds : new Rect(0, 0, ((FrameworkElement)el).ActualWidth, ((FrameworkElement)el).ActualHeight);
        if (el is Shape sh) local.Inflate(sh.StrokeThickness / 2, sh.StrokeThickness / 2);
        try { return el.TransformToVisual(_canvas).TransformBounds(local); }
        catch (InvalidOperationException) { return local; }
    }

    UIElement? HitItem(Point p)
    {
        var tol = 8 / Zoom + 2;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it is Polyline or Line or System.Windows.Shapes.Path)
            {
                try
                {
                    var lp = _canvas.TransformToDescendant(it).Transform(p);
                    var hit = false;
                    VisualTreeHelper.HitTest(it, null, _ => { hit = true; return HitTestResultBehavior.Stop; }, new GeometryHitTestParameters(new EllipseGeometry(lp, tol, tol)));
                    if (hit) return it;
                }
                catch (InvalidOperationException) { /* not in the tree */ }
            }
            else { var b = Bounds(it); b.Inflate(tol / 2, tol / 2); if (b.Contains(p)) return it; }
        }
        return null;
    }

    void SelectDown(MouseButtonEventArgs e)
    {
        var hit = HitItem(_start);
        _selected = hit;
        if (hit is not null)
        {
            _moving = true; _moveFrom = _start; _moved = new Vector(0, 0); _stage.CaptureMouse(); _stage.Cursor = Cursors.SizeAll;
        }
        RefreshUi();
        e.Handled = true;
    }

    void SelectMove(MouseEventArgs e)
    {
        var p = P(e);
        if (_moving && _selected is not null)
        {
            var d = (p - _moveFrom) - _moved;
            if (d.X != 0 || d.Y != 0) { Shift(_selected, d); _moved += d; }
            return;
        }
        _stage.Cursor = HitItem(p) is not null ? Cursors.SizeAll : Cursors.Arrow;
    }

    void SelectUp(MouseButtonEventArgs e)
    {
        if (!_moving) return;
        _moving = false; _stage.ReleaseMouseCapture();
        if (_selected is not null && (_moved.X != 0 || _moved.Y != 0))
        {
            // the shape already follows the mouse; record it so Ctrl+Z puts it back
            _undo.Push(new MoveCmd(this, _selected, _moved)); _redo.Clear();
        }
        _moved = new Vector(0, 0);
        RefreshUi();
    }

    void DeleteSelected()
    {
        if (_selected is null) return;
        var el = _selected; var idx = _items.IndexOf(el);
        _selected = null;
        Run(new DeleteCmd(this, el, idx));
        Hud("", "Đã xóa chú thích (Ctrl+Z để lấy lại)");
    }

    // ===================================================================== shared overlay refresh

    /// <summary>Redraw everything that is not part of the picture: selection frame, crop, button states, title mark.</summary>
    void RefreshUi()
    {
        if (_selected is not null && _items.Contains(_selected))
        {
            var b = Bounds(_selected); var pad = 4 / Zoom; b.Inflate(pad, pad);
            _selBox.StrokeThickness = 1.5 / Zoom; _selBox.StrokeDashArray = new DoubleCollection { 4, 3 };
            Canvas.SetLeft(_selBox, b.X); Canvas.SetTop(_selBox, b.Y); _selBox.Width = b.Width; _selBox.Height = b.Height;
            _selBox.Visibility = Visibility.Visible;
        }
        else _selBox.Visibility = Visibility.Collapsed;
        DrawCrop();
        if (_actions.TryGetValue("undo", out var u)) u.IsEnabled = _undo.Count > 0;
        if (_actions.TryGetValue("redo", out var r)) r.IsEnabled = _redo.Count > 0;
        if (_actions.TryGetValue("clear", out var c)) c.IsEnabled = _items.Count > 0;
        Title = "Hezb Clips — Chỉnh sửa · " + System.IO.Path.GetFileName(_path) + (Dirty ? "  ●" : "");
    }
}
