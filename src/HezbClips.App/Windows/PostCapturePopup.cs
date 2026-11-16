using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HezbClips.App.Windows;

/// <summary>Bottom-right popup after a capture: copy, open folder, delete, and (video) trim head/tail.
/// Closes itself after 8 seconds unless the mouse is over it.</summary>
public sealed class PostCapturePopup : Window
{
    readonly DispatcherTimer _auto = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly TextBlock _title = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 };
    readonly TextBlock _note = new() { Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, Margin = new Thickness(0, 2, 0, 0) };
    readonly StackPanel _trimPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBox _from = new() { Width = 60, Text = "00:00" };
    readonly TextBox _to = new() { Width = 60, Text = "00:10", Margin = new Thickness(6, 0, 6, 0) };
    string _path;
    readonly bool _video;

    /// <param name="trim">Called with (path, from, to); returns the trimmed file path.</param>
    public PostCapturePopup(string path, bool isVideo, string? note, Func<string, TimeSpan, TimeSpan, string?> trim)
    {
        _path = path; _video = isVideo;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = false;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 36));
        _title.Text = Path.GetFileName(path);
        _note.Text = note ?? ""; _note.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Content = t, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) }; b.Click += (_, _) => a(); buttons.Children.Add(b); return b; }
        B("Copy", Copy);
        B("Mở thư mục", () => Process.Start("explorer.exe", $"/select,\"{_path}\""));
        B("Xóa", () => { try { File.Delete(_path); } catch (IOException) { } Close(); });
        if (isVideo) B("Cắt", () => _trimPanel.Visibility = Visibility.Visible);

        var apply = new Button { Content = "Cắt ngay", Padding = new Thickness(8, 3, 8, 3) };
        apply.Click += (_, _) =>
        {
            if (TimeSpan.TryParse("00:" + _from.Text, out var a) && TimeSpan.TryParse("00:" + _to.Text, out var b) && b > a)
            {
                var res = trim(_path, a, b);
                if (res is not null) { _path = res; _title.Text = Path.GetFileName(res); _trimPanel.Visibility = Visibility.Collapsed; }
            }
        };
        _trimPanel.Children.Add(new TextBlock { Text = "từ", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _trimPanel.Children.Add(_from);
        _trimPanel.Children.Add(new TextBlock { Text = "đến", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        _trimPanel.Children.Add(_to); _trimPanel.Children.Add(apply);

        var col = new StackPanel { Margin = new Thickness(10) };
        var thumb = Thumbnail(path, isVideo);
        if (thumb is not null) col.Children.Add(thumb);
        col.Children.Add(_title); col.Children.Add(_note); col.Children.Add(buttons); col.Children.Add(_trimPanel);
        Content = new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), Child = col };

        _auto.Tick += (_, _) => { if (!IsMouseOver && _trimPanel.Visibility != Visibility.Visible) Close(); };
        MouseEnter += (_, _) => _auto.Stop();
        MouseLeave += (_, _) => { _auto.Stop(); _auto.Start(); };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16; Top = wa.Bottom - ActualHeight - 16;
            _auto.Start();
        };
        Closed += (_, _) => _auto.Stop();
    }

    static UIElement? Thumbnail(string path, bool isVideo)
    {
        if (isVideo) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.DecodePixelWidth = 320; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Width = 320, Margin = new Thickness(0, 0, 0, 6) };
        }
        catch (Exception) { return null; } // e.g. WebP is not decodable by WPF: show no thumbnail
    }

    void Copy()
    {
        try
        {
            if (!_video && Path.GetExtension(_path).ToLowerInvariant() is ".png" or ".jpg")
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(_path); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                Clipboard.SetImage(bmp);
            }
            else
            {
                var list = new System.Collections.Specialized.StringCollection { _path };
                Clipboard.SetFileDropList(list);
            }
        }
        catch (Exception) { /* clipboard busy: ignore */ }
    }
}
