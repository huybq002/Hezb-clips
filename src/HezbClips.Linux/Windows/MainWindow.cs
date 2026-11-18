using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HezbClips.Linux.Windows;

/// <summary>Small launcher so the app is usable even when the desktop has no tray.</summary>
public class MainWindow : Window
{
    readonly TextBlock _hk = new() { Opacity = .7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    readonly Button _install = new() { Content = "Cài phím tắt GNOME", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };

    public MainWindow(Action region, Action full, Action folder, Action settings, Action installShortcuts)
    {
        _install.Click += (_, _) => installShortcuts();
        Title = "Hezb Clips";
        Width = 280; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        static Button B(string t, Action a) { var b = new Button { Content = t, HorizontalAlignment = HorizontalAlignment.Stretch }; b.Click += (_, _) => a(); return b; }
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children = { B("Chụp vùng", region), B("Chụp toàn màn hình", full), B("Mở thư mục ảnh", folder), B("Cài đặt", settings), _hk, _install },
        };
    }

    public void SetShortcutStatus(string text, bool showInstall)
    {
        _hk.Text = text; _hk.IsVisible = text.Length > 0;
        _install.IsVisible = showInstall;
    }
}
