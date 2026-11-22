using HezbClips.Core.Hardware;

namespace HezbClips.Core.Imaging;

public static class ImageResizer
{
    public static IImageUpscaler CreateUpscaler(HardwareProfile p, string? realEsrganExe) =>
        p.VulkanAvailable && realEsrganExe is not null && File.Exists(realEsrganExe)
            ? new RealEsrganUpscaler(realEsrganExe)
            : new LanczosUpscaler();

    public static Raster Fit(Raster src, int targetW, int targetH, IImageUpscaler ai) =>
        targetW <= src.Width && targetH <= src.Height
            ? new LanczosUpscaler().Upscale(src, targetW, targetH)
            : ai.Upscale(src, targetW, targetH);
}
