using System.Drawing;

namespace HezbClips.Core.Imaging;

public static class DpiMath
{
    public static Rectangle ToPhysical(double x, double y, double w, double h, double scale)
    {
        static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return new Rectangle(R(x * scale), R(y * scale), R(w * scale), R(h * scale));
    }
}
