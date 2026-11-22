
namespace HezbClips.Core.Imaging;

public class LanczosUpscaler : IImageUpscaler
{
    public Raster Upscale(Raster src, int targetW, int targetH)
    {
        var enlarging = targetW > src.Width || targetH > src.Height;
        var resized = src.Resize(targetW, targetH);
        if (!enlarging) return resized;
        // Lanczos3 keeps edges well; the mild unsharp pass restores the crispness lost when enlarging.
        using (resized) return resized.Sharpen(0.9);
    }
}
