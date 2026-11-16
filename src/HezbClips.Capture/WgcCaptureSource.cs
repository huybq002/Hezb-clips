using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Capture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace HezbClips.Capture;

public class WgcCaptureSource : ICaptureSource
{
    public Image<Rgba32> CaptureScreen(int monitorIndex)
    {
        var mons = Native.Monitors();
        if (monitorIndex < 0 || monitorIndex >= mons.Count) throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        return Grab(WgcInterop.ItemForMonitor(mons[monitorIndex].Handle), null);
    }

    public Image<Rgba32> CaptureWindow(IntPtr hwnd) => Grab(WgcInterop.ItemForWindow(hwnd), null);

    public Image<Rgba32> CaptureRegion(Rectangle physicalPixels)
    {
        var (h, bounds) = Native.MonitorContaining(physicalPixels);
        var rel = Rectangle.Intersect(
            new Rectangle(physicalPixels.X - bounds.X, physicalPixels.Y - bounds.Y, physicalPixels.Width, physicalPixels.Height),
            new Rectangle(0, 0, bounds.Width, bounds.Height));
        if (rel.Width <= 0 || rel.Height <= 0) throw new ArgumentException("region outside monitor", nameof(physicalPixels));
        return Grab(WgcInterop.ItemForMonitor(h), rel);
    }

    static Image<Rgba32> Grab(GraphicsCaptureItem item, Rectangle? crop)
    {
        using var dev = WgcInterop.CreateDevice();
        var winDev = WgcInterop.ToWinRt(dev);
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winDev, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var session = pool.CreateCaptureSession(item);
        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (s, _) => { var f = s.TryGetNextFrame(); if (f is not null) tcs.TrySetResult(f); };
        session.StartCapture();
        if (!tcs.Task.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("no frame from Windows Graphics Capture");
        using var frame = tcs.Task.Result;
        var full = new Rectangle(0, 0, frame.ContentSize.Width, frame.ContentSize.Height);
        var r = crop is null ? full : Rectangle.Intersect(crop.Value, full);
        var buf = new byte[r.Width * r.Height * 4];
        using var reader = new FrameReader(dev);
        reader.Read(frame, r, buf);
        using var bgra = Image.LoadPixelData<Bgra32>(buf, r.Width, r.Height);
        return bgra.CloneAs<Rgba32>();
    }
}
