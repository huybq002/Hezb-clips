using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HezbClips.Core.Hotkeys;
using HezbClips.Core.Settings;

namespace HezbClips.App.Windows;

/// <summary>Settings shown inside the main window: a section list on the left, the section page on the right.</summary>
public sealed class SettingsView : Grid
{
    readonly AppSettings _s;
    readonly Action<AppSettings> _onSave;
    readonly Action _onRedetect;
    readonly Action<AppTheme> _onTheme;
    readonly ComboBox _theme = new(), _imgQuality = new(), _imgFormat = new(), _vidQuality = new(), _mode = new(), _fps = new(),
        _delay = new(), _gifFps = new(), _gifWidth = new(), _retention = new();
    readonly CheckBox _sys = new() { Content = "Thu âm thanh hệ thống" }, _mic = new() { Content = "Thu microphone" },
        _startup = new() { Content = "Khởi động cùng Windows (chạy ẩn ở khay hệ thống)" },
        _editAfter = new() { Content = "Tự mở trình chỉnh sửa ngay sau khi chụp ảnh" },
        _autoCopy = new() { Content = "Tự copy ảnh vào clipboard sau mỗi lần chụp" },
        _clicks = new() { Content = "Hiện vòng tròn khi bấm chuột" }, _keys = new() { Content = "Hiện phím bấm" }, _cam = new() { Content = "Hiện webcam ở góc vùng quay" };
    readonly TextBox _dir = new();
    readonly Dictionary<string, TextBox> _editorKeys = new();
    readonly Dictionary<string, TextBox> _hotkeys = new();
    readonly TextBlock _error = new() { Foreground = Ui.Brush("Danger"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly ContentControl _page = new();
    readonly List<(string Name, ToggleButton Btn, Func<UIElement> Build)> _sections = new();
    readonly Dictionary<string, UIElement> _built = new();
    Action? _extraApply;

    static readonly Dictionary<string, string> ToolLabels = new()
    {
        ["Pen"] = "Bút", ["Highlighter"] = "Tô sáng", ["Line"] = "Đường thẳng", ["Arrow"] = "Mũi tên", ["Rect"] = "Chữ nhật",
        ["Ellipse"] = "Elip", ["Text"] = "Chữ", ["Number"] = "Số", ["Mosaic"] = "Làm mờ", ["Crop"] = "Cắt ảnh",
    };

    /// <summary>All global hotkeys: id (matches the HotkeyService names), label, getter, setter.</summary>
    static readonly (string Name, string Label, Func<AppSettings, string> Get, Action<AppSettings, string> Set)[] Hk =
    {
        ("RegionShot", "Chụp vùng / tự nhận khung", s => s.HotkeyRegionShot, (s, v) => s.HotkeyRegionShot = v),
        ("FullShot", "Chụp toàn màn hình", s => s.HotkeyFullShot, (s, v) => s.HotkeyFullShot = v),
        ("WindowShot", "Chụp cửa sổ đang focus", s => s.HotkeyWindowShot, (s, v) => s.HotkeyWindowShot = v),
        ("DelayShot", "Chụp trễ (đếm ngược)", s => s.HotkeyDelayShot, (s, v) => s.HotkeyDelayShot = v),
        ("CopyRegion", "Chụp vùng và copy vào clipboard", s => s.HotkeyCopyRegion, (s, v) => s.HotkeyCopyRegion = v),
        ("PinShot", "Chụp vùng và ghim lên màn hình", s => s.HotkeyPinShot, (s, v) => s.HotkeyPinShot = v),
        ("Scroll", "Chụp cuộn trang", s => s.HotkeyScroll, (s, v) => s.HotkeyScroll = v),
        ("Record", "Quay video (bắt đầu / dừng)", s => s.HotkeyRecord, (s, v) => s.HotkeyRecord = v),
        ("Gif", "Quay GIF (tối đa 60 giây)", s => s.HotkeyGif, (s, v) => s.HotkeyGif = v),
        ("Pause", "Tạm dừng / tiếp tục khi đang quay", s => s.HotkeyPause, (s, v) => s.HotkeyPause = v),
        ("Library", "Mở ứng dụng / thư viện", s => s.HotkeyLibrary, (s, v) => s.HotkeyLibrary = v),
    };

    /// <param name="extraPage">Optional extra "Nâng cao" page (updates, AI upscaler) built by the host.</param>
    public SettingsView(AppSettings s, IReadOnlyList<HotkeyFailure> hotkeyFailures, Action<AppSettings> onSave, Action onRedetect, Action<AppTheme> onTheme,
        Func<AppSettings, Action<Action>, UIElement>? extraPage = null)
    {
        _s = s; _onSave = onSave; _onRedetect = onRedetect; _onTheme = onTheme;

        _theme.ItemsSource = new[] { "Tối", "Sáng", "Theo hệ thống" }; _theme.SelectedIndex = (int)s.Theme;
        _theme.SelectionChanged += (_, _) => { if (_theme.SelectedIndex >= 0) { _s.Theme = (AppTheme)_theme.SelectedIndex; _onTheme(_s.Theme); } };
        var qualities = new[] { "1080p (Full HD)", "1440p (2K)", "2160p (4K)" };
        _imgQuality.ItemsSource = qualities; _vidQuality.ItemsSource = qualities;
        _imgQuality.SelectedIndex = QIndex(s.Quality); _vidQuality.SelectedIndex = QIndex(s.VideoQuality);
        _imgFormat.ItemsSource = new[] { "PNG (không nén mất dữ liệu)", "WebP", "JPEG" }; _imgFormat.SelectedIndex = (int)s.ImageFormat;
        _delay.ItemsSource = new[] { "3 giây", "5 giây", "10 giây" }; _delay.SelectedIndex = s.DelaySeconds >= 10 ? 2 : s.DelaySeconds >= 5 ? 1 : 0;
        _mode.ItemsSource = new[] { "Tự động (theo cấu hình máy)", "Live (upscale khi quay)", "Nền (upscale sau khi quay)" }; _mode.SelectedIndex = (int)s.VideoMode;
        _fps.ItemsSource = new[] { "30", "60" }; _fps.SelectedIndex = s.Fps >= 60 ? 1 : 0;
        _gifFps.ItemsSource = new[] { "10 fps", "15 fps", "20 fps", "30 fps" }; _gifFps.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 10, 15, 20, 30 }, s.GifFps));
        _gifWidth.ItemsSource = new[] { "480 px", "720 px", "960 px", "Giữ nguyên kích thước" }; _gifWidth.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 480, 720, 960, 0 }, s.GifWidth));
        _retention.ItemsSource = new[] { "Không tự xóa", "Sau 7 ngày", "Sau 30 ngày", "Sau 90 ngày" };
        _retention.SelectedIndex = s.LibraryRetentionDays switch { >= 90 => 3, >= 30 => 2, >= 7 => 1, _ => 0 };
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone; _startup.IsChecked = s.StartWithWindows;
        _editAfter.IsChecked = s.OpenEditorAfterCapture; _autoCopy.IsChecked = s.AutoCopyAfterCapture;
        _clicks.IsChecked = s.ShowClicks; _keys.IsChecked = s.ShowKeys; _cam.IsChecked = s.Webcam;
        _dir.Text = s.OutputDir;
        foreach (var tool in EditorShortcuts.Tools)
            _editorKeys[tool] = new TextBox { Width = 38, MaxLength = 1, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold };
        foreach (var h in Hk) _hotkeys[h.Name] = new TextBox();
        LoadHotkeys(s);
        foreach (var f in hotkeyFailures)
            if (_hotkeys.TryGetValue(f.Name, out var box))
            {
                box.BorderBrush = Ui.Brush("Danger"); box.ToolTip = "Phím tắt này đang bị chương trình khác chiếm. Hãy chọn tổ hợp khác.";
                _error.Text += $"Phím tắt {f.Combo} bị chiếm. ";
            }

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        void Section(string icon, string name, Func<UIElement> build)
        {
            var b = new ToggleButton { Content = Ui.Content($"{icon} {name}"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(14, 9, 14, 9), Style = Ui.Style("NavButton") };
            b.Click += (_, _) => ShowSection(name);
            _sections.Add((name, b, build)); nav.Children.Add(b);
        }
        Section("\uE790", "Giao diện", BuildAppearance);
        Section("\uE91B", "Ảnh", BuildImage);
        Section("\uE714", "Video & âm thanh", BuildVideo);
        Section("\uE765", "Phím tắt", BuildShortcuts);
        Section("\uE8B7", "Lưu trữ", BuildStorage);
        if (extraPage is not null) Section("\uE90F", "Nâng cao", () => extraPage(_s, apply => _extraApply += apply));
        SetColumn(nav, 0); Children.Add(nav);

        var right = new DockPanel();
        var save = new Button { Content = "Lưu thay đổi", Style = Ui.Style("PrimaryButton"), Padding = new Thickness(20, 8, 20, 8), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) => { if (Apply()) { _onSave(_s); _error.Foreground = Ui.Brush("Accent"); _error.Text = "✓ Đã lưu"; } else _error.Foreground = Ui.Brush("Danger"); };
        _error.Margin = new Thickness(14, 0, 0, 0);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(save, Dock.Left); footer.Children.Add(save); footer.Children.Add(_error);
        DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        right.Children.Add(new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
        SetColumn(right, 1); Children.Add(right);

        ShowSection(_sections[0].Name);
    }

    public void ShowSection(string name)
    {
        foreach (var (n, btn, build) in _sections)
        {
            btn.IsChecked = n == name;
            if (n != name) continue;
            if (!_built.TryGetValue(n, out var el)) _built[n] = el = build();
            _page.Content = el;
        }
    }

    // ---------- pages ----------

    static StackPanel Page(string title, string subtitle)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        p.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text") });
        p.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Ui.Brush("TextDim"), Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });
        return p;
    }

    static void Field(Panel p, string label, UIElement c, string? note = null)
    {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Ui.Brush("TextDim") });
        p.Children.Add(c);
        if (note is not null) p.Children.Add(new TextBlock { Text = note, Foreground = Ui.Brush("Disabled"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
    }

    static void Check(Panel p, CheckBox c, double top = 8) { c.Margin = new Thickness(0, top, 0, 0); p.Children.Add(c); }

    UIElement BuildAppearance()
    {
        var p = Page("Giao diện", "Chủ đề màu và hành vi chung của ứng dụng.");
        Field(p, "Chế độ màu", _theme, "Đổi có hiệu lực ngay lập tức.");
        Check(p, _editAfter, 18);
        Check(p, _startup, 10);
        return p;
    }

    UIElement BuildImage()
    {
        var p = Page("Ảnh chụp", "Chất lượng, định dạng và hành vi sau khi chụp ảnh.");
        Field(p, "Chất lượng ảnh xuất", _imgQuality, "Phóng to ảnh nhỏ hơn sẽ làm mềm ảnh. Chọn 1080p nếu muốn giữ ảnh gần với màn hình gốc.");
        Field(p, "Định dạng ảnh", _imgFormat);
        Field(p, "Thời gian đếm ngược của \"Chụp trễ\"", _delay);
        Check(p, _autoCopy, 18);
        return p;
    }

    UIElement BuildVideo()
    {
        var p = Page("Video & âm thanh", "Cách quay màn hình.");
        Field(p, "Chất lượng video xuất", _vidQuality, "Máy yếu quay ở độ phân giải gốc rồi upscale ở nền.");
        Field(p, "Chế độ quay video", _mode);
        Field(p, "FPS", _fps);
        Check(p, _sys, 18); Check(p, _mic, 6);
        p.Children.Add(new TextBlock { Text = "Hiệu ứng mặc định khi quay", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 24, 0, 0) });
        Check(p, _clicks, 8); Check(p, _keys, 6); Check(p, _cam, 6);
        p.Children.Add(new TextBlock { Text = "Ảnh GIF (tối đa 60 giây, không có âm thanh)", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 24, 0, 0) });
        Field(p, "Số khung hình/giây", _gifFps, "Ít fps và chiều rộng nhỏ giúp file GIF nhẹ hơn.");
        Field(p, "Chiều rộng tối đa", _gifWidth);
        return p;
    }

    UIElement BuildStorage()
    {
        var p = Page("Lưu trữ", "Nơi lưu ảnh, video và GIF. Thư viện hiển thị nội dung của thư mục này.");
        var row = new DockPanel();
        var browse = new Button { Content = "Chọn…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 6, 14, 6) };
        browse.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _dir.Text, ShowNewFolderButton = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _dir.Text = dlg.SelectedPath;
        };
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(_dir);
        Field(p, "Thư mục lưu", row);
        Field(p, "Tự dọn file cũ", _retention, "File cũ hơn mốc này sẽ được chuyển vào thùng rác khi mở ứng dụng. File gắn sao không bao giờ bị xóa.");
        var redetect = new Button { Content = "Dò lại cấu hình máy", Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7) };
        redetect.Click += (_, _) => { _onRedetect(); _error.Foreground = Ui.Brush("Accent"); _error.Text = "Đang dò lại cấu hình ở nền…"; };
        p.Children.Add(redetect);
        p.Children.Add(new TextBlock { Text = "Dùng khi bạn đổi card đồ họa hoặc cài thêm ffmpeg.", Foreground = Ui.Brush("Disabled"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        return p;
    }

    UIElement BuildShortcuts()
    {
        var p = Page("Phím tắt", "Phím tắt toàn cục hoạt động ở mọi nơi. Định dạng: Ctrl+Alt+S, Alt+F1, ...");
        foreach (var h in Hk) Field(p, h.Label, _hotkeys[h.Name]);

        p.Children.Add(new TextBlock { Text = "Công cụ trình chỉnh sửa ảnh", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 24, 0, 0) });
        p.Children.Add(new TextBlock { Text = "Mỗi công cụ một chữ cái A-Z (không dùng C, đã dành cho màu tùy chọn).", Foreground = Ui.Brush("Disabled"), FontSize = 12, Margin = new Thickness(0, 3, 0, 8) });
        var grid = new WrapPanel();
        foreach (var tool in EditorShortcuts.Tools)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Width = 160, Margin = new Thickness(0, 0, 0, 6) };
            item.Children.Add(_editorKeys[tool]);
            item.Children.Add(new TextBlock { Text = ToolLabels[tool], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = Ui.Brush("Text") });
            grid.Children.Add(item);
        }
        p.Children.Add(grid);
        var reset = new Button { Content = "Khôi phục phím tắt mặc định", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => LoadHotkeys(new AppSettings());
        p.Children.Add(reset);
        p.Children.Add(BuildGuide());
        return p;
    }

    void LoadHotkeys(AppSettings s)
    {
        foreach (var h in Hk) _hotkeys[h.Name].Text = h.Get(s);
        var keys = EditorShortcuts.Resolve(s.EditorKeys);
        foreach (var (tool, box) in _editorKeys) box.Text = keys[tool];
    }

    /// <summary>Read-only cheat sheet of every shortcut (values as saved).</summary>
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
                    Padding = new Thickness(6, 1, 6, 1), Width = 136, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
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
        Group("Toàn cục", Hk.Select(h => (h.Get(_s), h.Label)).Append(("Esc", "Hủy khi đang chọn vùng")).ToArray());
        Group("Trình chỉnh sửa ảnh",
            ($"{K("Pen")} {K("Highlighter")} {K("Line")} {K("Arrow")}", "Bút · tô sáng · đường thẳng · mũi tên"),
            ($"{K("Rect")} {K("Ellipse")} {K("Text")} {K("Number")} {K("Mosaic")} {K("Crop")}", "Chữ nhật · elip · chữ · đánh số · làm mờ · cắt ảnh"),
            ("1 - 9 / C", "Chọn màu có sẵn / màu tùy chọn"),
            ("[   ]", "Giảm / tăng độ dày nét"),
            ("Shift (khi vẽ)", "Giữ tỉ lệ: hình vuông, hình tròn, góc 45°"),
            ("Ctrl+Z / Ctrl+Y", "Hoàn tác / làm lại (Delete cũng hoàn tác)"),
            ("Ctrl+Shift+Z", "Xóa hết chú thích"),
            ("Ctrl+L / Ctrl+R", "Xoay ảnh trái / phải"),
            ("Ctrl+S / Ctrl+C", "Lưu file *_edit.png / copy ảnh"),
            ("Esc", "Đóng trình chỉnh sửa"));
        Group("Ghim ảnh", ("Kéo / lăn chuột", "Di chuyển / phóng thu"), ("Alt + lăn chuột", "Đổi độ mờ"), ("Nhấp đúp / Esc", "Đóng"));
        Group("Thư viện", ("F5", "Làm mới"), ("Click thẻ", "Mở file"), ("Kéo thẻ", "Kéo file sang ứng dụng khác"));
        return new Border
        {
            Margin = new Thickness(0, 22, 0, 8), Padding = new Thickness(14), CornerRadius = new CornerRadius(12),
            Background = Ui.Brush("Surface"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), Child = g,
        };
    }

    // ---------- apply ----------

    static int QIndex(ExportQuality q) => q switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };
    static ExportQuality QFrom(int i) => i switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };

    bool Apply()
    {
        foreach (var h in Hk)
        {
            try { HotkeyParser.Parse(_hotkeys[h.Name].Text); }
            catch (FormatException) { _error.Text = $"Phím tắt '{h.Label}' không hợp lệ (ví dụ: Ctrl+Alt+S)."; ShowSection("Phím tắt"); return false; }
        }
        var combos = Hk.Select(h => _hotkeys[h.Name].Text.Trim().ToUpperInvariant().Replace(" ", "")).ToList();
        if (combos.Distinct().Count() != combos.Count) { _error.Text = "Hai phím tắt toàn cục đang trùng nhau."; ShowSection("Phím tắt"); return false; }
        var editorKeys = _editorKeys.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim().ToUpperInvariant());
        if (EditorShortcuts.Validate(editorKeys) is { } err) { _error.Text = err; ShowSection("Phím tắt"); return false; }
        _error.Text = "";

        _s.Quality = QFrom(_imgQuality.SelectedIndex); _s.VideoQuality = QFrom(_vidQuality.SelectedIndex);
        _s.ImageFormat = (ImageFormat)_imgFormat.SelectedIndex;
        _s.VideoMode = (VideoMode)_mode.SelectedIndex;
        _s.Fps = _fps.SelectedIndex == 1 ? 60 : 30;
        _s.DelaySeconds = _delay.SelectedIndex switch { 2 => 10, 1 => 5, _ => 3 };
        _s.GifFps = new[] { 10, 15, 20, 30 }[Math.Max(0, _gifFps.SelectedIndex)];
        _s.GifWidth = new[] { 480, 720, 960, 0 }[Math.Max(0, _gifWidth.SelectedIndex)];
        _s.LibraryRetentionDays = _retention.SelectedIndex switch { 3 => 90, 2 => 30, 1 => 7, _ => 0 };
        _s.CaptureSystemAudio = _sys.IsChecked == true; _s.CaptureMicrophone = _mic.IsChecked == true;
        _s.ShowClicks = _clicks.IsChecked == true; _s.ShowKeys = _keys.IsChecked == true; _s.Webcam = _cam.IsChecked == true;
        _s.StartWithWindows = _startup.IsChecked == true; _s.OpenEditorAfterCapture = _editAfter.IsChecked == true; _s.AutoCopyAfterCapture = _autoCopy.IsChecked == true;
        _s.Theme = (AppTheme)Math.Max(0, _theme.SelectedIndex);
        _s.OutputDir = string.IsNullOrWhiteSpace(_dir.Text) ? _s.OutputDir : _dir.Text.Trim();
        foreach (var h in Hk) h.Set(_s, _hotkeys[h.Name].Text.Trim());
        _s.EditorKeys = editorKeys;
        _extraApply?.Invoke();
        return true;
    }
}
