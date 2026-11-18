using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DRect = System.Drawing.Rectangle;

namespace HezbClips.Core.Imaging;

/// <summary>Cuts a region out of frozen per-monitor screenshots, stitching the pieces when the region spans monitors.</summary>
public static class FrozenComposer
{
    /// <param name="screens">Screenshot of each monitor keyed by its bounds in virtual-screen (physical) pixels.</param>
    /// <returns>The region as one image (areas outside every monitor stay black), or null when it touches no monitor.</returns>
    public static Image<Rgba32>? Compose(IReadOnlyDictionary<DRect, Image<Rgba32>> screens, DRect region)
    {
        var parts = screens.Select(kv => (Bounds: kv.Key, Img: kv.Value, Part: DRect.Intersect(kv.Key, region)))
                           .Where(x => x.Part.Width > 0 && x.Part.Height > 0).ToList();
        if (parts.Count == 0) return null;
        var result = new Image<Rgba32>(region.Width, region.Height, new Rgba32(0, 0, 0, 255));
        foreach (var (bounds, img, part) in parts)
        {
            var src = new Rectangle(part.X - bounds.X, part.Y - bounds.Y, part.Width, part.Height);
            src.Intersect(new Rectangle(0, 0, img.Width, img.Height)); // screenshot may be a pixel short of the bounds
            if (src.Width <= 0 || src.Height <= 0) continue;
            using var piece = img.Clone(c => c.Crop(src));
            var at = new Point(part.X - region.X, part.Y - region.Y);
            result.Mutate(c => c.DrawImage(piece, at, 1f));
        }
        return result;
    }
}
