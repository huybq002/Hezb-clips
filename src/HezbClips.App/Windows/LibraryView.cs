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
    readonly LibraryMetaStore _meta;
    readonly TextBox _search = new() { Width = 200, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Tìm theo tên file hoặc #thẻ" };
    readonly TextBlock _searchHint = new() { Text = "Tìm tên hoặc #thẻ", Foreground = Ui.Brush("Disabled"), FontSize = 13, IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    bool _starOnly;
    readonly string? _ffmpeg;
    readonly Action<string>? _edit, _pin;
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

    public LibraryView(string outputDir, string? ffmpeg, string dataDir, Action<string>? edit, Action<string>? pin = null)
    {
        _dirValue = outputDir; _ffmpeg = ffmpeg; _thumbCache = Path.Combine(dataDir, "thumbs"); _edit = edit; _pin = pin;
        _meta = new LibraryMetaStore(Path.Combine(dataDir, "library.json"));

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var openDir = new Button { Content = Ui.Content("\uE838 Mở thư mục"), Margin = new Thickness(8, 0, 0, 0) };
        openDir.Click += (_, _) => { Directory.CreateDirectory(_dir); Process.Start("explorer.exe", $"\"{_dir}\""); };
        var refresh = new Button { Content = Ui.Glyph("\uE72C"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6), ToolTip = "Làm mới (F5)" };
        refresh.Click += (_, _) => { _grid.Tag = null; Reload(); };
        right.Children.Add(_summary); right.Children.Add(openDir); right.Children.Add(refresh);
        DockPanel.SetDock(right, Dock.Right);
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        void Chip(string text, MediaKind? kind)
        {
            var c = new ToggleButton { Content = Ui.Content(text), Margin = new Thickness(0, 0, 6, 0), IsChecked = kind is null };
            c.Click += (_, _) => { _filter = kind; foreach (var o in _chips) o.IsChecked = o == c; Reload(); };
            _chips.Add(c); chips.Children.Add(c);
        }
        Chip("Tất cả", null); Chip("\uE91B Ảnh", MediaKind.Image); Chip("\uE714 Video", MediaKind.Video); Chip("GIF", MediaKind.Gif);
        var starChip = new ToggleButton { Content = Ui.Content("\uE734 Gắn sao"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Chỉ hiện file đã gắn sao" };
        starChip.Click += (_, _) => { _starOnly = starChip.IsChecked == true; Reload(); };
        chips.Children.Add(starChip);
        var searchBox = new Grid(); searchBox.Children.Add(_search); searchBox.Children.Add(_searchHint);
        _search.TextChanged += (_, _) => { _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Reload(); };
        chips.Children.Add(searchBox);
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
        var shown = LibraryQuery.Filter(items, _meta, _search.Text, _starOnly, _filter);
        _summary.Text = shown.Count == items.Count ? $"{items.Count} mục · {FormatSize(items.Sum(i => i.Size))}" : $"{shown.Count}/{items.Count} mục";
        _empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sig = _filter + ":" + _starOnly + ":" + _search.Text + ":" + string.Join('|', shown.Select(i => i.Path + i.Modified.Ticks));
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
        if (item.Kind == MediaKind.Gif) thumbBox.Children.Add(Ui.AnimatedGif(item.Path, 140));
        if (item.Kind == MediaKind.Video)
            thumbBox.Children.Add(new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                Child = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection { new Point(0, 0), new Point(0, 16), new Point(14, 8) }, Fill = Brushes.White,
                    Margin = new Thickness(4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
        var badge = item.Kind switch { MediaKind.Video => "VIDEO", MediaKind.Gif => "GIF", _ => "" };
        if (badge.Length > 0)
            thumbBox.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 27, 28, 34)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                Child = new TextBlock { Text = badge, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold },
            });

        // top-left: star toggle · top-right: "⋯" menu with the less common actions
        var starred = _meta.IsStarred(item.Path);
        var star = OverlayButton(starred ? "\uE735" : "\uE734", starred ? "Bỏ sao" : "Gắn sao", HorizontalAlignment.Left);
        if (starred) star.Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 10));
        star.Click += (_, e) => { _meta.ToggleStar(item.Path); _grid.Tag = null; Reload(); e.Handled = true; };
        var more = OverlayButton("\uE712", "Thêm thao tác", HorizontalAlignment.Right);
        var menu = new ContextMenu();
        void M(string text, Action act) { var mi = new MenuItem { Header = Ui.Content(text) }; mi.Click += (_, _) => act(); menu.Items.Add(mi); }
        if (item.Kind != MediaKind.Video && _pin is not null) M("\uE718 Ghim lên màn hình", () => _pin(item.Path));
        M("\uE8EC Gắn thẻ…", () => EditTags(item));
        M("\uE71B Copy đường dẫn", () => { try { Clipboard.SetText(item.Path); } catch (Exception) { } });
        more.Click += (_, e) => { menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; e.Handled = true; };
        thumbBox.Children.Add(star); thumbBox.Children.Add(more);
        thumbBox.Clip = new RectangleGeometry(new Rect(0, 0, 204, 140), 8, 8);

        // five main actions as icons, spread evenly under the card
        var list = new List<(string Glyph, string Tip, Action Act)>
        {
            ("\uE8E5", "Mở file", () => Open(item.Path)),
            ("\uE838", "Hiện trong thư mục", () => Process.Start("explorer.exe", $"/select,\"{item.Path}\"")),
            ("\uE8C8", "Copy", () => CopyItem(item)),
        };
        if (item.Kind == MediaKind.Image && _edit is not null) list.Add(("\uE70F", "Chỉnh sửa", () => _edit(item.Path)));
        list.Add(("\uE74D", "Xóa (vào thùng rác)", () => Delete(item)));
        var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = list.Count, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (glyph, tip, act) in list)
        {
            var btn = new Button { Content = Ui.Glyph(glyph, 15), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(0, 6, 0, 6) };
            btn.Click += (_, e) => { act(); e.Handled = true; };
            btn.MouseLeftButtonUp += (_, e) => e.Handled = true; // do not trigger card open
            actions.Children.Add(btn);
        }

        var col = new StackPanel();
        col.Children.Add(thumbBox);
        col.Children.Add(new TextBlock { Text = Path.GetFileName(item.Path), Foreground = Ui.Brush("Text"), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) });
        col.Children.Add(new TextBlock { Text = $"{item.Modified:dd/MM/yyyy HH:mm} · {FormatSize(item.Size)}", Foreground = Ui.Brush("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        var tags = _meta.Get(item.Path).Tags;
        if (tags.Count > 0)
            col.Children.Add(new TextBlock { Text = string.Join(" ", tags.Select(t => "#" + t)), Foreground = Ui.Brush("Accent"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
        col.Children.Add(actions);

        var card = new Border
        {
            Width = 220, Margin = new Thickness(6), Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            Background = Ui.Brush("Bg"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), Child = col,
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Brush("Accent");
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Brush("Border");
        // click opens; dragging the card out of the window copies the file into another app (Zalo, Chrome, Explorer…)
        Point? downAt = null; var dragged = false;
        card.MouseLeftButtonDown += (_, e) => { downAt = e.GetPosition(card); dragged = false; };
        card.MouseMove += (_, e) =>
        {
            if (downAt is not { } d || e.LeftButton != MouseButtonState.Pressed || dragged) return;
            var p = e.GetPosition(card);
            if (Math.Abs(p.X - d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragged = true;
            DragDrop.DoDragDrop(card, new DataObject(DataFormats.FileDrop, new[] { item.Path }), DragDropEffects.Copy);
        };
        card.MouseLeftButtonUp += (_, _) => { if (!dragged) Open(item.Path); downAt = null; };

        if (item.Kind != MediaKind.Gif) _ = LoadThumb(item, img, version);
        return card;
    }

    static Button OverlayButton(string glyph, string tip, HorizontalAlignment align)
    {
        var b = new Button
        {
            Content = Ui.Glyph(glyph, 14), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6), ToolTip = tip,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(190, 27, 28, 34)), BorderBrush = Brushes.Transparent, Foreground = Brushes.White,
        };
        b.MouseLeftButtonUp += (_, e) => e.Handled = true;
        return b;
    }

    void EditTags(MediaItem item)
    {
        var current = string.Join(", ", _meta.Get(item.Path).Tags);
        var text = PromptWindow.Ask(Window.GetWindow(this), "Gắn thẻ", "Các thẻ cách nhau bằng dấu phẩy, ví dụ: lỗi, thiết kế, khách hàng", current);
        if (text is null) return;
        _meta.SetTags(item.Path, LibraryMetaStore.ParseTags(text));
        _grid.Tag = null; Reload();
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
