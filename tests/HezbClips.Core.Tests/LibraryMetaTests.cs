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
        var p = Tmp(); var meta = new LibraryMetaStore(p); meta.ToggleStar("keep.png");
        var now = new DateTime(2026, 11, 17);
        var items = new[]
        {
            new MediaItem("old.png", MediaKind.Image, now.AddDays(-40), 1), new MediaItem("keep.png", MediaKind.Image, now.AddDays(-90), 1),
            new MediaItem("new.png", MediaKind.Image, now.AddDays(-2), 1),
        };
        Assert.Equal(new[] { "old.png" }, LibraryCleaner.FindExpired(items, meta, 30, now).Select(i => i.Path));
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
