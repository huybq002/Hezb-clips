using System.Diagnostics;
using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace HezbClips.Capture;

/// <summary>Emits BGRA frames of a screen region at a fixed fps (repeats the latest frame on static screens).
/// The buffer passed to the callback is reused: the consumer must copy or write it synchronously.</summary>
public sealed class WgcFrameSource : IVideoFrameSource
{
    readonly object _lock = new();
    Direct3D11CaptureFramePool? _pool;
    GraphicsCaptureSession? _session;
    FrameReader? _reader;
    Vortice.Direct3D11.ID3D11Device? _dev;
    byte[]? _latest;
    bool _hasFrame;
    CancellationTokenSource? _cts;
    Task? _pump;

    public void Start(Rectangle region, int fps, Action<ReadOnlyMemory<byte>> onFrame)
    {
        var (h, bounds) = Native.MonitorContaining(region);
        var rel = Rectangle.Intersect(
            new Rectangle(region.X - bounds.X, region.Y - bounds.Y, region.Width, region.Height),
            new Rectangle(0, 0, bounds.Width, bounds.Height));
        if (rel.Width <= 0 || rel.Height <= 0) throw new ArgumentException("region outside monitor", nameof(region));

        var item = WgcInterop.ItemForMonitor(h);
        _dev = WgcInterop.CreateDevice();
        _reader = new FrameReader(_dev);
        _latest = new byte[rel.Width * rel.Height * 4];
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            WgcInterop.ToWinRt(_dev), DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        _pool.FrameArrived += (s, _) =>
        {
            using var f = s.TryGetNextFrame();
            if (f is null) return;
            var full = new Rectangle(0, 0, f.ContentSize.Width, f.ContentSize.Height);
            if (!full.Contains(rel)) return;
            lock (_lock) { _reader!.Read(f, rel, _latest!); _hasFrame = true; }
        };
        _session = _pool.CreateCaptureSession(item);
        _session.StartCapture();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _pump = Task.Run(() =>
        {
            var interval = TimeSpan.FromSeconds(1.0 / fps);
            var sw = Stopwatch.StartNew(); long n = 0;
            while (!ct.IsCancellationRequested)
            {
                var due = interval * (++n) - sw.Elapsed;
                if (due > TimeSpan.Zero) Thread.Sleep(due);
                lock (_lock) { if (_hasFrame) onFrame(_latest!); }
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _pump?.Wait(2000); } catch (AggregateException) { }
        _session?.Dispose(); _pool?.Dispose();
        lock (_lock) { _reader?.Dispose(); }
        _dev?.Dispose();
        _session = null; _pool = null; _reader = null; _dev = null; _pump = null;
    }
}
