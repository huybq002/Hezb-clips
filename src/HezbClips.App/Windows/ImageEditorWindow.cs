using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace HezbClips.App.Windows;

/// <summary>Annotate a captured image: select/move, pen, highlighter, line, arrow, rectangle, ellipse, text, numbered badge,
/// mosaic and crop. Every tool and colour has a keyboard shortcut; every action gives visible feedback and can be undone.</summary>
public sealed partial class ImageEditorWindow : Window
{
    enum Tool { Select, Pen, Highlighter, Line, Arrow, Rect, Ellipse, Text, Number, Mosaic, Crop }

    static readonly (string Name, string Hex)[] Palette =
    {
        ("Đỏ", "#E5484D"), ("Cam", "#F5A524"), ("Vàng", "#FFD60A"), ("Xanh lá", "#2EA043"), ("Cyan", "#22D3EE"),
        ("Xanh dương", "#4C8DFF"), ("Tím", "#A855F7"), ("Trắng", "#FFFFFF"), ("Đen", "#000000"),
    };

    static readonly Color Green = Color.FromRgb(46, 160, 67);

    readonly string _path;
    readonly bool _copyOnSave;
    BitmapSource _bmp;
    BitmapSource _pixelated;
    readonly Image _baseImage = new() { IsHitTestVisible = false };
    readonly Canvas _canvas = new();   // picture + annotations (what gets saved)
    readonly Canvas _ui = new() { IsHitTestVisible = false }; // selection / crop overlays (never saved)
    readonly Grid _stage = new() { Background = Brushes.Transparent };
    readonly ScaleTransform _scale = new(1, 1);
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    double? _manualZoom;               // null = fit to window
    readonly TextBlock _status = new() { Foreground = Ui.Brush("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _zoomText = new() { Foreground = Ui.Brush("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Nhấp để vừa khung / 100% (Ctrl+0 / Ctrl+1)" };
    readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    readonly List<Button> _swatches = new();
    readonly Rectangle _currentColorChip = new() { Width = 22, Height = 22, RadiusX = 11, RadiusY = 11, Stroke = Brushes.White, StrokeThickness = 1.5 };
    readonly Slider _size = new() { Minimum = 1, Maximum = 20, Value = 4, Width = 110, VerticalAlignment = VerticalAlignment.Center, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly List<UIElement> _items = new();   // annotations on the canvas, bottom to top
    Tool _tool = Tool.Rect;
    Color _color = (Color)ColorConverter.ConvertFromString("#E5484D");
    Point _start; UIElement? _drawing; Polyline? _poly; TextBox? _editor;
    UIElement? _selected; bool _moving; Point _moveFrom; Vector _moved;
    readonly Border _dragLabel = new() { Background = new SolidColorBrush(Color.FromArgb(225, 20, 22, 30)), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2), Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0, 1) };
    readonly TextBlock _dragLabelText = new() { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Consolas") };
    readonly Rectangle _selBox = new() { Stroke = Ui.Brush("Accent"), StrokeDashArray = new DoubleCollection { 4, 3 }, Visibility = Visibility.Collapsed, Fill = Brushes.Transparent };

    readonly Dictionary<string, string> _keys;
    readonly Dictionary<Key, Tool> _keyToTool = new();

    // feedback: the bubble at the bottom of the picture, and the flash on the button that was pressed
    readonly Border _hud = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 16, 9), Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 22),
        Background = new SolidColorBrush(Color.FromArgb(235, 22, 24, 32)), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5 } };
    readonly TextBlock _hudIcon = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0), FontFamily = new FontFamily(Ui.IconFontName) };
    readonly TextBlock _hudText = new() { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<Button, DispatcherTimer> _flashing = new();

    public ImageEditorWindow(string path, IDictionary<string, string>? toolKeys = null, bool copyOnSave = true)
    {
        _path = path; _copyOnSave = copyOnSave;
        _keys = HezbClips.Core.Settings.EditorShortcuts.Resolve(toolKeys);
        foreach (var (name, letter) in _keys)
            if (Enum.TryParse<Tool>(name, out var t) && Enum.TryParse<Key>(letter, out var k)) _keyToTool[k] = t;
        _bmp = LoadBitmap(path);
        _pixelated = Pixelate(_bmp);
        Title = "Hezb Clips — Chỉnh sửa · " + System.IO.Path.GetFileName(path);
        Width = 1180; Height = 780; MinWidth = 760; MinHeight = 480;
        Icon = Ui.Logo("logo-light.png", 64); Background = Ui.Brush("Bg"); WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _canvas.Width = _ui.Width = _bmp.PixelWidth; _canvas.Height = _ui.Height = _bmp.PixelHeight;
        _canvas.Background = Brushes.Transparent;
        _baseImage.Source = _bmp; _baseImage.Width = _bmp.PixelWidth; _baseImage.Height = _bmp.PixelHeight;
        _canvas.Children.Add(_baseImage);
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.HighQuality);

        _ui.Children.Add(_selBox);
        BuildCropUi();
        _dragLabel.Child = _dragLabelText; _ui.Children.Add(_dragLabel);

        _stage.Children.Add(_canvas); _stage.Children.Add(_ui);
        _stage.LayoutTransform = _scale;
        _stage.MouseLeftButtonDown += OnDown; _stage.MouseMove += OnMove; _stage.MouseLeftButtonUp += OnUp;
        _stage.MouseDown += OnMiddleDown; _stage.MouseUp += OnMiddleUp;
        var framed = new Border { Child = _stage, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, Opacity = 0.5, ShadowDepth = 0 } };
        _scroll.Content = framed;
        _scroll.PreviewMouseWheel += OnWheel;
        _scroll.SizeChanged += (_, _) => { if (_manualZoom is null) ApplyZoom(); };

        _hud.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _hudIcon, _hudText } };
        var center = new Grid(); center.Children.Add(_scroll); center.Children.Add(BuildCropBar()); center.Children.Add(_hud);

        var root = new DockPanel();
        var top = BuildToolbar();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var statusRow = new DockPanel { Margin = new Thickness(16, 8, 16, 10) };
        _zoomText.MouseLeftButtonUp += (_, _) => { if (_manualZoom is null) SetZoom(1.0); else FitZoom(); };
        DockPanel.SetDock(_zoomText, Dock.Right); statusRow.Children.Add(_zoomText); statusRow.Children.Add(_status);
        var bottom = new Border { Child = statusRow, BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(center);
        Content = root;

        SelectTool(Tool.Rect); SelectColor(Palette[0].Hex);
        PreviewKeyDown += OnKey;
        Closing += OnClosing;
        Loaded += (_, _) => { ApplyZoom(); Hud("", "Ảnh đã sẵn sàng để chỉnh sửa. Chọn công cụ và kéo trên ảnh."); };
    }

    // ---------- toolbar ----------

    readonly Dictionary<string, Button> _actions = new();

    UIElement BuildToolbar()
    {
        var wrap = new WrapPanel { Margin = new Thickness(12, 10, 12, 8) };
        void Sep() => wrap.Children.Add(new Border { Width = 1, Background = Ui.Brush("Border"), Margin = new Thickness(8, 4, 8, 4) });
        void ToolBtn(Tool t, string icon, string name)
        {
            var key = _keys.TryGetValue(t.ToString(), out var kk) ? kk : "";
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            var lc = Ui.Content($"{icon} {name}"); label.Children.Add(lc as UIElement ?? new TextBlock { Text = (string)lc });
            if (key.Length > 0)
                label.Children.Add(new Border
                {
                    Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
                    Child = new TextBlock { Text = key, FontSize = 10, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas") },
                });
            var tip = new StackPanel { Orientation = Orientation.Horizontal };
            tip.Children.Add(new TextBlock { Text = name + "  " });
            if (key.Length > 0) tip.Children.Add(new TextBlock { Text = key, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas") });
            var b = new ToggleButton { Content = label, ToolTip = new ToolTip { Content = tip }, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 5, 10, 5) };
            ToolTipService.SetInitialShowDelay(b, 150);
            b.Click += (_, _) => SelectTool(t);
            _toolButtons[t] = b; wrap.Children.Add(b);
        }
        ToolBtn(Tool.Select, "", "Chọn");
        ToolBtn(Tool.Pen, "", "Bút");
        ToolBtn(Tool.Highlighter, "", "Tô sáng");
        ToolBtn(Tool.Line, "╱", "Đường thẳng");
        ToolBtn(Tool.Arrow, "", "Mũi tên");
        ToolBtn(Tool.Rect, "", "Chữ nhật");
        ToolBtn(Tool.Ellipse, "", "Elip");
        ToolBtn(Tool.Text, "", "Chữ");
        ToolBtn(Tool.Number, "①", "Số");
        ToolBtn(Tool.Mosaic, "▦", "Làm mờ");
        ToolBtn(Tool.Crop, "", "Cắt ảnh");
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
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1.5),
            };
            sw.Click += (_, _) => SelectColor(hex);
            _swatches.Add(sw); wrap.Children.Add(sw);
        }
        var custom = new Button { Content = Ui.Glyph(""), ToolTip = "Màu tùy chọn (C)", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 3, 8, 3) };
        custom.Click += (_, _) => PickCustomColor();
        wrap.Children.Add(custom);
        Sep();

        wrap.Children.Add(new TextBlock { Text = "Độ dày", Foreground = Ui.Brush("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
        _size.Margin = new Thickness(0, 0, 0, 4); _size.ToolTip = "[  ]  giảm / tăng";
        _size.ValueChanged += (_, _) => UpdateHint();
        wrap.Children.Add(_size);
        Sep();

        Button Act(string key, string text, string tip, Action<Button> a, bool primary = false)
        {
            var b = new Button { Content = Ui.Content(text), ToolTip = tip, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(10, 5, 10, 5), Tag = text };
            if (primary) b.Style = Ui.Style("PrimaryButton");
            b.Click += (_, _) => a(b); wrap.Children.Add(b); _actions[key] = b; return b;
        }
        Act("undo", "", "Hoàn tác (Ctrl+Z)", _ => Undo());
        Act("redo", "", "Làm lại (Ctrl+Y)", _ => Redo());
        Act("clear", " Xóa hết", "Xóa mọi chú thích (Ctrl+Shift+Z). Vẫn hoàn tác được.", _ => ClearAll());
        Act("left", " Trái", "Xoay trái (Ctrl+L)", b => Rotate(false, b));
        Act("right", " Phải", "Xoay phải (Ctrl+R)", b => Rotate(true, b));
        Act("orig", " Gốc", "Khôi phục ảnh gốc (bỏ cắt, xoay, chú thích). Vẫn hoàn tác được.", b => RestoreOriginal(b));
        Sep();
        Act("ocr", " Copy chữ", "Nhận dạng chữ trong ảnh (OCR) và copy vào clipboard", b => _ = CopyTextAsync(b));
        Act("redact", " Che nhạy cảm", "Tự tìm email, số điện thoại, số thẻ trong ảnh và làm mờ", b => _ = RedactAsync(b));
        Act("pin", " Ghim", "Ghim ảnh này lên màn hình", b => PinImage(b));
        Sep();
        Act("copy", " Copy", "Copy ảnh (Ctrl+C)", b => CopyToClipboard(b));
        Act("save", " Lưu", "Lưu thành file mới (Ctrl+S)", b => Save(b), primary: true);
        return new Border { Child = wrap, BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    // ---------- feedback ----------

    /// <summary>Bubble over the picture: icon + message, fades by itself. ok=false shows it in red.</summary>
    void Hud(string glyph, string text, bool ok = true)
    {
        _hudIcon.Text = glyph; _hudIcon.Foreground = new SolidColorBrush(ok ? Color.FromRgb(86, 211, 100) : Color.FromRgb(255, 123, 114));
        _hudText.Text = text;
        _hud.Visibility = Visibility.Visible;
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130 + 1700))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130 + 1700 + 350))));
        fade.Completed += (_, _) => { if (_hud.Opacity < 0.01) _hud.Visibility = Visibility.Collapsed; };
        _hud.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Make the button itself show that it worked: green, check mark and short text, then back to normal.</summary>
    void Flash(Button b, string doneText)
    {
        if (_flashing.TryGetValue(b, out var running)) running.Stop();
        var original = b.Tag as string ?? "";
        var prevBg = b.Background; var prevFg = b.Foreground;
        b.Content = Ui.Content(" " + doneText);
        b.Background = new SolidColorBrush(Green); b.Foreground = Brushes.White;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(1, 1); b.RenderTransform = st;
        var pulse = new DoubleAnimation(1, 1.1, TimeSpan.FromMilliseconds(110)) { AutoReverse = true };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse); st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        t.Tick += (_, _) =>
        {
            t.Stop(); _flashing.Remove(b);
            b.Content = Ui.Content(original); b.Background = prevBg; b.Foreground = prevFg;
        };
        _flashing[b] = t; t.Start();
    }

    void Done(Button? b, string buttonText, string message)
    {
        if (b is not null) Flash(b, buttonText);
        Hud("", message);
    }

    void UpdateHint()
    {
        var hint = _tool switch
        {
            Tool.Select => "Nhấp một chú thích để chọn, kéo để di chuyển · Delete xóa · mũi tên nhích 1 px (Shift: 10 px)",
            Tool.Text => "Kéo một khung để đặt chữ (cỡ chữ theo chiều cao khung) · Enter xong · Shift+Enter xuống dòng",
            Tool.Crop => "Kéo vùng cần giữ · kéo tay nắm để chỉnh · Enter để cắt · Esc để hủy",
            Tool.Number => "Nhấp để đặt số thứ tự",
            Tool.Mosaic => "Kéo vùng cần làm mờ",
            _ => "Kéo trên ảnh để vẽ · giữ Shift: thẳng/vuông/tròn",
        };
        _status.Text = $"{ToolName(_tool)} · độ dày {(int)_size.Value}   |   {hint}   |   Ctrl+Z hoàn tác · Ctrl+S lưu · Ctrl+C copy · Ctrl+lăn chuột: thu phóng";
    }

    static string ToolName(Tool t) => t switch
    {
        Tool.Select => "Chọn", Tool.Pen => "Bút", Tool.Highlighter => "Tô sáng", Tool.Line => "Đường thẳng", Tool.Arrow => "Mũi tên", Tool.Rect => "Chữ nhật",
        Tool.Ellipse => "Elip", Tool.Text => "Chữ", Tool.Number => "Số", Tool.Mosaic => "Làm mờ", _ => "Cắt ảnh",
    };

    void SelectTool(Tool t)
    {
        CommitText();
        if (_tool == Tool.Crop && t != Tool.Crop) CancelCrop();
        _tool = t;
        if (t != Tool.Select) { _selected = null; }
        foreach (var (k, b) in _toolButtons) b.IsChecked = k == t;
        _stage.Cursor = t switch { Tool.Text => Cursors.IBeam, Tool.Select => Cursors.Arrow, _ => Cursors.Cross };
        RefreshUi(); UpdateHint();
    }

    void SelectColor(string hex) => SelectColor((Color)ColorConverter.ConvertFromString(hex));

    void SelectColor(Color c)
    {
        _color = c;
        _currentColorChip.Fill = new SolidColorBrush(c);
        if (_editor is not null) _editor.Foreground = new SolidColorBrush(c);
        // ring on the swatch that is in use
        foreach (var sw in _swatches)
        {
            var on = sw.Background is SolidColorBrush b && b.Color == c;
            sw.BorderBrush = on ? Ui.Brush("Accent") : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            sw.BorderThickness = new Thickness(on ? 3 : 1.5);
        }
    }

    void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B) };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SelectColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    // ---------- zoom ----------

    double FitScale()
    {
        var vw = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : Math.Max(200, ActualWidth - 40);
        var vh = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : Math.Max(200, ActualHeight - 200);
        return Math.Clamp(Math.Min((vw - 44) / _bmp.PixelWidth, (vh - 44) / _bmp.PixelHeight), 0.05, 1.0);
    }

    double Zoom => _scale.ScaleX;

    void ApplyZoom()
    {
        var z = _manualZoom ?? FitScale();
        _scale.ScaleX = _scale.ScaleY = z;
        _zoomText.Text = _manualZoom is null ? $"Vừa khung · {z:P0}" : $"{z:P0}";
        RefreshUi();
    }

    void SetZoom(double z) { _manualZoom = Math.Clamp(z, 0.1, 8); ApplyZoom(); }
    void FitZoom() { _manualZoom = null; ApplyZoom(); }

    void OnWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    Point _panFrom; double _panH, _panV; bool _panning;

    void OnMiddleDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panning = true; _panFrom = e.GetPosition(_scroll); _panH = _scroll.HorizontalOffset; _panV = _scroll.VerticalOffset;
        _stage.CaptureMouse(); _stage.Cursor = Cursors.SizeAll; e.Handled = true;
    }

    void OnMiddleUp(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_panning) return;
        _panning = false; _stage.ReleaseMouseCapture(); SelectTool(_tool); e.Handled = true;
    }

    // ---------- keyboard ----------

    void OnKey(object s, KeyEventArgs e)
    {
        if (_editor is not null && _editor.IsKeyboardFocused)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CommitText(); Save(_actions["save"]); e.Handled = true; } // save works while typing
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
                case Key.S: Save(_actions["save"]); e.Handled = true; break;
                case Key.C: CopyToClipboard(_actions["copy"]); e.Handled = true; break;
                case Key.L: Rotate(false, _actions["left"]); e.Handled = true; break;
                case Key.R: Rotate(true, _actions["right"]); e.Handled = true; break;
                case Key.D0 or Key.NumPad0: FitZoom(); e.Handled = true; break;
                case Key.D1 or Key.NumPad1: SetZoom(1); e.Handled = true; break;
                case Key.OemPlus or Key.Add: SetZoom(Zoom * 1.25); e.Handled = true; break;
                case Key.OemMinus or Key.Subtract: SetZoom(Zoom / 1.25); e.Handled = true; break;
            }
            return;
        }
        if (_crop is not null && e.Key == Key.Enter) { ApplyCrop(); e.Handled = true; return; }
        if (_keyToTool.TryGetValue(e.Key, out var tool)) { SelectTool(tool); e.Handled = true; return; }
        if (e.Key >= Key.D1 && e.Key <= Key.D9) { SelectColor(Palette[e.Key - Key.D1].Hex); e.Handled = true; return; }
        if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) { SelectColor(Palette[e.Key - Key.NumPad1].Hex); e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.C: PickCustomColor(); e.Handled = true; break;
            case Key.OemOpenBrackets: _size.Value = Math.Max(_size.Minimum, _size.Value - 1); e.Handled = true; break;
            case Key.OemCloseBrackets: _size.Value = Math.Min(_size.Maximum, _size.Value + 1); e.Handled = true; break;
            case Key.Delete or Key.Back when _selected is not null: DeleteSelected(); e.Handled = true; break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected is not null:
                var step = shift ? 10 : 1;
                var d = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
                Run(new MoveCmd(this, _selected, d)); e.Handled = true; break;
            case Key.Escape:
                if (_crop is not null) { CancelCrop(); Hud("", "Đã hủy vùng cắt", ok: false); }
                else if (_selected is not null) { _selected = null; RefreshUi(); }
                else Close();
                e.Handled = true; break;
        }
    }

    void OnClosing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        CommitText();
        if (!Dirty) return;
        var r = MessageBox.Show(this, "Lưu thay đổi trước khi đóng?", "Hezb Clips", MessageBoxButton.YesNoCancel);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes && Save(null) is null) e.Cancel = true;
    }

    // ---------- drawing ----------

    Brush ColorBrush(byte alpha = 255) => new SolidColorBrush(Color.FromArgb(alpha, _color.R, _color.G, _color.B));
    double Thickness => _size.Value;
    Point P(MouseEventArgs e) => e.GetPosition(_canvas);

    void OnDown(object s, MouseButtonEventArgs e)
    {
        CommitText();
        _start = P(e);
        if (_tool == Tool.Select) { SelectDown(e); return; }
        if (_tool == Tool.Crop) { CropDown(e); return; }
        if (_tool == Tool.Number) { AddNumber(_start); e.Handled = true; return; }
        _stage.CaptureMouse();
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
            case Tool.Mosaic:
                // dashed outline while dragging so the area being blurred is obvious; removed when committed
                var r = new Rectangle { Stroke = Ui.Brush("Accent"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Tag = "mosaic" };
                RenderOptions.SetBitmapScalingMode(r, BitmapScalingMode.NearestNeighbor);
                _drawing = r; break;
        }
        if (_drawing is null) return;
        // lines, pen strokes and arrows carry absolute coordinates; only box-like shapes are positioned with Canvas.Left/Top
        if (_drawing is not Line and not Polyline and not Path) { Canvas.SetLeft(_drawing, _start.X); Canvas.SetTop(_drawing, _start.Y); }
        _canvas.Children.Add(_drawing);
    }

    void OnMove(object s, MouseEventArgs e)
    {
        if (_panning)
        {
            var p0 = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(_panH - (p0.X - _panFrom.X)); _scroll.ScrollToVerticalOffset(_panV - (p0.Y - _panFrom.Y));
            return;
        }
        if (_tool == Tool.Select) { SelectMove(e); return; }
        if (_tool == Tool.Crop) { CropMove(e); return; }
        if (_drawing is null) return;
        var p = P(e);
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
                ShowDragLabel(new Rect(x, y, sh.Width, sh.Height));
                break;
        }
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Select) { SelectUp(e); return; }
        if (_tool == Tool.Crop) { CropUp(e); return; }
        if (_drawing is null) return;
        _stage.ReleaseMouseCapture();
        HideDragLabel();
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
        var tiny = d switch
        {
            Polyline => false,
            Line ln => Math.Abs(ln.X2 - ln.X1) < 3 && Math.Abs(ln.Y2 - ln.Y1) < 3,
            Path ar => ar.Data is null,
            Shape sh => sh.Width < 3 || sh.Height < 3 || double.IsNaN(sh.Width) || double.IsNaN(sh.Height),
            _ => false,
        };
        _canvas.Children.Remove(d);
        if (tiny) return;
        if (d is Rectangle { Tag: "mosaic" } mosaic) { mosaic.Stroke = Brushes.Transparent; mosaic.StrokeDashArray = null; }
        Run(new AddCmd(this, d, ItemName(d)));
    }

    static string ItemName(UIElement d) => d switch
    {
        Rectangle { Tag: "mosaic" } => "làm mờ",
        Rectangle => "chữ nhật", Ellipse => "elip", Line => "đường thẳng", Path => "mũi tên", Polyline p => p.StrokeThickness > 12 ? "tô sáng" : "nét vẽ",
        TextBlock => "chữ", _ => "chú thích",
    };

    /// <summary>"width × height px" next to the area being dragged, so the user sees exactly what is chosen.</summary>
    void ShowDragLabel(Rect r)
    {
        _dragLabelText.Text = $"{Math.Round(r.Width)} × {Math.Round(r.Height)} px";
        _dragLabel.Visibility = Visibility.Visible;
        var z = Zoom;
        _dragLabel.RenderTransform = new ScaleTransform(1 / z, 1 / z);
        Canvas.SetLeft(_dragLabel, r.X); Canvas.SetTop(_dragLabel, Math.Max(26 / z, r.Y) - 4 / z);
        Panel.SetZIndex(_dragLabel, 50);
    }

    void HideDragLabel() => _dragLabel.Visibility = Visibility.Collapsed;

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
        Run(new AddCmd(this, g, $"số {n}"));
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

    /// <summary>Turn the text being typed into a real annotation. Safe to call anytime (save, copy, undo, tool change…).</summary>
    void CommitText()
    {
        var ed = _editor; if (ed is null) return;
        _editor = null;
        _canvas.Children.Remove(ed);
        if (string.IsNullOrWhiteSpace(ed.Text)) return;
        var tb = new TextBlock { Text = ed.Text, FontSize = ed.FontSize, FontWeight = FontWeights.Bold, Foreground = ed.Foreground, TextWrapping = TextWrapping.Wrap, Width = ed.Width - 6 };
        Canvas.SetLeft(tb, Canvas.GetLeft(ed) + 3); Canvas.SetTop(tb, Canvas.GetTop(ed) + 1);
        Run(new AddCmd(this, tb, "chữ"));
    }

    void CancelText()
    {
        var ed = _editor; if (ed is null) return;
        _editor = null; _canvas.Children.Remove(ed);
    }

    // ---------- output ----------

    BitmapSource Render()
    {
        CommitText(); // never lose the text that is still being typed
        _canvas.UpdateLayout(); // an element added a moment ago has not been laid out yet and would render empty
        var w = _bmp.PixelWidth; var h = _bmp.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            // absolute viewbox: shapes poking past the edges must not shift or shrink the picture
            dc.DrawRectangle(new VisualBrush(_canvas) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    string? _savedPath;

    string? Save(Button? b)
    {
        try
        {
            var flat = Render();
            var dir = System.IO.Path.GetDirectoryName(_path)!;
            var stem = System.IO.Path.GetFileNameWithoutExtension(_path);
            if (stem.EndsWith("_edit", StringComparison.OrdinalIgnoreCase)) stem = stem[..^5];
            // first save picks a free name (never overwrites an older edit); later saves in this window update that file
            var outPath = _savedPath ?? System.IO.Path.Combine(dir, stem + "_edit.png");
            for (int i = 1; _savedPath is null && File.Exists(outPath); i++) outPath = System.IO.Path.Combine(dir, $"{stem}_edit_{i}.png");
            _savedPath = outPath;
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(flat));
            using (var fs = File.Create(outPath)) enc.Save(fs);
            _savedTop = _undo.Count > 0 ? _undo.Peek() : null;
            RefreshUi(); // clears the "unsaved" dot in the title
            var copied = _copyOnSave && TryCopy(flat);
            Done(b, "Đã lưu", $"Đã lưu {System.IO.Path.GetFileName(outPath)}" + (copied ? " và copy vào clipboard" : ""));
            return outPath;
        }
        catch (Exception ex)
        {
            Hud("", "Không lưu được: " + ex.Message, ok: false);
            return null;
        }
    }

    static bool TryCopy(BitmapSource img)
    {
        for (int i = 0; i < 3; i++)
        {
            try { Clipboard.SetImage(img); return true; }
            catch (Exception) { System.Threading.Thread.Sleep(50); }
        }
        return false;
    }

    void CopyToClipboard(Button? b)
    {
        if (TryCopy(Render())) Done(b, "Đã copy", "Đã copy ảnh (kèm chú thích) vào clipboard");
        else Hud("", "Clipboard đang bận, thử lại", ok: false);
    }

    void PinImage(Button? b)
    {
        new PinWindow(Render()).Show();
        Done(b, "Đã ghim", "Đã ghim ảnh lên màn hình (kéo để di chuyển, nhấp đúp để đóng)");
    }

    // ---------- crop / rotate / restore ----------

    void Rotate(bool right, Button? b = null)
    {
        var flat = Render();
        var old = _bmp; var items = _items.ToList();
        var rotated = new TransformedBitmap(flat, new RotateTransform(right ? 90 : -90)); rotated.Freeze();
        Run(new BaseCmd(this, old, rotated, items, right ? "xoay phải" : "xoay trái"));
        Done(b, right ? "Đã xoay" : "Đã xoay", right ? "Đã xoay phải 90° (Ctrl+Z để hoàn tác)" : "Đã xoay trái 90° (Ctrl+Z để hoàn tác)");
    }

    void RestoreOriginal(Button? b = null)
    {
        try
        {
            CommitText();
            var orig = LoadBitmap(_path);
            Run(new BaseCmd(this, _bmp, orig, _items.ToList(), "khôi phục ảnh gốc"));
            Done(b, "Đã khôi phục", "Đã khôi phục ảnh gốc (Ctrl+Z để quay lại bản vừa sửa)");
        }
        catch (Exception ex) { Hud("", "Không đọc lại được ảnh gốc: " + ex.Message, ok: false); }
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
