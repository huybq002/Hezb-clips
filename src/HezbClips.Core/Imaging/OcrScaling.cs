namespace HezbClips.Core.Imaging;

/// <summary>Chooses how much to enlarge a picture before OCR. Tesseract reads best when letters are about 25-35 px tall: small UI text
/// needs 2-3x, but text that is already large gets worse when enlarged, so engines that report confidence try 1x first and
/// enlarge only while the result is unsure.</summary>
public static class OcrScaling
{
    /// <summary>Mean word confidence (0..100) at or above which a pass is accepted without trying a bigger scale.</summary>
    public const double GoodEnough = 93;

    /// <summary>Scales to try in order, 1 first. Pictures that are already big are never enlarged; nothing exceeds <paramref name="maxDim"/>.</summary>
    public static IReadOnlyList<double> Candidates(int width, int height, int maxDim)
    {
        var list = new List<double> { 1 };
        if (height < 700 && width < 2400)
        {
            foreach (var k in new[] { 2.0, 3.0 })
                if (Math.Max(width, height) * k <= maxDim && (k < 3 || height < 350)) list.Add(k);
        }
        return list;
    }

    /// <summary>One fixed scale for engines without confidence (Windows OCR): 3x for short pictures, 2x for medium, never past the limit.</summary>
    public static double Fixed(int width, int height, int maxDim)
    {
        var scale = 1.0;
        if (height < 700 && width < 2400) scale = height < 350 ? 3 : 2;
        if (Math.Max(width, height) * scale > maxDim) scale = (double)maxDim / Math.Max(width, height);
        return scale;
    }

    /// <summary>Average confidence weighted by word length; null when the engine gave none, 0 when there are no words.</summary>
    public static double? MeanConfidence(IEnumerable<OcrLineInfo> lines)
    {
        double sum = 0, len = 0; var any = false;
        foreach (var w in lines.SelectMany(l => l.Words))
        {
            if (w.Confidence is not { } c) return null;
            any = true; sum += c * w.Text.Length; len += w.Text.Length;
        }
        return !any || len == 0 ? 0 : sum / len;
    }
}
