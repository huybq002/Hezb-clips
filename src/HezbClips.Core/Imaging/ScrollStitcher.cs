using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.Core.Imaging;

/// <summary>Joins successive screenshots of a scrolling page into one tall image by finding how far each
/// frame moved (the vertical shift that makes the overlapping rows match).</summary>
public static class ScrollStitcher
{
    public const int NoMatch = -1;
    const int ColStep = 6;       // sample every 6th column
    const double MaxDiff = 5.0;  // mean absolute luminance difference accepted as "same rows"

    /// <summary>How many rows the content moved up between prev and next: 0 = identical (end of page), NoMatch = could not align.</summary>
    public static int FindShift(Image<Rgba32> prev, Image<Rgba32> next)
    {
        if (prev.Width != next.Width || prev.Height != next.Height) return NoMatch;
        var a = Luma(prev); var b = Luma(next);
        int h = prev.Height;
        if (MeanDiff(a, b, 0, h) <= 1.0) return 0;
        // candidate shifts from small to large; require a clear match
        var best = NoMatch; var bestDiff = double.MaxValue;
        for (int s = Math.Max(2, h / 40); s <= h - Math.Max(16, h / 8); s++)
        {
            var d = MeanDiff(a, b, s, h);
            if (d < bestDiff) { bestDiff = d; best = s; }
        }
        return bestDiff <= MaxDiff ? best : NoMatch;
    }

    /// <summary>Mean abs difference between prev rows [s, h) and next rows [0, h - s).</summary>
    static double MeanDiff(byte[][] a, byte[][] b, int s, int h)
    {
        long sum = 0, n = 0;
        int rows = h - s;
        int rowStep = Math.Max(1, rows / 120);
        for (int y = 0; y < rows; y += rowStep)
        {
            var ra = a[y + s]; var rb = b[y];
            for (int x = 0; x < ra.Length; x++) sum += Math.Abs(ra[x] - rb[x]);
            n += ra.Length;
        }
        return n == 0 ? double.MaxValue : (double)sum / n;
    }

    static byte[][] Luma(Image<Rgba32> img)
    {
        var rows = new byte[img.Height][];
        var cols = (img.Width + ColStep - 1) / ColStep;
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var span = acc.GetRowSpan(y);
                var r = new byte[cols];
                for (int i = 0, x = 0; x < span.Length; x += ColStep, i++)
                    r[i] = (byte)((span[x].R * 299 + span[x].G * 587 + span[x].B * 114) / 1000);
                rows[y] = r;
            }
        });
        return rows;
    }

    /// <summary>Stitch frames in order. Stops at the first frame identical to the previous one or that cannot be aligned.
    /// Returns the tall image and how many frames were used.</summary>
    public static (Image<Rgba32> Image, int Used) Stitch(IReadOnlyList<Image<Rgba32>> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        var shifts = new List<int>();
        for (int i = 1; i < frames.Count; i++)
        {
            var s = FindShift(frames[i - 1], frames[i]);
            if (s <= 0) break;
            shifts.Add(s);
        }
        return (Build(frames, shifts), shifts.Count + 1);
    }

    /// <summary>The newly revealed bottom rows of a frame after it moved by `shift` rows.</summary>
    public static Image<Rgba32> Strip(Image<Rgba32> frame, int shift) =>
        frame.Clone(c => c.Crop(new Rectangle(0, frame.Height - shift, frame.Width, shift)));

    /// <summary>First frame plus the strips revealed by each later frame, joined top to bottom.</summary>
    public static Image<Rgba32> Assemble(Image<Rgba32> first, IReadOnlyList<Image<Rgba32>> strips)
    {
        var result = new Image<Rgba32>(first.Width, first.Height + strips.Sum(s => s.Height));
        result.Mutate(c => c.DrawImage(first, new Point(0, 0), 1f));
        var y = first.Height;
        foreach (var strip in strips) { var at = y; result.Mutate(c => c.DrawImage(strip, new Point(0, at), 1f)); y += strip.Height; }
        return result;
    }

    /// <summary>Build the tall image from known per-frame shifts.</summary>
    public static Image<Rgba32> Build(IReadOnlyList<Image<Rgba32>> frames, IReadOnlyList<int> shifts)
    {
        var w = frames[0].Width; var h = frames[0].Height;
        var total = h + shifts.Sum();
        var result = new Image<Rgba32>(w, total);
        result.Mutate(c => c.DrawImage(frames[0], new Point(0, 0), 1f));
        var y = h;
        for (int i = 0; i < shifts.Count; i++)
        {
            var s = shifts[i];
            using var strip = frames[i + 1].Clone(c => c.Crop(new Rectangle(0, h - s, w, s)));
            var at = y;
            result.Mutate(c => c.DrawImage(strip, new Point(0, at), 1f));
            y += s;
        }
        return result;
    }
}
