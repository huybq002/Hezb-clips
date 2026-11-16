using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.Core.Imaging;

public interface IImageUpscaler
{
    Image<Rgba32> Upscale(Image<Rgba32> src, int targetW, int targetH);
}
