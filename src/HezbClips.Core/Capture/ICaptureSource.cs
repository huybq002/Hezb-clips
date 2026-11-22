using Rectangle = System.Drawing.Rectangle;
using System.Drawing;
using HezbClips.Core.Imaging;

namespace HezbClips.Core.Capture;

public interface ICaptureSource
{
    Raster CaptureRegion(Rectangle physicalPixels);
    Raster CaptureWindow(IntPtr hwnd);
    Raster CaptureScreen(int monitorIndex);
}
