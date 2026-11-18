using HezbClips.Core.Imaging;
using HezbClips.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.Core.Tests;

public class LibraryMetaTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid() + ".json");

    [Fact] public void Star_and_tags_persist()
    {
        var p = Tmp();
        var a = new LibraryMetaStore(p);
        Assert.True(a.ToggleStar(@"C:\x\one.png"));
        a.SetTags(@"C:\x\one.png", LibraryMetaStore.ParseTags("#bug, ui  login"));
        var b = new LibraryMetaStore(p);
        Assert.True(b.IsStarred(@"D:\other\one.png")); // keyed by file name
        Assert.Equal(new[] { "bug", "ui", "login" }, b.Get("one.png").Tags);
        File.Delete(p);
    }

    [Fact] public void Entry_without_star_or_tags_is_dropped()
    {
        var p = Tmp(); var a = new LibraryMetaStore(p);
        a.ToggleStar("a.png"); a.ToggleStar("a.png");
        Assert.DoesNotContain("a.png", File.ReadAllText(p));
        File.Delete(p);
    }

    [Fact] public void Filter_by_text_tag_star_and_kind()
    {
        var p = Tmp(); var meta = new LibraryMetaStore(p);
        meta.SetTags("login.png", new[] { "bug" }); meta.ToggleStar("clip.mp4");
        var now = DateTime.Now;
        var items = new[]
        {
            new MediaItem("login.png", MediaKind.Image, now, 1), new MediaItem("clip.mp4", MediaKind.Video, now, 1), new MediaItem("pic.png", MediaKind.Image, now, 1),
        };
        Assert.Single(LibraryQuery.Filter(items, meta, "bug", false, null));
        Assert.Single(LibraryQuery.Filter(items, meta, "#bug", false, null));
        Assert.Single(LibraryQuery.Filter(items, meta, null, true, null));
        Assert.Equal(2, LibraryQuery.Filter(items, meta, "", false, MediaKind.Image).Count);
        Assert.Single(LibraryQuery.Filter(items, meta, "PIC", false, null));
        File.Delete(p);
    }

    [Fact] public void Cleaner_skips_starred_and_recent_and_disabled()
    {
        var p = Tmp(); var meta = new LibraryMetaStore(p); meta.ToggleStar("Clip_20260801_101010.png");
        var now = new DateTime(2026, 11, 17);
        var items = new[]
        {
            new MediaItem("Clip_20261001_101010_edit.png", MediaKind.Image, now.AddDays(-40), 1), new MediaItem("Clip_20260801_101010.png", MediaKind.Image, now.AddDays(-90), 1),
            new MediaItem("Clip_20261115_101010.png", MediaKind.Image, now.AddDays(-2), 1), new MediaItem("holiday.jpg", MediaKind.Image, now.AddDays(-300), 1),
        };
        Assert.Equal(new[] { "Clip_20261001_101010_edit.png" }, LibraryCleaner.FindExpired(items, meta, 30, now).Select(i => i.Path));
        Assert.Empty(LibraryCleaner.FindExpired(items, meta, 0, now));
        File.Delete(p);
    }
}

public class ScrollStitcherTests
{
    // a tall "page" where every row is unique
    static Image<Rgba32> Page(int w, int h)
    {
        var rnd = new Random(7); var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) img[x, y] = new Rgba32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 255);
        return img;
    }

    static Image<Rgba32> Window(Image<Rgba32> page, int y, int h) => page.Clone(c => c.Crop(new Rectangle(0, y, page.Width, h)));

    [Fact] public void Finds_the_scroll_shift()
    {
        using var page = Page(120, 900);
        using var a = Window(page, 0, 300); using var b = Window(page, 140, 300);
        Assert.Equal(140, ScrollStitcher.FindShift(a, b));
    }

    [Fact] public void Identical_frames_mean_end_of_page()
    {
        using var page = Page(120, 600);
        using var a = Window(page, 0, 300); using var b = Window(page, 0, 300);
        Assert.Equal(0, ScrollStitcher.FindShift(a, b));
    }

    [Fact] public void Unrelated_frames_do_not_match()
    {
        using var a = Page(120, 300);
        using var other = new Image<Rgba32>(120, 300, new Rgba32(10, 200, 30, 255));
        Assert.Equal(ScrollStitcher.NoMatch, ScrollStitcher.FindShift(a, other));
    }

    // what the browser shows: a fixed header and footer drawn over the scrolled page
    static Image<Rgba32> ViewWithChrome(Image<Rgba32> page, int y, int h, int header, int footer)
    {
        var v = Window(page, y, h);
        for (int yy = 0; yy < h; yy++)
            for (int x = 0; x < page.Width; x++)
            {
                if (yy < header) v[x, yy] = new Rgba32(30, 60, 200, 255);
                else if (yy >= h - footer) v[x, yy] = new Rgba32(200, 60, 30, 255);
            }
        return v;
    }

    [Fact] public void Sticky_header_and_footer_are_ignored()
    {
        using var page = Page(100, 1000);
        const int H = 300, head = 40, foot = 30;
        var frames = new List<Image<Rgba32>>();
        foreach (var y in new[] { 0, 110, 220, 330, 440, 550, 660, 700, 700 }) frames.Add(ViewWithChrome(page, y, H, head, foot));
        var (img, used) = ScrollStitcher.Stitch(frames);
        using (img)
        {
            Assert.Equal(8, used);
            // header once at the top, footer once at the bottom, page content contiguous in between
            Assert.Equal(700 + H, img.Height);
            Assert.Equal(frames[0][5, 5], img[5, 5]);
            Assert.Equal(frames[^1][5, H - 5], img[5, img.Height - 5]);
            for (int y = head; y < img.Height - foot; y += 23) Assert.Equal(page[7, y], img[7, y]);
        }
        foreach (var f in frames) f.Dispose();
    }

    [Fact] public void Stitch_rebuilds_the_page_and_stops_at_the_end()
    {
        using var page = Page(100, 800);
        var frames = new List<Image<Rgba32>> { Window(page, 0, 300), Window(page, 120, 300), Window(page, 240, 300), Window(page, 360, 300), Window(page, 500, 300), Window(page, 500, 300) };
        var (img, used) = ScrollStitcher.Stitch(frames);
        using (img)
        {
            Assert.Equal(5, used);
            Assert.Equal(800, img.Height);
            for (int y = 0; y < 800; y += 37) Assert.Equal(page[5, y], img[5, y]);
        }
        foreach (var f in frames) f.Dispose();
    }
}

public class ScrollAmbiguityTests
{
    [Fact] public void Blank_frames_that_moved_are_not_guessed()
    {
        using var a = new Image<Rgba32>(100, 300, new Rgba32(255, 255, 255, 255));
        using var b = new Image<Rgba32>(100, 300, new Rgba32(255, 255, 255, 255));
        b[50, 10] = new Rgba32(0, 0, 0, 255); // tiny change so it is not "identical"
        Assert.True(ScrollStitcher.FindShift(a, b) <= 0);
    }
}

public class FrozenComposerTests
{
    [Fact] public void Region_across_two_monitors_takes_both_halves()
    {
        using var left = new Image<Rgba32>(100, 50, new Rgba32(255, 0, 0, 255));
        using var right = new Image<Rgba32>(100, 50, new Rgba32(0, 0, 255, 255));
        var screens = new Dictionary<System.Drawing.Rectangle, Image<Rgba32>>
        {
            [new System.Drawing.Rectangle(0, 0, 100, 50)] = left, [new System.Drawing.Rectangle(100, 0, 100, 50)] = right,
        };
        using var img = HezbClips.Core.Imaging.FrozenComposer.Compose(screens, new System.Drawing.Rectangle(80, 10, 40, 20))!;
        Assert.Equal(40, img.Width); Assert.Equal(20, img.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), img[5, 5]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), img[35, 5]);
    }

    [Fact] public void Region_off_screen_is_null()
    {
        using var a = new Image<Rgba32>(10, 10);
        var screens = new Dictionary<System.Drawing.Rectangle, Image<Rgba32>> { [new System.Drawing.Rectangle(0, 0, 10, 10)] = a };
        Assert.Null(HezbClips.Core.Imaging.FrozenComposer.Compose(screens, new System.Drawing.Rectangle(50, 50, 5, 5)));
    }
}

public class SparsePageStitchTests
{
    // like a search-results page: white background, a few text-like rows, large blank gaps
    static Image<Rgba32> SparsePage(int w, int h)
    {
        var img = new Image<Rgba32>(w, h, new Rgba32(255, 255, 255, 255));
        var rnd = new Random(11);
        for (int y = 20; y < h - 20; y += rnd.Next(18, 60))
        {
            int len = rnd.Next(w / 4, w * 3 / 4), x0 = rnd.Next(10, w / 4);
            for (int dy = 0; dy < 12 && y + dy < h; dy++)
                for (int x = x0; x < x0 + len && x < w; x++)
                    if (rnd.Next(100) < 55) img[x, y + dy] = new Rgba32(40, 40, 40, 255);
        }
        return img;
    }

    [Theory]
    [InlineData(200)] [InlineData(333)] [InlineData(410)]
    public void Sparse_text_page_shift_is_found(int shift)
    {
        using var page = SparsePage(480, 2400);
        using var a = page.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, 480, 800)));
        using var b = page.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(0, shift, 480, 800)));
        Assert.Equal(shift, ScrollStitcher.FindShift(a, b));
    }
}

public class ScrollCaptureLoopTests
{
    const int W = 300, WinH = 400, Header = 40, Footer = 0;

    // rows made of the same long bars, with only a tiny unique tag per row: very ambiguous to a matcher
    static Image<Rgba32> RepetitivePage(int h)
    {
        var img = new Image<Rgba32>(W, h, new Rgba32(255, 255, 255, 255));
        var rnd = new Random(5);
        for (int row = 0; row * 24 + 14 < h; row++)
        {
            int y = row * 24;
            for (int dy = 0; dy < 10; dy++) for (int x = 10; x < 250; x++) img[x, y + dy] = new Rgba32(60, 60, 60, 255);
            for (int dy = 0; dy < 10; dy++) for (int x = 255; x < 255 + 12; x++) if (rnd.Next(2) == 0) img[x, y + dy] = new Rgba32(200, 30, 30, 255);
        }
        return img;
    }

    sealed class FakeBrowser(Image<Rgba32> page, int pxPerNotch)
    {
        public int Offset;
        public Image<Rgba32> Capture()
        {
            var v = page.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(0, Offset, W, WinH)));
            for (int y = 0; y < Header; y++) for (int x = 0; x < W; x++) v[x, y] = new Rgba32(30, 60, 200, 255); // sticky header
            return v;
        }
        public void Scroll(int notches) => Offset = Math.Min(page.Height - WinH, Offset + notches * pxPerNotch);
    }

    [Fact] public void Repetitive_page_is_captured_to_the_very_end()
    {
        using var page = RepetitivePage(3000);
        var fb = new FakeBrowser(page, 70);
        var (img, steps, reason) = HezbClips.Core.Imaging.ScrollCapture.Run(fb.Capture, fb.Scroll, 3, () => false, _ => { });
        using (img)
        {
            Assert.Equal("đã tới cuối trang", reason);
            Assert.Equal(3000, img.Height);                       // header once + whole page + nothing missing
            for (int y = Header + 3; y < 3000; y += 29) Assert.Equal(page[20, y], img[20, y]);
            for (int y = Header + 3; y < 3000; y += 31) Assert.Equal(page[258, y], img[258, y]); // the unique tags line up too
        }
    }

    [Fact] public void Cancel_keeps_what_was_captured()
    {
        using var page = RepetitivePage(3000);
        var fb = new FakeBrowser(page, 70);
        int calls = 0;
        var (img, _, reason) = HezbClips.Core.Imaging.ScrollCapture.Run(fb.Capture, fb.Scroll, 3, () => ++calls > 4, _ => { });
        using (img) { Assert.Equal("đã dừng theo yêu cầu", reason); Assert.True(img.Height > WinH); Assert.True(img.Height < 3000); }
    }
}

public class ScrollEndTests
{
    [Fact] public void Last_tiny_scroll_at_the_page_end_is_not_lost()
    {
        var rnd = new Random(2);
        using var page = new Image<Rgba32>(200, 1000);
        for (int y = 0; y < 1000; y++) for (int x = 0; x < 200; x++) page[x, y] = new Rgba32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 255);
        int off = 0;
        Image<Rgba32> Cap() => page.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(0, off, 200, 300)));
        // 700 = 3 * 200 + 100 ... use steps so the final one moves only 7 px
        void Scroll(int n) => off = Math.Min(693, off + n * 231);
        var (img, _, reason) = HezbClips.Core.Imaging.ScrollCapture.Run(Cap, Scroll, 1, () => false, _ => { });
        using (img) { Assert.Equal("đã tới cuối trang", reason); Assert.Equal(993, img.Height); }
    }

    [Fact] public void Page_that_never_scrolls_is_reported_as_such()
    {
        using var page = new Image<Rgba32>(100, 300, new Rgba32(10, 10, 10, 255));
        page[5, 5] = new Rgba32(255, 255, 255, 255);
        var (img, _, reason) = HezbClips.Core.Imaging.ScrollCapture.Run(() => page.Clone(), _ => { }, 1, () => false, _ => { });
        using (img) Assert.Equal("trang không cuộn", reason);
    }

    [Fact] public void Keyboard_fallback_is_tried_when_the_wheel_does_nothing()
    {
        var rnd = new Random(4);
        using var page = new Image<Rgba32>(100, 900);
        for (int y = 0; y < 900; y++) for (int x = 0; x < 100; x++) page[x, y] = new Rgba32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 255);
        int off = 0; bool pressed = false;
        Image<Rgba32> Cap() => page.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(0, off, 100, 300)));
        var (img, _, _) = HezbClips.Core.Imaging.ScrollCapture.Run(Cap, _ => { /* wheel ignored */ }, 1, () => false, _ => { },
            scrollFallback: () => { pressed = true; });
        using (img) Assert.True(pressed);
    }
}
