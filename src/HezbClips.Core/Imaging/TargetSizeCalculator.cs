using HezbClips.Core.Settings;

namespace HezbClips.Core.Imaging;

public static class TargetSizeCalculator
{
    public static (int W, int H) Calculate(int srcW, int srcH, ExportQuality q)
    {
        long bh = (int)q;
        long bw = bh * 16 / 9;
        long w, h;
        if ((long)srcW * bh >= (long)srcH * bw) { w = bw; h = srcH * bw / srcW; }
        else { h = bh; w = srcW * bh / srcH; }
        return ((int)(w & ~1L), (int)(h & ~1L));
    }
}
