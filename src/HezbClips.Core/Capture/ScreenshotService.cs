using Rectangle = System.Drawing.Rectangle;
using System.Drawing;
using HezbClips.Core.Imaging;
using HezbClips.Core.Output;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Capture;

public class ScreenshotService(ICaptureSource src, IImageUpscaler ai, AppSettings s)
{
    public Task<string> TakeRegionAsync(Rectangle physicalPixels) => SaveAsync(src.CaptureRegion(physicalPixels));
    public Task<string> TakeScreenAsync(int monitorIndex) => SaveAsync(src.CaptureScreen(monitorIndex));
    public Task<string> TakeWindowAsync(IntPtr hwnd) => SaveAsync(src.CaptureWindow(hwnd));

    /// <summary>Scale to the export quality and save. Takes ownership of the image.</summary>
    public async Task<string> SaveAsync(Raster shot)
    {
        using (shot)
        {
            var (w, h) = TargetSizeCalculator.Calculate(shot.Width, shot.Height, s.Quality);
            using var fitted = ImageResizer.Fit(shot, w, h, ai);
            Directory.CreateDirectory(s.OutputDir);
            var ext = s.ImageFormat switch { ImageFormat.WebP => "webp", ImageFormat.Jpeg => "jpg", _ => "png" };
            var path = FileNamer.Next(s.OutputDir, "Clip", ext, DateTime.Now, File.Exists);
            await Task.Run(() =>
            {
                switch (s.ImageFormat)
                {
                    case ImageFormat.WebP: fitted.SaveWebp(path); break;
                    case ImageFormat.Jpeg: fitted.SaveJpeg(path); break;
                    default: fitted.SavePng(path); break;
                }
            });
            return path;
        }
    }
}
