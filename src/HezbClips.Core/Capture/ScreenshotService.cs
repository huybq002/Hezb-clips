using Rectangle = System.Drawing.Rectangle;
using System.Drawing;
using HezbClips.Core.Imaging;
using HezbClips.Core.Output;
using HezbClips.Core.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.Core.Capture;

public class ScreenshotService(ICaptureSource src, IImageUpscaler ai, AppSettings s)
{
    public Task<string> TakeRegionAsync(Rectangle physicalPixels) => SaveAsync(src.CaptureRegion(physicalPixels));
    public Task<string> TakeScreenAsync(int monitorIndex) => SaveAsync(src.CaptureScreen(monitorIndex));
    public Task<string> TakeWindowAsync(IntPtr hwnd) => SaveAsync(src.CaptureWindow(hwnd));

    async Task<string> SaveAsync(Image<Rgba32> shot)
    {
        using (shot)
        {
            var (w, h) = TargetSizeCalculator.Calculate(shot.Width, shot.Height, s.Quality);
            using var fitted = ImageResizer.Fit(shot, w, h, ai);
            Directory.CreateDirectory(s.OutputDir);
            var ext = s.ImageFormat switch { ImageFormat.WebP => "webp", ImageFormat.Jpeg => "jpg", _ => "png" };
            var path = FileNamer.Next(s.OutputDir, "Clip", ext, DateTime.Now, File.Exists);
            switch (s.ImageFormat)
            {
                case ImageFormat.WebP: await fitted.SaveAsWebpAsync(path); break;
                case ImageFormat.Jpeg: await fitted.SaveAsJpegAsync(path); break;
                default: await fitted.SaveAsPngAsync(path); break;
            }
            return path;
        }
    }
}
