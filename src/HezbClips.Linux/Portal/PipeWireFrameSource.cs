using System.Diagnostics;
using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Capture;

namespace HezbClips.Linux.Portal;

/// <summary>Reads a screencast PipeWire node through `gst-launch-1.0 pipewiresrc` and delivers fixed-fps BGRA frames.
/// The region is relative to the stream. Requires gstreamer + its pipewire plugin.</summary>
public sealed class PipeWireFrameSource(uint nodeId, int streamW, int streamH) : IVideoFrameSource
{
    Process? _gst;
    Thread? _pump;
    volatile bool _stopping;

    public void Start(Rectangle region, int fps, Action<ReadOnlyMemory<byte>> onFrame)
    {
        int right = streamW - region.Right, bottom = streamH - region.Bottom;
        if (region.X < 0 || region.Y < 0 || right < 0 || bottom < 0)
            throw new ArgumentException("region outside the shared screen", nameof(region));

        var pipeline =
            $"-q pipewiresrc path={nodeId} do-timestamp=true keepalive-time={Math.Max(1, 1000 / fps)} ! " +
            $"videoconvert ! video/x-raw,format=BGRA ! " +
            $"videocrop left={region.X} top={region.Y} right={right} bottom={bottom} ! " +
            $"videorate ! video/x-raw,framerate={fps}/1 ! fdsink fd=1";
        var psi = new ProcessStartInfo("gst-launch-1.0", pipeline)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        try { _gst = Process.Start(psi) ?? throw new InvalidOperationException("gst-launch-1.0 failed to start"); }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("gst-launch-1.0 not found. Install gstreamer1.0-tools and gstreamer1.0-pipewire."); }
        _gst.ErrorDataReceived += (_, _) => { };
        _gst.BeginErrorReadLine();

        var frame = new byte[region.Width * region.Height * 4];
        var stdout = _gst.StandardOutput.BaseStream;
        _stopping = false;
        _pump = new Thread(() =>
        {
            try
            {
                while (!_stopping)
                {
                    stdout.ReadExactly(frame);
                    onFrame(frame);
                }
            }
            catch (EndOfStreamException) { }
            catch (IOException) { }
        }) { IsBackground = true, Name = "pipewire-frames" };
        _pump.Start();
    }

    public void Stop()
    {
        _stopping = true;
        try { if (_gst is { HasExited: false }) _gst.Kill(); } catch (InvalidOperationException) { }
        try { _pump?.Join(2000); } catch (ThreadStateException) { }
        _gst?.Dispose(); _gst = null; _pump = null;
    }
}
