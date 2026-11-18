using Rectangle = System.Drawing.Rectangle;

namespace HezbClips.Core.Imaging;

/// <summary>Maps screen / selection rectangles onto a single stitched desktop screenshot.</summary>
public static class CropMath
{
    static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>Where a screen sits inside a screenshot of the whole desktop. Handles screenshots whose
    /// pixel size differs from the desktop size (scaling).</summary>
    public static Rectangle ScreenRegionInImage(Rectangle screen, Rectangle desktop, int imgW, int imgH)
    {
        double sx = (double)imgW / desktop.Width, sy = (double)imgH / desktop.Height;
        return Clamp(new Rectangle(R((screen.X - desktop.X) * sx), R((screen.Y - desktop.Y) * sy),
            R(screen.Width * sx), R(screen.Height * sy)), imgW, imgH);
    }

    /// <summary>A selection drawn in DIP over a window that shows <paramref name="shown"/> stretched to
    /// (winW x winH); returns the matching pixel rectangle in screenshot coordinates.</summary>
    public static Rectangle SelectionToImage(double x, double y, double w, double h,
        double winW, double winH, Rectangle shown)
    {
        double sx = shown.Width / winW, sy = shown.Height / winH;
        var r = new Rectangle(shown.X + R(x * sx), shown.Y + R(y * sy), R(w * sx), R(h * sy));
        return Rectangle.Intersect(r, shown);
    }

    static Rectangle Clamp(Rectangle r, int w, int h) => Rectangle.Intersect(r, new Rectangle(0, 0, w, h));

    public static Rectangle Union(IEnumerable<Rectangle> rects) =>
        rects.Aggregate(Rectangle.Union);
}
