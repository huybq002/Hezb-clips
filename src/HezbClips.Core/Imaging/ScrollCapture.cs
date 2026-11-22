
namespace HezbClips.Core.Imaging;

/// <summary>The scroll-and-stitch loop, independent of Windows: the caller supplies how to grab a frame and how to scroll.</summary>
public static class ScrollCapture
{
    public sealed record Options(int MaxSteps = 300, int MaxHeight = 30000, int Settle = 6);

    /// <param name="capture">Grab the region now.</param>
    /// <param name="scroll">Scroll the page down by this many wheel notches.</param>
    /// <param name="cancelled">Polled each step; true stops early and keeps what was captured.</param>
    /// <param name="sleep">Pause between captures (milliseconds), so tests can run without waiting.</param>
    public static (Raster Image, int Steps, string EndReason) Run(
        Func<Raster> capture, Action<int> scroll, int notches, Func<bool> cancelled, Action<int> sleep, Options? options = null,
        Action<string>? log = null, Action<Raster, Raster>? onNoMatch = null, Action? scrollFallback = null)
    {
        var o = options ?? new Options();
        using var first = capture();
        using var acc = new ScrollAccumulator(first);
        int steps = 0, idleScrolls = 0, noMatch = 0;
        var reason = "đủ giới hạn";
        var scrolled = true;

        for (; steps < o.MaxSteps; steps++)
        {
            if (cancelled()) { reason = "đã dừng theo yêu cầu"; break; }
            if (acc.Height >= o.MaxHeight) { reason = "đạt giới hạn chiều cao"; break; }
            if (scrolled) scroll(notches);

            using var next = WaitForStill(capture, sleep, o.Settle);
            var result = acc.Add(next);
            log?.Invoke($"step {steps}: {result} lastShift={acc.LastShift} height={acc.Height}");
            switch (result)
            {
                case AddResult.Added: idleScrolls = 0; noMatch = 0; scrolled = true; break;
                case AddResult.Identical:
                    // nothing moved: either the page is at its end, or the scroll has not started yet. Try twice more.
                    if (++idleScrolls >= 3)
                    {
                        reason = acc.Height == first.Height ? "trang không cuộn" : "đã tới cuối trang";
                        goto done;
                    }
                    // the wheel did nothing at all: try the keyboard once (some pages/controls ignore the wheel)
                    if (idleScrolls == 2 && acc.Height == first.Height) scrollFallback?.Invoke();
                    scrolled = idleScrolls != 2 || scrollFallback is null; break;
                case AddResult.NoMatch:
                    // could not align: do NOT scroll again (the gap would only grow); look once more, then take the
                    // distance the last scrolls moved if the frame really changed, otherwise give up
                    scrolled = false;
                    onNoMatch?.Invoke(acc.LastFrame, next);
                    if (++noMatch >= 3) { reason = "không ghép được thêm"; goto done; }
                    sleep(300); break;
            }
        }
        done:
        return (acc.Build(), steps, reason);
    }

    /// <summary>Capture until two frames in a row are identical (the scroll animation has finished).</summary>
    static Raster WaitForStill(Func<Raster> capture, Action<int> sleep, int settle)
    {
        sleep(250);
        var prev = capture();
        for (int i = 0; i < settle; i++)
        {
            sleep(200);
            var cur = capture();
            if (ScrollStitcher.FindShift(prev, cur) == 0) { prev.Dispose(); return cur; }
            prev.Dispose(); prev = cur;
        }
        return prev;
    }
}
