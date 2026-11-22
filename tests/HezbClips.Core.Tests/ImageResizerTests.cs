using HezbClips.Core.Hardware;
using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class ImageResizerTests
{
    class SpyUpscaler : IImageUpscaler
    {
        public int Calls;
        public Raster Upscale(Raster s, int w, int h) { Calls++; return new Raster(w, h); }
    }

    [Fact] public void Lanczos_gives_exact_size()
    {
        using var src = new Raster(100, 50);
        using var r = new LanczosUpscaler().Upscale(src, 200, 100);
        Assert.Equal((200, 100), (r.Width, r.Height));
    }

    [Fact] public void Factory_falls_back_to_lanczos_when_exe_missing()
    {
        var p = new HardwareProfile(8, true, 4096, true, 60);
        Assert.IsType<LanczosUpscaler>(ImageResizer.CreateUpscaler(p, @"C:\none\x.exe"));
    }

    [Fact] public void Factory_falls_back_when_vulkan_unavailable()
    {
        var p = new HardwareProfile(8, true, 4096, false, 60);
        Assert.IsType<LanczosUpscaler>(ImageResizer.CreateUpscaler(p, null));
    }

    [Fact] public void Fit_downscale_never_calls_ai()
    {
        var spy = new SpyUpscaler(); using var src = new Raster(3840, 2160);
        using var r = ImageResizer.Fit(src, 1920, 1080, spy);
        Assert.Equal(0, spy.Calls);
        Assert.Equal((1920, 1080), (r.Width, r.Height));
    }

    [Fact] public void Fit_upscale_calls_ai_once()
    {
        var spy = new SpyUpscaler(); using var src = new Raster(1280, 720);
        ImageResizer.Fit(src, 3840, 2160, spy).Dispose();
        Assert.Equal(1, spy.Calls);
    }
}
