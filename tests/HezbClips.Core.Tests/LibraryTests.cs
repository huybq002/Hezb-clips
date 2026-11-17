using HezbClips.Core.Output;
using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class LibraryTests
{
    [Fact] public void Scan_classifies_and_sorts_newest_first()
    {
        var d = Directory.CreateTempSubdirectory("lib").FullName;
        try
        {
            File.WriteAllText(Path.Combine(d, "a.png"), "x"); File.SetLastWriteTime(Path.Combine(d, "a.png"), new DateTime(2026, 1, 1));
            File.WriteAllText(Path.Combine(d, "b.mp4"), "x"); File.SetLastWriteTime(Path.Combine(d, "b.mp4"), new DateTime(2026, 1, 3));
            File.WriteAllText(Path.Combine(d, "c.GIF"), "x"); File.SetLastWriteTime(Path.Combine(d, "c.GIF"), new DateTime(2026, 1, 2));
            File.WriteAllText(Path.Combine(d, "notes.txt"), "x");
            var items = LibraryScanner.Scan(d);
            Assert.Equal(new[] { "b.mp4", "c.GIF", "a.png" }, items.Select(i => Path.GetFileName(i.Path)));
            Assert.Equal(new[] { MediaKind.Video, MediaKind.Gif, MediaKind.Image }, items.Select(i => i.Kind));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact] public void Scan_missing_dir_is_empty() => Assert.Empty(LibraryScanner.Scan(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid())));

    [Fact] public void Gif_args_use_palette_and_cap_width()
    {
        var a = FfmpegCommandBuilder.GifArgs("in.mp4", "o.gif");
        Assert.Contains("palettegen", a); Assert.Contains("paletteuse", a);
        Assert.Contains("min(960,iw)", a); Assert.Contains("-loop 0", a);
        Assert.Equal(TimeSpan.FromSeconds(60), FfmpegCommandBuilder.GifMaxDuration);
    }
}

public class EditorShortcutTests
{
    [Fact] public void Defaults_are_valid() => Assert.Null(HezbClips.Core.Settings.EditorShortcuts.Validate(new Dictionary<string, string>(HezbClips.Core.Settings.EditorShortcuts.Defaults)));

    [Fact] public void Duplicate_key_rejected()
    {
        var m = new Dictionary<string, string>(HezbClips.Core.Settings.EditorShortcuts.Defaults) { ["Line"] = "p" };
        Assert.NotNull(HezbClips.Core.Settings.EditorShortcuts.Validate(m));
    }

    [Fact] public void Reserved_and_nonletter_rejected()
    {
        var m = new Dictionary<string, string>(HezbClips.Core.Settings.EditorShortcuts.Defaults) { ["Pen"] = "C" };
        Assert.NotNull(HezbClips.Core.Settings.EditorShortcuts.Validate(m));
        m["Pen"] = "1"; Assert.NotNull(HezbClips.Core.Settings.EditorShortcuts.Validate(m));
    }

    [Fact] public void Resolve_fills_missing_with_defaults()
    {
        var r = HezbClips.Core.Settings.EditorShortcuts.Resolve(new Dictionary<string, string> { ["Pen"] = "b", ["Rect"] = "??" });
        Assert.Equal("B", r["Pen"]); Assert.Equal("R", r["Rect"]); Assert.Equal("M", r["Mosaic"]);
    }
}
