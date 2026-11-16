using Rectangle = System.Drawing.Rectangle;
using System.Drawing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.Core.Capture;

public interface ICaptureSource
{
    Image<Rgba32> CaptureRegion(Rectangle physicalPixels);
    Image<Rgba32> CaptureWindow(IntPtr hwnd);
    Image<Rgba32> CaptureScreen(int monitorIndex);
}
