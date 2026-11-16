using Rectangle = System.Drawing.Rectangle;
using System.Drawing;
using HezbClips.Core.Capture;
using HezbClips.Core.Imaging;
using HezbClips.Core.Output;
using HezbClips.Core.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.Core.Tests;

public class ScreenshotServiceTests
{
    class FakeSource(int w, int h) : ICaptureSource
    {
        public Image<Rgba32> CaptureRegion(Rectangle r) => new(w, h);
        public Image<Rgba32> CaptureWindow(IntPtr hwnd) => new(w, h);
        public Image<Rgba32> CaptureScreen(int monitorIndex) => new(w, h);
    }

    [Fact] public void Collision_adds_suffix()
    {
        var now = new DateTime(2026, 11, 16, 9, 30, 0);
        var first = @"D:\o\Clip_20261116_093000.png";
        var r = FileNamer.Next(@"D:\o", "Clip", "png", now, f => f == first);
        Assert.Equal(@"D:\o\Clip_20261116_093000_1.png", r);
    }

    [Fact] public async Task Region_shot_saved_at_export_size()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var s = new AppSettings { Quality = ExportQuality.P1440, OutputDir = dir };
        var svc = new ScreenshotService(new FakeSource(1280, 720), new LanczosUpscaler(), s);
        var path = await svc.TakeRegionAsync(new Rectangle(0, 0, 1280, 720));
        using var img = Image.Load(path);
        Assert.Equal((2560, 1440), (img.Width, img.Height));
        Assert.EndsWith(".png", path);
    }

    [Fact] public async Task Webp_format_is_respected()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var s = new AppSettings { ImageFormat = ImageFormat.WebP, OutputDir = dir };
        var path = await new ScreenshotService(new FakeSource(640, 360), new LanczosUpscaler(), s).TakeScreenAsync(0);
        Assert.EndsWith(".webp", path);
    }
}
