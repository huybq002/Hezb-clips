using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace HezbClips.Linux.Windows;

public class PostCapturePopup : Window
{
    readonly string _path;
    readonly TextBlock _status = new() { Opacity = .7, Margin = new Thickness(0, 2, 0, 8) };
    readonly DispatcherTimer _auto = new() { Interval = TimeSpan.FromSeconds(8) };

    public PostCapturePopup(string path, bool copied, bool isVideo = false)
    {
        _path = path;
        Title = "Hezb Clips";
        SystemDecorations = SystemDecorations.BorderOnly;
        Topmost = true; ShowInTaskbar = false; CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        _status.Text = copied ? "Đã lưu và copy" : "Đã lưu";

        var copy = new Button { Content = "Copy", IsVisible = !isVideo }; copy.Click += (_, _) => Copy();
        var open = new Button { Content = "Mở thư mục" }; open.Click += (_, _) => ClipboardTool.OpenFolder(_path);
        var del = new Button { Content = "Xóa" }; del.Click += (_, _) => Delete();
        var close = new Button { Content = "✕" }; close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(12), MinWidth = 320,
            Children =
            {
                new TextBlock { Text = Path.GetFileName(path), FontWeight = Avalonia.Media.FontWeight.SemiBold, MaxWidth = 380, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                _status,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { copy, open, del, close } },
            },
        };

        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                Position = new PixelPoint(wa.Right - (int)(Bounds.Width * screen.Scaling) - 16,
                                          wa.Bottom - (int)(Bounds.Height * screen.Scaling) - 16);
            }
        };
        _auto.Tick += (_, _) => Close();
        _auto.Start();
        PointerEntered += (_, _) => _auto.Stop();
        Closed += (_, _) => _auto.Stop();
    }

    void Copy() => _status.Text = ClipboardTool.CopyImage(_path)
        ? "Đã copy" : "Thiếu wl-clipboard hoặc xclip để copy ảnh";

    void Delete()
    {
        try { File.Delete(_path); Close(); }
        catch (IOException ex) { _status.Text = "Xóa lỗi: " + ex.Message; }
    }
}
