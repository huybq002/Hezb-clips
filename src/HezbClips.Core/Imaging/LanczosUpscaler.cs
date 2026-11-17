using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.Core.Imaging;

public class LanczosUpscaler : IImageUpscaler
{
    public Image<Rgba32> Upscale(Image<Rgba32> src, int targetW, int targetH)
    {
        var copy = src.Clone();
        var enlarging = targetW > src.Width || targetH > src.Height;
        // Lanczos3 keeps edges well; the mild unsharp pass restores the crispness lost when enlarging.
        copy.Mutate(c =>
        {
            c.Resize(targetW, targetH, KnownResamplers.Lanczos3);
            if (enlarging) c.GaussianSharpen(0.9f);
        });
        return copy;
    }
}
