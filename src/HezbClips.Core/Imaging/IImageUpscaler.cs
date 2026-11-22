
namespace HezbClips.Core.Imaging;

public interface IImageUpscaler
{
    Raster Upscale(Raster src, int targetW, int targetH);
}
