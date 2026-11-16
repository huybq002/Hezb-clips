using Rectangle = System.Drawing.Rectangle;

namespace HezbClips.Core.Capture;

public interface IVideoFrameSource
{
    void Start(Rectangle physicalPixels, int fps, Action<ReadOnlyMemory<byte>> onBgraFrame);
    void Stop();
}
