using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HezbClips.Core.Settings;
using HezbClips.Core.Tools;

namespace HezbClips.App.Windows;

/// <summary>What the "Nâng cao" settings page can ask the app to do.</summary>
public sealed record AdvancedHost(
    Func<string?> EsrganPath,
    Func<IProgress<double>, Task> InstallEsrgan,
    Func<string, Task<UpdateInfo?>> CheckUpdate,
    Func<UpdateInfo, IProgress<double>, Task<string>> DownloadUpdate,
    string CurrentVersion);

/// <summary>Settings page: AI upscaler download and app updates.</summary>
public static class AdvancedPage
{
    /// <param name="onApply">Registers what to copy into the settings when the user presses "Lưu thay đổi".</param>
    public static UIElement Build(AppSettings s, Action<Action> onApply, AdvancedHost host)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        TextBlock T(string text, double size, bool bold = false, bool dim = false, double top = 0) => new()
        {
            Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            Foreground = Ui.Brush(dim ? "TextDim" : "Text"), Margin = new Thickness(0, top, 0, 0),
        };
        p.Children.Add(T("Nâng cao", 20, bold: true));
        p.Children.Add(T("Công cụ tùy chọn và cập nhật ứng dụng.", 12, dim: true, top: 2));

        // ---- AI upscaler ----
        p.Children.Add(T("Upscale bằng AI (Real-ESRGAN)", 15, bold: true, top: 22));
        var esrgan = T("", 13, top: 8);
        void RefreshEsrgan()
        {
            var path = host.EsrganPath();
            esrgan.Text = path is null ? "Chưa cài. Ứng dụng đang dùng Lanczos (nhanh, không cần cài thêm)." : "✓ Đã cài: " + path;
        }
        RefreshEsrgan();
        p.Children.Add(esrgan);
        p.Children.Add(T("Tải về khoảng 40 MB từ github.com/xinntao/Real-ESRGAN rồi giải nén vào thư mục dữ liệu của ứng dụng. Cần card đồ họa hỗ trợ Vulkan, nếu không app tự dùng Lanczos.", 12, dim: true, top: 6));
        var install = new Button { Content = "⬇ Tải và cài đặt Real-ESRGAN", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7) };
        var bar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; bar.Visibility = Visibility.Visible; bar.Value = 0; esrgan.Text = "Đang tải…";
            try
            {
                await host.InstallEsrgan(new Progress<double>(v => bar.Value = v));
                RefreshEsrgan();
            }
            catch (Exception ex) { esrgan.Text = "Không cài được: " + ex.Message; }
            finally { install.IsEnabled = true; bar.Visibility = Visibility.Collapsed; }
        };
        p.Children.Add(install); p.Children.Add(bar);

        // ---- updates ----
        p.Children.Add(T("Cập nhật", 15, bold: true, top: 28));
        p.Children.Add(T($"Phiên bản hiện tại: {host.CurrentVersion}", 13, top: 8));
        p.Children.Add(T("Địa chỉ feed cập nhật (https)", 12, dim: true, top: 12));
        var feed = new TextBox { Text = s.UpdateFeedUrl, Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(feed);
        p.Children.Add(T("Feed là một file JSON dạng {\"version\":\"1.2.0\",\"url\":\"https://…/HezbClips.zip\",\"notes\":\"…\",\"sha256\":\"tùy chọn\"}. Nhớ bấm \"Lưu thay đổi\" để giữ địa chỉ.", 12, dim: true, top: 6));

        var auto = new CheckBox { Content = "Tự kiểm tra khi mở ứng dụng", IsChecked = s.AutoCheckUpdates, Margin = new Thickness(0, 10, 0, 0) };
        p.Children.Add(auto);
        // nothing here touches the settings until "Lưu thay đổi"
        onApply(() => { s.UpdateFeedUrl = feed.Text.Trim(); s.AutoCheckUpdates = auto.IsChecked == true; });
        var status = T("", 13, top: 10);
        var check = new Button { Content = "Kiểm tra cập nhật", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7) };
        var download = new Button { Content = "Tải bản mới", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7), Style = Ui.Style("PrimaryButton"), Visibility = Visibility.Collapsed };
        var dlBar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        UpdateInfo? found = null;
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false; download.Visibility = Visibility.Collapsed; status.Text = "Đang kiểm tra…";
            try
            {
                found = await host.CheckUpdate(feed.Text.Trim());
                if (found is null) status.Text = "✓ Bạn đang dùng bản mới nhất.";
                else { status.Text = $"Có bản mới {found.Version}. {found.Notes}"; download.Content = $"Tải bản {found.Version}"; download.Visibility = Visibility.Visible; }
            }
            catch (Exception ex) { status.Text = "Không kiểm tra được: " + ex.Message; }
            finally { check.IsEnabled = true; }
        };
        download.Click += async (_, _) =>
        {
            if (found is null) return;
            download.IsEnabled = false; dlBar.Visibility = Visibility.Visible; dlBar.Value = 0;
            try
            {
                var file = await host.DownloadUpdate(found, new Progress<double>(v => dlBar.Value = v));
                status.Text = "✓ Đã tải xong: " + file;
                var runnable = Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".msi";
                if (runnable && string.IsNullOrWhiteSpace(found.Sha256))
                {
                    // without a checksum we cannot tell the file is the one the feed meant: never run it automatically
                    status.Text = "Đã tải: " + file + ". Feed không có mã SHA-256 nên ứng dụng không tự chạy file này; hãy kiểm tra trước khi mở.";
                    Process.Start("explorer.exe", $"/select,\"{file}\"");
                }
                else if (runnable)
                {
                    if (MessageBox.Show(Window.GetWindow(p), "Chạy trình cài đặt vừa tải ngay bây giờ?\nỨng dụng sẽ đóng để cập nhật.", "Hezb Clips", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); Application.Current.Shutdown(); }
                }
                else Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
            catch (Exception ex) { status.Text = "Không tải được: " + ex.Message; }
            finally { download.IsEnabled = true; dlBar.Visibility = Visibility.Collapsed; }
        };
        p.Children.Add(check); p.Children.Add(status); p.Children.Add(download); p.Children.Add(dlBar);
        return p;
    }
}
