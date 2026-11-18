using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using HezbClips.Core.Settings;

namespace HezbClips.Linux.Windows;

public class SettingsWindow : Window
{
    public event Action? Saved;

    public SettingsWindow(AppSettings s)
    {
        Title = "Hezb Clips - Cài đặt";
        Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var quality = new ComboBox { ItemsSource = new[] { "1080p", "1440p (2K)", "2160p (4K)" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        quality.SelectedIndex = s.Quality switch { ExportQuality.P1440 => 1, ExportQuality.P2160 => 2, _ => 0 };
        var format = new ComboBox { ItemsSource = new[] { "PNG", "WebP", "JPEG" }, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = (int)s.ImageFormat };
        var dir = new TextBox { Text = s.OutputDir };
        var autoCopy = new CheckBox { Content = "Tự copy ảnh sau khi chụp", IsChecked = s.AutoCopyAfterCapture };
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed };

        var save = new Button { Content = "Lưu", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(dir.Text)) { error.Text = "Thư mục lưu trống"; return; }
            s.Quality = quality.SelectedIndex switch { 1 => ExportQuality.P1440, 2 => ExportQuality.P2160, _ => ExportQuality.P1080 };
            s.ImageFormat = (ImageFormat)format.SelectedIndex;
            s.OutputDir = dir.Text.Trim();
            s.AutoCopyAfterCapture = autoCopy.IsChecked == true;
            Saved?.Invoke();
            Close();
        };
        var cancel = new Button { Content = "Huỷ", IsCancel = true }; cancel.Click += (_, _) => Close();

        static Control Row(string label, Control c) => new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = .8 }, c } };

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 10,
            Children =
            {
                Row("Chất lượng xuất ảnh", quality),
                new TextBlock { Text = "Upscale làm ảnh nét và mượt hơn, không thêm chi tiết gốc.", Opacity = .6, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Row("Định dạng ảnh", format),
                Row("Thư mục lưu", dir),
                autoCopy,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } },
            },
        };
    }
}
