using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.Core.Imaging;

/// <summary>One scroll step: how far the content moved, and how many rows at the top/bottom stayed put
/// (sticky header/footer, or blank space) and were left out of the matching.</summary>
public readonly record struct ScrollStep(int Shift, int Top, int Bottom);

/// <summary>Joins successive screenshots of a scrolling page into one tall image by finding how far each
/// frame moved. Sticky headers/footers and the moving scrollbar are ignored while matching.</summary>
public static class ScrollStitcher
{
    public const int NoMatch = -1;
    const int ColStep = 4;          // sample every 4th column
    const double MaxDiff = 6.0;     // mean absolute luminance difference accepted as "same rows"
    const double StaticRow = 1.5;   // a row this close in both frames did not move

    /// <summary>How many rows the content moved up between prev and next: 0 = identical (end of page), NoMatch = could not align.</summary>
    public static int FindShift(Image<Rgba32> prev, Image<Rgba32> next) => Align(prev, next).Shift;

    public static ScrollStep Align(Image<Rgba32> prev, Image<Rgba32> next)
    {
        if (prev.Width != next.Width || prev.Height != next.Height) return new(NoMatch, 0, 0);
        var a = Luma(prev); var b = Luma(next);
        int h = prev.Height;

        int top = 0, bottom = 0;
        while (top < h / 3 && RowDiff(a[top], b[top]) <= StaticRow) top++;
        while (bottom < h / 3 && RowDiff(a[h - 1 - bottom], b[h - 1 - bottom]) <= StaticRow) bottom++;
        if (top + bottom >= h - 8 || MeanDiff(a, b, 0, 0, h) <= 1.0) return new(0, top, bottom);

        int band = h - top - bottom;
        int lo = Math.Max(2, band / 40), hi = band - Math.Max(12, band / 12);
        if (hi <= lo) return new(NoMatch, top, bottom);
        var diffs = new double[hi + 1];
        var best = NoMatch; var bestDiff = double.MaxValue;
        for (int s = lo; s <= hi; s++)
        {
            var d = MeanDiff(a, b, top + s, top, band - s);
            diffs[s] = d;
            if (d < bestDiff) { bestDiff = d; best = s; }
        }
        if (bestDiff > MaxDiff) return new(NoMatch, top, bottom);
        // blank or repetitive content matches almost everywhere: refuse instead of guessing a wrong shift
        var second = double.MaxValue;
        for (int s = lo; s <= hi; s++) if (Math.Abs(s - best) > 4 && diffs[s] < second) second = diffs[s];
        if (second - bestDiff < 0.75) return new(NoMatch, top, bottom);
        return new(best, top, bottom);
    }

    static double RowDiff(byte[] x, byte[] y)
    {
        long sum = 0;
        for (int i = 0; i < x.Length; i++) sum += Math.Abs(x[i] - y[i]);
        return x.Length == 0 ? 0 : (double)sum / x.Length;
    }

    /// <summary>Mean abs difference between prev rows [pa, pa+rows) and next rows [pb, pb+rows).</summary>
    static double MeanDiff(byte[][] a, byte[][] b, int pa, int pb, int rows)
    {
        if (rows <= 0) return double.MaxValue;
        long sum = 0, n = 0;
        int rowStep = Math.Max(1, rows / 160);
        for (int y = 0; y < rows; y += rowStep)
        {
            var ra = a[pa + y]; var rb = b[pb + y];
            for (int x = 0; x < ra.Length; x++) sum += Math.Abs(ra[x] - rb[x]);
            n += ra.Length;
        }
        return n == 0 ? double.MaxValue : (double)sum / n;
    }

    static byte[][] Luma(Image<Rgba32> img)
    {
        var rows = new byte[img.Height][];
        // leave out the right edge: the scrollbar thumb moves every step
        var usable = Math.Max(ColStep, img.Width - Math.Min(24, img.Width / 20));
        var cols = (usable + ColStep - 1) / ColStep;
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var span = acc.GetRowSpan(y);
                var r = new byte[cols];
                for (int i = 0, x = 0; x < usable && i < cols; x += ColStep, i++)
                    r[i] = (byte)((span[x].R * 299 + span[x].G * 587 + span[x].B * 114) / 1000);
                rows[y] = r;
            }
        });
        return rows;
    }

    /// <summary>Rows [y, y+height) of an image as a new image.</summary>
    public static Image<Rgba32> Rows(Image<Rgba32> img, int y, int height) =>
        img.Clone(c => c.Crop(new Rectangle(0, y, img.Width, height)));

    /// <summary>Join images top to bottom (all the same width).</summary>
    public static Image<Rgba32> Assemble(IReadOnlyList<Image<Rgba32>> parts)
    {
        var result = new Image<Rgba32>(parts[0].Width, Math.Max(1, parts.Sum(p => p.Height)));
        var y = 0;
        foreach (var part in parts)
        {
            if (part.Height == 0) continue;
            var at = y; result.Mutate(c => c.DrawImage(part, new Point(0, at), 1f)); y += part.Height;
        }
        return result;
    }

    /// <summary>Stitch frames in order, stopping at the first one that is identical or cannot be aligned.
    /// Returns the tall image and how many frames were used.</summary>
    public static (Image<Rgba32> Image, int Used) Stitch(IReadOnlyList<Image<Rgba32>> frames)
    {
        using var acc = new ScrollAccumulator(frames[0]);
        int used = 1;
        for (int i = 1; i < frames.Count && acc.Add(frames[i]); i++) used++;
        return (acc.Build(), used);
    }
}

/// <summary>Builds the tall image step by step, keeping only the newly revealed rows of each frame in memory.
/// The bottom static band (sticky footer) found in the first step is kept fixed so the strips stay contiguous,
/// and is added back once at the very end; a sticky header only ever comes from the first frame.</summary>
public sealed class ScrollAccumulator : IDisposable
{
    readonly Image<Rgba32> _first;
    Image<Rgba32> _last;
    readonly List<Image<Rgba32>> _strips = new();
    int? _bottom;

    public ScrollAccumulator(Image<Rgba32> first) { _first = first.Clone(); _last = first.Clone(); Height = first.Height; }

    /// <summary>Total height so far.</summary>
    public int Height { get; private set; }

    /// <summary>Add the next frame. False when it is the end of the page or could not be aligned (frame not used).</summary>
    public bool Add(Image<Rgba32> next)
    {
        var step = ScrollStitcher.Align(_last, next);
        if (step.Shift <= 0) return false;
        _bottom ??= step.Bottom;
        var h = next.Height; var b = _bottom.Value; var s = step.Shift;
        if (h - b - s < 0) return false;
        _strips.Add(ScrollStitcher.Rows(next, h - b - s, s));
        _last.Dispose(); _last = next.Clone();
        Height += s;
        return true;
    }

    public Image<Rgba32> Build()
    {
        var h = _first.Height; var b = _bottom ?? 0;
        var parts = new List<Image<Rgba32>> { ScrollStitcher.Rows(_first, 0, h - b) };
        parts.AddRange(_strips);
        if (b > 0) parts.Add(ScrollStitcher.Rows(_last, h - b, b));
        try { return ScrollStitcher.Assemble(parts); }
        finally { parts[0].Dispose(); if (b > 0) parts[^1].Dispose(); }
    }

    public void Dispose()
    {
        _first.Dispose(); _last.Dispose();
        foreach (var s in _strips) s.Dispose();
        _strips.Clear();
    }
}
