using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace HezbClips.App.Windows;

/// <summary>Annotate a captured image: pen, highlighter, line, arrow, rectangle, ellipse, text, numbered badge, mosaic.
/// Every tool and colour has a keyboard shortcut. Ctrl+S saves "name_edit.png" next to the original, Ctrl+C copies.</summary>
public sealed partial class ImageEditorWindow : Window
{
    enum Tool { Pen, Highlighter, Line, Arrow, Rect, Ellipse, Text, Number, Mosaic, Crop }

    static readonly (string Name, string Hex)[] Palette =
    {
        ("Đỏ", "#E5484D"), ("Cam", "#F5A524"), ("Vàng", "#FFD60A"), ("Xanh lá", "#2EA043"), ("Cyan", "#22D3EE"),
        ("Xanh dương", "#4C8DFF"), ("Tím", "#A855F7"), ("Trắng", "#FFFFFF"), ("Đen", "#000000"),
    };

    readonly string _path;
    BitmapSource _bmp;
    BitmapSource _pixelated;
    readonly Image _baseImage = new() { IsHitTestVisible = false };
    readonly Canvas _canvas = new();
    readonly Viewbox _view = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _status = new() { Foreground = Ui.Brush("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    readonly List<Button> _swatches = new();
    readonly Rectangle _currentColorChip = new() { Width = 22, Height = 22, RadiusX = 11, RadiusY = 11, Stroke = Brushes.White, StrokeThickness = 1.5 };
    readonly Slider _size = new() { Minimum = 1, Maximum = 20, Value = 4, Width = 110, VerticalAlignment = VerticalAlignment.Center, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly List<UIElement> _items = new();
    readonly Stack<UIElement> _redo = new();
    Tool _tool = Tool.Rect;
    Color _color = (Color)ColorConverter.ConvertFromString("#E5484D");
    Point _start; UIElement? _drawing; Polyline? _poly; TextBox? _editor;
    bool _dirty;

    readonly Dictionary<string, string> _keys;
    readonly Dictionary<Key, Tool> _keyToTool = new();

    public ImageEditorWindow(string path, IDictionary<string, string>? toolKeys = null)
    {
        _path = path;
        _keys = HezbClips.Core.Settings.EditorShortcuts.Resolve(toolKeys);
        foreach (var (name, letter) in _keys)
            if (Enum.TryParse<Tool>(name, out var t) && Enum.TryParse<Key>(letter, out var k)) _keyToTool[k] = t;
        _bmp = LoadBitmap(path);
        _pixelated = Pixelate(_bmp);
        Title = "Hezb Clips — Chỉnh sửa · " + System.IO.Path.GetFileName(path);
        Width = 1100; Height = 760; MinWidth = 720; MinHeight = 460;
        Icon = Ui.Logo("logo-light.png", 64); Background = Ui.Brush("Bg"); WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _canvas.Width = _bmp.PixelWidth; _canvas.Height = _bmp.PixelHeight;
        _canvas.Background = Brushes.Transparent;
        _baseImage.Source = _bmp; _baseImage.Width = _bmp.PixelWidth; _baseImage.Height = _bmp.PixelHeight;
        _canvas.Children.Add(_baseImage);
        _canvas.Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.HighQuality);
        _canvas.MouseLeftButtonDown += OnDown; _canvas.MouseMove += OnMove; _canvas.MouseLeftButtonUp += OnUp;
        _view.Child = new Border { Child = _canvas, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, Opacity = 0.5, ShadowDepth = 0 } };
        _view.Margin = new Thickness(16);

        var root = new DockPanel();
        var top = BuildToolbar();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new Border { Padding = new Thickness(16, 8, 16, 10), Child = _status, BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(_view);
        Content = root;

        SelectTool(Tool.Rect); SelectColor(Palette[0].Hex);
        SetHint();
        PreviewKeyDown += OnKey;
        Closing += OnClosing;
    }

    // ---------- UI ----------

    UIElement BuildToolbar()
    {
        var wrap = new WrapPanel { Margin = new Thickness(12, 10, 12, 8) };
        void Sep() => wrap.Children.Add(new Border { Width = 1, Background = Ui.Brush("Border"), Margin = new Thickness(8, 4, 8, 4) });
        void ToolBtn(Tool t, string icon, string name, string _)
        {
            var key = _keys[t.ToString()];
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = $"{icon} {name}" });
            label.Children.Add(new Border
            {
                Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
                Child = new TextBlock { Text = key, FontSize = 10, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas") },
            });
            var tip = new StackPanel { Orientation = Orientation.Horizontal };
            tip.Children.Add(new TextBlock { Text = name + "  " });
            tip.Children.Add(new TextBlock { Text = key, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas") });
            var b = new ToggleButton { Content = label, ToolTip = new ToolTip { Content = tip }, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 5, 10, 5) };
            ToolTipService.SetInitialShowDelay(b, 150);
            b.Click += (_, _) => SelectTool(t);
            _toolButtons[t] = b; wrap.Children.Add(b);
        }
        ToolBtn(Tool.Pen, "✏", "Bút", "P");
        ToolBtn(Tool.Highlighter, "🖍", "Tô sáng", "H");
        ToolBtn(Tool.Line, "╱", "Đường thẳng", "L");
        ToolBtn(Tool.Arrow, "➜", "Mũi tên", "A");
        ToolBtn(Tool.Rect, "▭", "Chữ nhật", "R");
        ToolBtn(Tool.Ellipse, "◯", "Elip", "O");
        ToolBtn(Tool.Text, "T", "Chữ", "T");
        ToolBtn(Tool.Number, "①", "Số", "N");
        ToolBtn(Tool.Mosaic, "▦", "Làm mờ", "M");
        ToolBtn(Tool.Crop, "✂", "Cắt ảnh", "X");
        Sep();

        wrap.Children.Add(_currentColorChip);
        _currentColorChip.Margin = new Thickness(0, 0, 8, 4);
        for (int i = 0; i < Palette.Length; i++)
        {
            var hex = Palette[i].Hex;
            var sw = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0), ToolTip = $"{Palette[i].Name} ({i + 1})",
                Template = SwatchTemplate(), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), Tag = hex,
            };
            sw.Click += (_, _) => SelectColor(hex);
            _swatches.Add(sw); wrap.Children.Add(sw);
        }
        var custom = new Button { Content = "🎨", ToolTip = "Màu tùy chọn (C)", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 3, 8, 3) };
        custom.Click += (_, _) => PickCustomColor();
        wrap.Children.Add(custom);
        Sep();

        var sizeLabel = new TextBlock { Text = "Độ dày", Foreground = Ui.Brush("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) };
        wrap.Children.Add(sizeLabel);
        _size.Margin = new Thickness(0, 0, 0, 4); _size.ToolTip = "[  ]  giảm / tăng";
        _size.ValueChanged += (_, _) => SetHint();
        wrap.Children.Add(_size);
        Sep();

        Button Act(string text, string tip, Action a, bool primary = false)
        {
            var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 5, 10, 5) };
            if (primary) b.Style = Ui.Style("PrimaryButton");
            b.Click += (_, _) => a(); wrap.Children.Add(b); return b;
        }
        Act("↶", "Hoàn tác (Ctrl+Z)", Undo);
        Act("↷", "Làm lại (Ctrl+Y)", Redo);
        Act("🗑 Xóa hết", "Xóa mọi chú thích (Ctrl+Shift+Z)", ClearAll);
        Act("⟲", "Xoay trái (Ctrl+L)", () => Rotate(false));
        Act("⟳", "Xoay phải (Ctrl+R)", () => Rotate(true));
        Act("↺ Gốc", "Khôi phục ảnh gốc (bỏ cắt, xoay và chú thích)", RestoreOriginal);
        Sep();
        Act("🔤 Copy chữ", "Nhận dạng chữ trong ảnh (OCR) và copy vào clipboard", () => _ = CopyTextAsync());
        Act("🛡 Che nhạy cảm", "Tự tìm email, số điện thoại, số thẻ trong ảnh và làm mờ", () => _ = RedactAsync());
        Act("📌 Ghim", "Ghim ảnh này lên màn hình", () => new PinWindow(Render()).Show());
        Sep();
        Act("📋 Copy", "Copy ảnh (Ctrl+C)", CopyToClipboard);
        Act("💾 Lưu", "Lưu thành file mới (Ctrl+S)", () => Save(), primary: true);
        return new Border { Child = wrap, BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    void SetHint() =>
        _status.Text = $"Công cụ: {ToolName(_tool)} · độ dày {(int)_size.Value}   |   " +
            string.Join(" · ", _keys.Select(kv => $"{kv.Value} {ToolName(Enum.Parse<Tool>(kv.Key)).ToLowerInvariant()}")) +
            "   |   1-9 màu · C màu khác · [ ] độ dày   |   Ctrl+Z/Y hoàn tác · Ctrl+S lưu · Ctrl+C copy · Shift giữ tỉ lệ · Esc đóng";

    static string ToolName(Tool t) => t switch
    {
        Tool.Pen => "Bút", Tool.Highlighter => "Tô sáng", Tool.Line => "Đường thẳng", Tool.Arrow => "Mũi tên", Tool.Rect => "Chữ nhật",
        Tool.Ellipse => "Elip", Tool.Text => "Chữ", Tool.Number => "Số", Tool.Mosaic => "Làm mờ", _ => "Cắt ảnh",
    };

    void SelectTool(Tool t)
    {
        CommitText();
        _tool = t;
        foreach (var (k, b) in _toolButtons) b.IsChecked = k == t;
        _canvas.Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        SetHint();
    }

    void SelectColor(string hex) => SelectColor((Color)ColorConverter.ConvertFromString(hex));

    void SelectColor(Color c)
    {
        _color = c;
        _currentColorChip.Fill = new SolidColorBrush(c);
        if (_editor is not null) _editor.Foreground = new SolidColorBrush(c);
    }

    void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B) };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SelectColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    // ---------- keyboard ----------

    void OnKey(object s, KeyEventArgs e)
    {
        if (_editor is not null && _editor.IsKeyboardFocused)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            return;
        }
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: ClearAll(); e.Handled = true; break;
                case Key.Z: Undo(); e.Handled = true; break;
                case Key.Y: Redo(); e.Handled = true; break;
                case Key.S: Save(); e.Handled = true; break;
                case Key.C: CopyToClipboard(); e.Handled = true; break;
                case Key.L: Rotate(false); e.Handled = true; break;
                case Key.R: Rotate(true); e.Handled = true; break;
            }
            return;
        }
        if (_keyToTool.TryGetValue(e.Key, out var tool)) { SelectTool(tool); e.Handled = true; return; }
        if (e.Key >= Key.D1 && e.Key <= Key.D9) { SelectColor(Palette[e.Key - Key.D1].Hex); e.Handled = true; return; }
        if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) { SelectColor(Palette[e.Key - Key.NumPad1].Hex); e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.C: PickCustomColor(); e.Handled = true; break;
            case Key.OemOpenBrackets: _size.Value = Math.Max(_size.Minimum, _size.Value - 1); e.Handled = true; break;
            case Key.OemCloseBrackets: _size.Value = Math.Min(_size.Maximum, _size.Value + 1); e.Handled = true; break;
            case Key.Delete: Undo(); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    void OnClosing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty) return;
        var r = MessageBox.Show(this, "Lưu thay đổi trước khi đóng?", "Hezb Clips", MessageBoxButton.YesNoCancel);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes && Save() is null) e.Cancel = true;
    }

    // ---------- drawing ----------

    Brush ColorBrush(byte alpha = 255) => new SolidColorBrush(Color.FromArgb(alpha, _color.R, _color.G, _color.B));
    double Thickness => _size.Value;

    void OnDown(object s, MouseButtonEventArgs e)
    {
        CommitText();
        _start = e.GetPosition(_canvas);
        switch (_tool)
        {
            case Tool.Number: AddNumber(_start); e.Handled = true; return;
        }
        _canvas.CaptureMouse();
        switch (_tool)
        {
            case Tool.Pen:
                _poly = new Polyline { Stroke = ColorBrush(), StrokeThickness = Thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _poly.Points.Add(_start); _poly.Points.Add(_start); _drawing = _poly; break;
            case Tool.Highlighter:
                _poly = new Polyline { Stroke = ColorBrush(110), StrokeThickness = Thickness * 4, StrokeLineJoin = PenLineJoin.Round };
                _poly.Points.Add(_start); _poly.Points.Add(_start); _drawing = _poly; break;
            case Tool.Line: _drawing = new Line { Stroke = ColorBrush(), StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, X1 = _start.X, Y1 = _start.Y, X2 = _start.X, Y2 = _start.Y }; break;
            case Tool.Arrow: _drawing = new Path { Stroke = ColorBrush(), StrokeThickness = Thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
            case Tool.Rect: _drawing = new Rectangle { Stroke = ColorBrush(), StrokeThickness = Thickness, RadiusX = 2, RadiusY = 2 }; break;
            case Tool.Ellipse: _drawing = new Ellipse { Stroke = ColorBrush(), StrokeThickness = Thickness }; break;
            case Tool.Text:
                _drawing = new Rectangle { Stroke = Ui.Brush("Accent"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 76, 141, 255)) };
                break;
            case Tool.Crop:
                _drawing = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 5, 3 }, Fill = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) };
                break;
            case Tool.Mosaic:
                var r = new Rectangle { Stroke = Brushes.Transparent };
                RenderOptions.SetBitmapScalingMode(r, BitmapScalingMode.NearestNeighbor);
                _drawing = r; break;
        }
        if (_drawing is null) return;
        if (_drawing is not Line and not Polyline) { Canvas.SetLeft(_drawing, _start.X); Canvas.SetTop(_drawing, _start.Y); }
        _canvas.Children.Add(_drawing);
    }

    void OnMove(object s, MouseEventArgs e)
    {
        if (_drawing is null) return;
        var p = e.GetPosition(_canvas);
        p = new Point(Math.Clamp(p.X, 0, _canvas.Width), Math.Clamp(p.Y, 0, _canvas.Height));
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (_drawing)
        {
            case Polyline pl: pl.Points.Add(p); break;
            case Line ln:
                if (shift) p = SnapAngle(_start, p);
                ln.X2 = p.X; ln.Y2 = p.Y; break;
            case Path ar:
                if (shift) p = SnapAngle(_start, p);
                ar.Data = ArrowGeometry(_start, p, Thickness); break;
            case Shape sh:
                var w = p.X - _start.X; var h = p.Y - _start.Y;
                if (shift && _tool != Tool.Mosaic) { var m = Math.Max(Math.Abs(w), Math.Abs(h)); w = Math.Sign(w) * m; h = Math.Sign(h) * m; }
                var x = w < 0 ? _start.X + w : _start.X; var y = h < 0 ? _start.Y + h : _start.Y;
                Canvas.SetLeft(sh, x); Canvas.SetTop(sh, y); sh.Width = Math.Abs(w); sh.Height = Math.Abs(h);
                if (_tool == Tool.Mosaic) sh.Fill = MosaicBrush(new Rect(x, y, Math.Abs(w), Math.Abs(h)));
                break;
        }
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (_drawing is null) return;
        _canvas.ReleaseMouseCapture();
        var d = _drawing; _drawing = null; _poly = null;
        if (_tool == Tool.Text && d is Rectangle box)
        {
            // drag defines the text box; font size follows its height. A plain click uses a default box.
            var rect = new Rect(Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height);
            _canvas.Children.Remove(d);
            if (double.IsNaN(rect.Width) || rect.Width < 12 || rect.Height < 12) rect = new Rect(_start.X, _start.Y, 260, 14 + Thickness * 3 + 12);
            StartText(rect);
            return;
        }
        if (_tool == Tool.Crop && d is Rectangle cropBox)
        {
            var rect = new Rect(Canvas.GetLeft(cropBox), Canvas.GetTop(cropBox), cropBox.Width, cropBox.Height);
            _canvas.Children.Remove(d);
            ApplyCrop(rect);
            return;
        }
        var tiny = d switch
        {
            Polyline pl => false,
            Line ln => Math.Abs(ln.X2 - ln.X1) < 3 && Math.Abs(ln.Y2 - ln.Y1) < 3,
            Path ar => ar.Data is null,
            Shape sh => sh.Width < 3 || sh.Height < 3 || double.IsNaN(sh.Width) || double.IsNaN(sh.Height),
            _ => false,
        };
        if (tiny) _canvas.Children.Remove(d); else Commit(d);
    }

    static Point SnapAngle(Point a, Point b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + len * Math.Cos(ang), a.Y + len * Math.Sin(ang));
    }

    static Geometry ArrowGeometry(Point a, Point b, double thickness)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false); c.LineTo(b, true, true);
            var dx = a.X - b.X; var dy = a.Y - b.Y; var len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1)
            {
                var head = Math.Min(len * 0.6, Math.Max(14, thickness * 4.5));
                var ang = Math.Atan2(dy, dx);
                foreach (var da in new[] { 0.45, -0.45 })
                {
                    var tip = new Point(b.X + head * Math.Cos(ang + da), b.Y + head * Math.Sin(ang + da));
                    c.BeginFigure(b, false, false); c.LineTo(tip, true, true);
                }
            }
        }
        g.Freeze(); return g;
    }

    Brush MosaicBrush(Rect r) => new ImageBrush(_pixelated)
    {
        Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
        Viewbox = new Rect(r.X / _canvas.Width, r.Y / _canvas.Height, Math.Max(r.Width / _canvas.Width, 1e-6), Math.Max(r.Height / _canvas.Height, 1e-6)),
    };

    void AddNumber(Point p)
    {
        var n = _items.Count(i => i is FrameworkElement { Tag: "num" }) + 1;
        var d = 22 + Thickness * 3;
        var g = new Grid { Width = d, Height = d, Tag = "num" };
        g.Children.Add(new Ellipse { Fill = ColorBrush(), Stroke = Brushes.White, StrokeThickness = 2 });
        var lum = (0.299 * _color.R + 0.587 * _color.G + 0.114 * _color.B);
        g.Children.Add(new TextBlock
        {
            Text = n.ToString(), FontWeight = FontWeights.Bold, FontSize = d * 0.5, Foreground = lum > 170 ? Brushes.Black : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(g, p.X - d / 2); Canvas.SetTop(g, p.Y - d / 2);
        _canvas.Children.Add(g); Commit(g);
    }

    // ---------- text ----------

    void StartText(Rect r)
    {
        var fontSize = Math.Clamp(r.Height * 0.55, 12, 300);
        _editor = new TextBox
        {
            Width = Math.Max(60, r.Width), MinHeight = r.Height, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize, FontWeight = FontWeights.Bold,
            Foreground = ColorBrush(), Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), CaretBrush = Brushes.White,
            BorderBrush = Ui.Brush("Accent"), BorderThickness = new Thickness(1), Padding = new Thickness(2),
        };
        Canvas.SetLeft(_editor, r.X); Canvas.SetTop(_editor, r.Y);
        _canvas.Children.Add(_editor);
        _editor.Loaded += (_, _) => _editor?.Focus();
        _editor.LostKeyboardFocus += (_, _) => { if (_editor is not null) CommitText(); };
    }

    void CommitText()
    {
        var ed = _editor; if (ed is null) return;
        _editor = null;
        _canvas.Children.Remove(ed);
        if (string.IsNullOrWhiteSpace(ed.Text)) return;
        var tb = new TextBlock { Text = ed.Text, FontSize = ed.FontSize, FontWeight = FontWeights.Bold, Foreground = ed.Foreground, TextWrapping = TextWrapping.Wrap, Width = ed.Width - 6 };
        Canvas.SetLeft(tb, Canvas.GetLeft(ed) + 3); Canvas.SetTop(tb, Canvas.GetTop(ed) + 1);
        _canvas.Children.Add(tb); Commit(tb);
    }

    void CancelText()
    {
        var ed = _editor; if (ed is null) return;
        _editor = null; _canvas.Children.Remove(ed);
    }

    // ---------- crop / rotate ----------

    /// <summary>Swap the picture under the annotations (crop, rotate, restore). Annotations are flattened first by the callers.</summary>
    void ReplaceBase(BitmapSource bmp)
    {
        CancelText();
        foreach (var el in _items) _canvas.Children.Remove(el);
        _items.Clear(); _redo.Clear();
        _bmp = bmp; _pixelated = Pixelate(bmp);
        _canvas.Width = bmp.PixelWidth; _canvas.Height = bmp.PixelHeight;
        _baseImage.Source = bmp; _baseImage.Width = bmp.PixelWidth; _baseImage.Height = bmp.PixelHeight;
        _dirty = true;
    }

    void ApplyCrop(Rect r)
    {
        var x = (int)Math.Clamp(Math.Round(r.X), 0, _bmp.PixelWidth - 1);
        var y = (int)Math.Clamp(Math.Round(r.Y), 0, _bmp.PixelHeight - 1);
        var w = (int)Math.Min(Math.Round(r.Width), _bmp.PixelWidth - x);
        var h = (int)Math.Min(Math.Round(r.Height), _bmp.PixelHeight - y);
        if (w < 8 || h < 8) return;
        var flat = Render(); // annotations inside the crop are kept
        var cropped = new CroppedBitmap(flat, new Int32Rect(x, y, w, h)); cropped.Freeze();
        ReplaceBase(cropped);
        _status.Text = $"✓ Đã cắt còn {w} × {h}. Bấm \"↺ Gốc\" để khôi phục ảnh ban đầu.";
    }

    void Rotate(bool right)
    {
        var flat = Render();
        var rotated = new TransformedBitmap(flat, new RotateTransform(right ? 90 : -90)); rotated.Freeze();
        ReplaceBase(rotated);
        _status.Text = right ? "✓ Đã xoay phải 90°" : "✓ Đã xoay trái 90°";
    }

    void RestoreOriginal()
    {
        try { ReplaceBase(LoadBitmap(_path)); _dirty = false; _status.Text = "✓ Đã khôi phục ảnh gốc"; }
        catch (Exception ex) { _status.Text = "Không đọc lại được ảnh gốc: " + ex.Message; }
    }

    // ---------- history ----------

    void Commit(UIElement el) { _items.Add(el); _redo.Clear(); _dirty = true; }

    void Undo()
    {
        CancelText();
        if (_items.Count == 0) return;
        var el = _items[^1]; _items.RemoveAt(_items.Count - 1);
        _canvas.Children.Remove(el); _redo.Push(el); _dirty = _items.Count > 0 || _dirty;
    }

    void Redo()
    {
        if (_redo.Count == 0) return;
        var el = _redo.Pop(); _canvas.Children.Add(el); _items.Add(el); _dirty = true;
    }

    void ClearAll()
    {
        CancelText();
        foreach (var el in _items) { _canvas.Children.Remove(el); _redo.Push(el); }
        _items.Clear();
    }

    // ---------- output ----------

    BitmapSource Render()
    {
        CancelText();
        var w = _bmp.PixelWidth; var h = _bmp.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(_canvas) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    string? Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path)!;
            var stem = System.IO.Path.GetFileNameWithoutExtension(_path);
            if (stem.EndsWith("_edit", StringComparison.OrdinalIgnoreCase)) stem = stem[..^5];
            var outPath = System.IO.Path.Combine(dir, stem + "_edit.png");
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Render()));
            using (var fs = File.Create(outPath)) enc.Save(fs);
            _dirty = false;
            _status.Text = "✓ Đã lưu: " + System.IO.Path.GetFileName(outPath);
            return outPath;
        }
        catch (Exception ex) { MessageBox.Show(this, "Không lưu được: " + ex.Message, "Hezb Clips"); return null; }
    }

    void CopyToClipboard()
    {
        try { Clipboard.SetImage(Render()); _status.Text = "✓ Đã copy ảnh vào clipboard"; }
        catch (Exception) { _status.Text = "Clipboard đang bận, thử lại."; }
    }

    // ---------- loading ----------

    static BitmapSource LoadBitmap(string path)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit(); b.UriSource = new Uri(path); b.CacheOption = BitmapCacheOption.OnLoad; b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; b.EndInit();
            b.Freeze(); return b;
        }
        catch (Exception)
        {
            // e.g. WebP: decode with ImageSharp and hand WPF a PNG
            using var img = SixLabors.ImageSharp.Image.Load(path);
            using var ms = new MemoryStream();
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms);
            ms.Position = 0;
            var b = new BitmapImage();
            b.BeginInit(); b.StreamSource = ms; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
            return b;
        }
    }

    /// <summary>Block-averaged copy of the image (about 1/14 size) used as the mosaic fill.</summary>
    static BitmapSource Pixelate(BitmapSource src)
    {
        var f = 1.0 / 14;
        var small = new TransformedBitmap(src, new ScaleTransform(f, f));
        small.Freeze();
        return small;
    }
}
