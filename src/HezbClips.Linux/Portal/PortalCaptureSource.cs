using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Capture;
using HezbClips.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.Linux.Portal;

/// <summary>ICaptureSource backed by the screenshot portal. Per-window capture is not available there.</summary>
public class PortalCaptureSource(IReadOnlyList<Rectangle> screens) : ICaptureSource
{
    public Image<Rgba32> CaptureScreen(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= screens.Count) throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        var img = ScreenshotPortal.GrabAsync().GetAwaiter().GetResult();
        return CropToScreen(img, screens, monitorIndex);
    }

    public Image<Rgba32> CaptureRegion(Rectangle physicalPixels)
    {
        var img = ScreenshotPortal.GrabAsync().GetAwaiter().GetResult();
        var r = Rectangle.Intersect(physicalPixels, new Rectangle(0, 0, img.Width, img.Height));
        if (r.Width <= 0 || r.Height <= 0) { img.Dispose(); throw new ArgumentException("region outside screenshot"); }
        using (img) return img.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(r.X, r.Y, r.Width, r.Height)));
    }

    public Image<Rgba32> CaptureWindow(IntPtr hwnd) =>
        throw new NotSupportedException("Window capture is not supported on Linux yet.");

    /// <summary>Crop one screen out of a whole-desktop screenshot. Takes ownership of <paramref name="img"/>.</summary>
    public static Image<Rgba32> CropToScreen(Image<Rgba32> img, IReadOnlyList<Rectangle> screens, int index)
    {
        using (img)
        {
            var r = CropMath.ScreenRegionInImage(screens[index], CropMath.Union(screens), img.Width, img.Height);
            return img.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(r.X, r.Y, r.Width, r.Height)));
        }
    }
}
