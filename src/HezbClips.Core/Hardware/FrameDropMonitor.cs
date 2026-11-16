namespace HezbClips.Core.Hardware;

public class FrameDropMonitor(int window = 60, double dropRatio = 0.1)
{
    readonly Queue<bool> _samples = new();
    int _drops;

    public void Report(bool dropped)
    {
        _samples.Enqueue(dropped);
        if (dropped) _drops++;
        if (_samples.Count > window && _samples.Dequeue()) _drops--;
    }

    public bool ShouldDowngrade => _samples.Count >= window && (double)_drops / window >= dropRatio;
}
