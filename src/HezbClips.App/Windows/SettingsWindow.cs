using System;
using System.Collections.Generic;
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
    readonly TextBox _dir = new(), _hkRegion = new(), _hkFull = new(), _hkRecord = new();
    readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public SettingsWindow(AppSettings s, IReadOnlyList<HotkeyFailure> hotkeyFailures, Action<AppSettings> onSave, Action onRedetect)
    {
        _s = s;
        Title = "Hezb Clips — Cài đặt"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _quality.ItemsSource = new[] { "1080p (Full HD)", "1440p (2K)", "2160p (4K)" };
        _quality.SelectedIndex = s.Quality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };
        _imageFmt.ItemsSource = new[] { "PNG (không nén mất dữ liệu)", "WebP", "JPEG" }; _imageFmt.SelectedIndex = (int)s.ImageFormat;
        _mode.ItemsSource = new[] { "Tự động (theo cấu hình máy)", "Live (upscale khi quay)", "Nền (upscale sau khi quay)" }; _mode.SelectedIndex = (int)s.VideoMode;
        _fps.ItemsSource = new[] { "30", "60" }; _fps.SelectedIndex = s.Fps >= 60 ? 1 : 0;
        _sys.IsChecked = s.CaptureSystemAudio; _mic.IsChecked = s.CaptureMicrophone; _startup.IsChecked = s.StartWithWindows;
        _dir.Text = s.OutputDir; _hkRegion.Text = s.HotkeyRegionShot; _hkFull.Text = s.HotkeyFullShot; _hkRecord.Text = s.HotkeyRecord;
        foreach (var f in hotkeyFailures)
        {
            var box = f.Name switch { "RegionShot" => _hkRegion, "FullShot" => _hkFull, _ => _hkRecord };
            box.BorderBrush = Brushes.Firebrick; box.ToolTip = "Phím tắt này đang bị chương trình khác chiếm. Hãy chọn tổ hợp khác.";
            _error.Text += $"Phím tắt {f.Combo} bị chiếm. ";
        }

        var panel = new StackPanel { Margin = new Thickness(16) };
        void Row(string label, UIElement c) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2), FontWeight = FontWeights.SemiBold }); panel.Children.Add(c); }
        Row("Chất lượng xuất", _quality);
        panel.Children.Add(new TextBlock
        {
            Text = "Upscale làm ảnh nét và mượt hơn, không thêm chi tiết gốc.",
            Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
        });
        Row("Định dạng ảnh", _imageFmt);
        Row("Chế độ quay video", _mode);
        Row("FPS", _fps);
        panel.Children.Add(_sys); panel.Children.Add(_mic);
        _sys.Margin = new Thickness(0, 10, 0, 2);
        Row("Thư mục lưu", _dir);
        Row("Phím tắt: chụp vùng", _hkRegion);
        Row("Phím tắt: chụp toàn màn hình", _hkFull);
        Row("Phím tắt: bắt đầu/dừng quay", _hkRecord);
        panel.Children.Add(_startup); _startup.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_error);

        var redetect = new Button { Content = "Dò lại cấu hình máy", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 12, 8, 0) };
        redetect.Click += (_, _) => { onRedetect(); MessageBox.Show(this, "Đang dò lại cấu hình ở nền.", "Hezb Clips"); };
        var save = new Button { Content = "Lưu", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "Đóng", IsCancel = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 12, 0, 0) };
        save.Click += (_, _) => { if (Apply()) { onSave(_s); Close(); } };
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(redetect); bar.Children.Add(save); bar.Children.Add(cancel);
        panel.Children.Add(bar);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    bool Apply()
    {
        foreach (var (box, name) in new[] { (_hkRegion, "chụp vùng"), (_hkFull, "chụp toàn màn hình"), (_hkRecord, "quay") })
        {
            try { HotkeyParser.Parse(box.Text); }
            catch (FormatException) { _error.Text = $"Phím tắt '{name}' không hợp lệ (ví dụ: Ctrl+Shift+S)."; return false; }
        }
        _s.Quality = _quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
        _s.ImageFormat = (ImageFormat)_imageFmt.SelectedIndex;
        _s.VideoMode = (VideoMode)_mode.SelectedIndex;
        _s.Fps = _fps.SelectedIndex == 1 ? 60 : 30;
        _s.CaptureSystemAudio = _sys.IsChecked == true; _s.CaptureMicrophone = _mic.IsChecked == true;
        _s.StartWithWindows = _startup.IsChecked == true;
        _s.OutputDir = string.IsNullOrWhiteSpace(_dir.Text) ? _s.OutputDir : _dir.Text.Trim();
        _s.HotkeyRegionShot = _hkRegion.Text.Trim(); _s.HotkeyFullShot = _hkFull.Text.Trim(); _s.HotkeyRecord = _hkRecord.Text.Trim();
        return true;
    }
}
