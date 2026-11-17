using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HezbClips.Core.Hotkeys;
using HezbClips.Core.Settings;

namespace HezbClips.App.Windows;

public sealed class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly ComboBox _quality = new(), _imageFmt = new(), _mode = new(), _fps = new();
    readonly CheckBox _sys = new() { Content = "Thu âm thanh hệ thống" }, _mic = new() { Content = "Thu mic" }, _startup = new() { Content = "Khởi động cùng Windows" };
    readonly TextBox _dir = new(), _hkRegion = new(), _hkFull = new(), _hkRecord = new(), _hkGif = new(), _hkLibrary = new();
    readonly Dictionary<string, TextBox> _editorKeys = new();
    readonly TextBlock _error = new() { Foreground = Ui.Brush("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    static readonly Dictionary<string, string> ToolLabels = new()
    {
        ["Pen"] = "Bút", ["Highlighter"] = "Tô sáng", ["Line"] = "Đường thẳng", ["Arrow"] = "Mũi tên", ["Rect"] = "Chữ nhật",
        ["Ellipse"] = "Elip", ["Text"] = "Chữ", ["Number"] = "Số", ["Mosaic"] = "Làm mờ",
    };

    public SettingsWindow(AppSettings s, IReadOnlyList<HotkeyFailure> hotkeyFailures, Action<AppSettings> onSave, Action onRedetect)
    {
        _s = s;
        Title = "Hezb Clips — Cài đặt"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Ui.Logo("logo.png", 64); Background = Ui.Brush("Bg"); Foreground = Ui.Brush("Text");

        _quality.ItemsSource = new[] { "1080p (Full HD)", "1440p (2K)", "2160p (4K)" };
        _quality.SelectedIndex = s.Quality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };
        _imageFmt.ItemsSource = new[] { "PNG (không nén mất dữ liệu)", "WebP", "JPEG" }; _imageFmt.SelectedIndex = (int)s.ImageFormat;
        _mode.ItemsSource = new[] { "Tự động (theo cấu hình máy)", "Live (upscale khi quay)", "Nền (upscale sau khi quay)" }; _mode.SelectedIndex = (int)s.VideoMode;
        _fps.ItemsSource = new[] { "30", "60" }; _fps.SelectedIndex = s.Fps >= 60 ? 1 : 0;
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone; _startup.IsChecked = s.StartWithWindows;
        _dir.Text = s.OutputDir;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new Image { Source = Ui.Logo("logo.png", 96), Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Hezb Clips", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text") });
        titles.Children.Add(new TextBlock { Text = "Chụp và quay màn hình chất lượng cao", FontSize = 12, Foreground = Ui.Brush("TextDim") });
        head.Children.Add(titles); panel.Children.Add(head);

        void Label(string label) => panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Ui.Brush("TextDim") });
        void Row(string label, UIElement c) { Label(label); panel.Children.Add(c); }
        void Section(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 22, 0, 0), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text") });
        void Note(string text, double top = 4) => panel.Children.Add(new TextBlock { Text = text, Foreground = Ui.Brush("Disabled"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) });

        Section("Chất lượng & quay");
        Row("Chất lượng xuất", _quality);
        Note("Upscale làm ảnh nét và mượt hơn, không thêm chi tiết gốc.", 2);
        Row("Định dạng ảnh", _imageFmt);
        Row("Chế độ quay video", _mode);
        Row("FPS", _fps);
        panel.Children.Add(_sys); panel.Children.Add(_mic);
        _sys.Margin = new Thickness(0, 10, 0, 2);
        Row("Thư mục lưu", _dir);

        Section("Phím tắt toàn cục");
        Note("Hoạt động ở mọi nơi, kể cả khi app thu nhỏ. Định dạng: Ctrl+Shift+S, Alt+F1, ...");
        Row("Chụp vùng / tự nhận khung", _hkRegion);
        Row("Chụp toàn màn hình", _hkFull);
        Row("Quay video (bắt đầu / dừng)", _hkRecord);
        Row("Quay GIF (tối đa 60 giây)", _hkGif);
        Row("Mở thư viện", _hkLibrary);

        Section("Phím tắt trình chỉnh sửa ảnh");
        Note("Mỗi công cụ một chữ cái A-Z (không dùng C, đã dành cho màu tùy chọn).");
        var grid = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var tool in EditorShortcuts.Tools)
        {
            var box = new TextBox { Width = 38, MaxLength = 1, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold };
            _editorKeys[tool] = box;
            var item = new StackPanel { Orientation = Orientation.Horizontal, Width = 150, Margin = new Thickness(0, 0, 0, 6) };
            item.Children.Add(box);
            item.Children.Add(new TextBlock { Text = ToolLabels[tool], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            grid.Children.Add(item);
        }
        panel.Children.Add(grid);
        LoadHotkeys(s);
        foreach (var f in hotkeyFailures)
        {
            var box = f.Name switch { "RegionShot" => _hkRegion, "FullShot" => _hkFull, "Gif" => _hkGif, "Library" => _hkLibrary, _ => _hkRecord };
            box.BorderBrush = Ui.Brush("Danger"); box.ToolTip = "Phím tắt này đang bị chương trình khác chiếm. Hãy chọn tổ hợp khác.";
            _error.Text += $"Phím tắt {f.Combo} bị chiếm. ";
        }

        var reset = new Button { Content = "Khôi phục phím tắt mặc định", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => LoadHotkeys(new AppSettings());
        panel.Children.Add(reset);
        panel.Children.Add(BuildGuide());

        panel.Children.Add(_startup); _startup.Margin = new Thickness(0, 22, 0, 0);
        panel.Children.Add(_error);

        var redetect = new Button { Content = "Dò lại cấu hình máy", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 16, 8, 0) };
        redetect.Click += (_, _) => { onRedetect(); MessageBox.Show(this, "Đang dò lại cấu hình ở nền.", "Hezb Clips"); };
        var save = new Button { Content = "Lưu", IsDefault = true, Style = Ui.Style("PrimaryButton"), Padding = new Thickness(20, 7, 20, 7), Margin = new Thickness(0, 16, 8, 0) };
        var cancel = new Button { Content = "Đóng", IsCancel = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 16, 0, 0) };
        save.Click += (_, _) => { if (Apply()) { onSave(_s); Close(); } };
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(redetect); bar.Children.Add(save); bar.Children.Add(cancel);
        panel.Children.Add(bar);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    void LoadHotkeys(AppSettings s)
    {
        _hkRegion.Text = s.HotkeyRegionShot; _hkFull.Text = s.HotkeyFullShot; _hkRecord.Text = s.HotkeyRecord;
        _hkGif.Text = s.HotkeyGif; _hkLibrary.Text = s.HotkeyLibrary;
        var keys = EditorShortcuts.Resolve(s.EditorKeys);
        foreach (var (tool, box) in _editorKeys) box.Text = keys[tool];
    }

    /// <summary>Read-only cheat sheet of every shortcut (values as saved; reopen Settings after changing).</summary>
    UIElement BuildGuide()
    {
        var g = new StackPanel();
        g.Children.Add(new TextBlock { Text = "Hướng dẫn phím tắt", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 0, 0, 4) });
        void Group(string title, params (string Keys, string What)[] rows)
        {
            g.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Accent"), Margin = new Thickness(0, 10, 0, 3) });
            foreach (var (k, w) in rows)
            {
                var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
                var chip = new Border
                {
                    Background = Ui.Brush("Surface"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 1, 6, 1), Width = 124, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = k, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Ui.Brush("Text"), TextWrapping = TextWrapping.Wrap },
                };
                DockPanel.SetDock(chip, Dock.Left);
                line.Children.Add(chip);
                line.Children.Add(new TextBlock { Text = w, FontSize = 12, Foreground = Ui.Brush("TextDim"), TextWrapping = TextWrapping.Wrap });
                g.Children.Add(line);
            }
        }
        var keys = EditorShortcuts.Resolve(_s.EditorKeys);
        string K(string t) => keys[t];
        Group("Toàn cục",
            (_s.HotkeyRegionShot, "Chụp vùng: hover tự nhận khung, click chọn, kéo để tự vẽ vùng"),
            (_s.HotkeyFullShot, "Chụp toàn màn hình"),
            (_s.HotkeyRecord, "Chọn vùng rồi quay video; bấm lại để hủy thanh quay hoặc dừng"),
            (_s.HotkeyGif, "Chọn vùng rồi quay GIF, tự dừng sau 60 giây"),
            (_s.HotkeyLibrary, "Mở thư viện ảnh / video / GIF"),
            ("Esc", "Hủy khi đang chọn vùng"));
        Group("Trình chỉnh sửa ảnh",
            ($"{K("Pen")} {K("Highlighter")} {K("Line")} {K("Arrow")}", "Bút · tô sáng · đường thẳng · mũi tên"),
            ($"{K("Rect")} {K("Ellipse")} {K("Text")} {K("Number")} {K("Mosaic")}", "Chữ nhật · elip · chữ · đánh số · làm mờ"),
            ("1 - 9 / C", "Chọn màu có sẵn / màu tùy chọn"),
            ("[   ]", "Giảm / tăng độ dày nét"),
            ("Shift (khi vẽ)", "Giữ tỉ lệ: hình vuông, hình tròn, góc 45°"),
            ("Ctrl+Z / Ctrl+Y", "Hoàn tác / làm lại (Delete cũng hoàn tác)"),
            ("Ctrl+Shift+Z", "Xóa hết chú thích"),
            ("Ctrl+S / Ctrl+C", "Lưu file *_edit.png / copy ảnh"),
            ("Esc", "Đóng trình chỉnh sửa"));
        Group("Thư viện", ("F5", "Làm mới"), ("Esc", "Đóng"), ("Click thẻ", "Mở file"));
        return new Border
        {
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(14), CornerRadius = new CornerRadius(12),
            Background = Ui.Brush("Bg"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), Child = g,
        };
    }

    bool Apply()
    {
        foreach (var (box, name) in new[] { (_hkRegion, "chụp vùng"), (_hkFull, "chụp toàn màn hình"), (_hkRecord, "quay"), (_hkGif, "quay GIF"), (_hkLibrary, "thư viện") })
        {
            try { HotkeyParser.Parse(box.Text); }
            catch (FormatException) { _error.Text = $"Phím tắt '{name}' không hợp lệ (ví dụ: Ctrl+Shift+S)."; return false; }
        }
        var combos = new[] { _hkRegion, _hkFull, _hkRecord, _hkGif, _hkLibrary }.Select(b => b.Text.Trim().ToUpperInvariant().Replace(" ", "")).ToList();
        if (combos.Distinct().Count() != combos.Count) { _error.Text = "Hai phím tắt toàn cục đang trùng nhau."; return false; }
        var editorKeys = _editorKeys.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim().ToUpperInvariant());
        if (EditorShortcuts.Validate(editorKeys) is { } err) { _error.Text = err; return false; }
        _error.Text = "";

        _s.Quality = _quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
        _s.ImageFormat = (ImageFormat)_imageFmt.SelectedIndex;
        _s.VideoMode = (VideoMode)_mode.SelectedIndex;
        _s.Fps = _fps.SelectedIndex == 1 ? 60 : 30;
        _s.CaptureSystemAudio = _sys.IsChecked == true; _s.CaptureMicrophone = _mic.IsChecked == true;
        _s.StartWithWindows = _startup.IsChecked == true;
        _s.OutputDir = string.IsNullOrWhiteSpace(_dir.Text) ? _s.OutputDir : _dir.Text.Trim();
        _s.HotkeyRegionShot = _hkRegion.Text.Trim(); _s.HotkeyFullShot = _hkFull.Text.Trim(); _s.HotkeyRecord = _hkRecord.Text.Trim();
        _s.HotkeyGif = _hkGif.Text.Trim(); _s.HotkeyLibrary = _hkLibrary.Text.Trim();
        _s.EditorKeys = editorKeys;
        return true;
    }
}
