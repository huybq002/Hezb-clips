using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HezbClips.Core.Output;
using Microsoft.VisualBasic.FileIO;

namespace HezbClips.App.Windows;

/// <summary>Gallery of everything captured: images, videos, GIFs. Click a card to open it;
/// card buttons: open, show in folder, copy, edit (images), delete (recycle bin).</summary>
public sealed class LibraryView : DockPanel
{
    readonly string _thumbCache;
    readonly string? _ffmpeg;
    readonly Action<string>? _edit;
    readonly WrapPanel _grid = new() { Margin = new Thickness(-6, 4, 0, 12) };
    readonly TextBlock _summary = new() { Foreground = Ui.Brush("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _empty = new() { Text = "Chưa có gì ở đây.\nChụp hoặc quay để bắt đầu.", Foreground = Ui.Brush("TextDim"), FontSize = 14, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 80, 0, 0), Visibility = Visibility.Collapsed };
    readonly List<ToggleButton> _chips = new();
    MediaKind? _filter;
    int _version;

    string _dirValue;
    string _dir => _dirValue;

    /// <summary>Point the gallery at another folder (after Settings changed it).</summary>
    public void SetFolder(string dir) { _dirValue = dir; _grid.Tag = null; Reload(); }

    public LibraryView(string outputDir, string? ffmpeg, string dataDir, Action<string>? edit)
    {
        _dirValue = outputDir; _ffmpeg = ffmpeg; _thumbCache = Path.Combine(dataDir, "thumbs"); _edit = edit;

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var openDir = new Button { Content = "📂 Mở thư mục", Margin = new Thickness(8, 0, 0, 0) };
        openDir.Click += (_, _) => { Directory.CreateDirectory(_dir); Process.Start("explorer.exe", $"\"{_dir}\""); };
        var refresh = new Button { Content = "⟳", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6), ToolTip = "Làm mới (F5)" };
        refresh.Click += (_, _) => { _grid.Tag = null; Reload(); };
        right.Children.Add(_summary); right.Children.Add(openDir); right.Children.Add(refresh);
        DockPanel.SetDock(right, Dock.Right);
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        void Chip(string text, MediaKind? kind)
        {
            var c = new ToggleButton { Content = text, Margin = new Thickness(0, 0, 6, 0), IsChecked = kind is null };
            c.Click += (_, _) => { _filter = kind; foreach (var o in _chips) o.IsChecked = o == c; Reload(); };
            _chips.Add(c); chips.Children.Add(c);
        }
        Chip("Tất cả", null); Chip("🖼 Ảnh", MediaKind.Image); Chip("🎞 Video", MediaKind.Video); Chip("GIF", MediaKind.Gif);
        bar.Children.Add(right); bar.Children.Add(chips);

        var body = new Grid();
        body.Children.Add(new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(_empty);
        SetDock(bar, Dock.Top); Children.Add(bar); Children.Add(body);
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        var items = LibraryScanner.Scan(_dir);
        var shown = _filter is null ? items : items.Where(i => i.Kind == _filter).ToList();
        _summary.Text = $"{items.Count} mục · {FormatSize(items.Sum(i => i.Size))}";
        _empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sig = _filter + ":" + string.Join('|', shown.Select(i => i.Path + i.Modified.Ticks));
        if (_grid.Tag as string == sig) return; // Activated fires often: keep cards when nothing changed
        _grid.Tag = sig; _grid.Children.Clear(); _version++;
        foreach (var item in shown) _grid.Children.Add(Card(item, _version));
    }

    UIElement Card(MediaItem item, int version)
    {
        var img = new Image { Stretch = Stretch.UniformToFill, Height = 140 };
        var thumbBox = new Grid { Height = 140, Background = Ui.Brush("Surface"), ClipToBounds = true };
        thumbBox.Children.Add(new TextBlock { Text = item.Kind == MediaKind.Video ? "🎞" : "🖼", FontSize = 30, Foreground = Ui.Brush("Disabled"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        thumbBox.Children.Add(img);
        var badge = item.Kind switch { MediaKind.Video => "VIDEO", MediaKind.Gif => "GIF", _ => "" };
        if (badge.Length > 0)
            thumbBox.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 27, 28, 34)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = badge, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold },
            });
        thumbBox.Clip = new RectangleGeometry(new Rect(0, 0, 204, 140), 8, 8);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        void A(string t, string tip, Action a)
        {
            var b = new Button { Content = t, ToolTip = tip, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
            b.Click += (_, e) => { a(); e.Handled = true; };
            b.MouseLeftButtonUp += (_, e) => e.Handled = true; // do not trigger card open
            actions.Children.Add(b);
        }
        A("Mở", "Mở file", () => Open(item.Path));
        A("📂", "Hiện trong thư mục", () => Process.Start("explorer.exe", $"/select,\"{item.Path}\""));
        A("📋", "Copy", () => CopyItem(item));
        if (item.Kind == MediaKind.Image && _edit is not null) A("✏", "Chỉnh sửa", () => _edit(item.Path));
        A("🗑", "Xóa (vào thùng rác)", () => Delete(item));

        var col = new StackPanel();
        col.Children.Add(thumbBox);
        col.Children.Add(new TextBlock { Text = Path.GetFileName(item.Path), Foreground = Ui.Brush("Text"), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) });
        col.Children.Add(new TextBlock { Text = $"{item.Modified:dd/MM/yyyy HH:mm} · {FormatSize(item.Size)}", Foreground = Ui.Brush("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        col.Children.Add(actions);

        var card = new Border
        {
            Width = 220, Margin = new Thickness(6), Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            Background = Ui.Brush("Bg"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), Child = col,
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Brush("Accent");
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Brush("Border");
        card.MouseLeftButtonUp += (_, _) => Open(item.Path);

        _ = LoadThumb(item, img, version);
        return card;
    }

    async Task LoadThumb(MediaItem item, Image img, int version)
    {
        var src = await Thumbnails.LoadAsync(item, _ffmpeg, _thumbCache);
        if (src is not null && version == _version) img.Source = src;
    }

    static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { /* no associated app */ }
    }

    static void CopyItem(MediaItem item)
    {
        try
        {
            if (item.Kind == MediaKind.Image && Path.GetExtension(item.Path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(item.Path); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit();
                Clipboard.SetImage(bmp);
            }
            else Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { item.Path });
        }
        catch (Exception) { /* clipboard busy */ }
    }

    void Delete(MediaItem item)
    {
        try { FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
        catch (Exception) { return; }
        _grid.Tag = null; Reload();
    }

    static string FormatSize(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / (double)(1L << 10):0} KB",
        _ => $"{b} B",
    };
}
