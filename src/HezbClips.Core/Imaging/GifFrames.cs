using SkiaSharp;

namespace HezbClips.Core.Imaging;

/// <summary>Decodes an animated GIF into fully composed frames (disposal and partial frames already applied).</summary>
public static class GifFrames
{
    /// <param name="maxFrames">Stop after this many frames.</param>
    /// <param name="maxWidth">Frames wider than this are shrunk (aspect kept); 0 = keep the size.</param>
    /// <returns>Each frame with its display time in milliseconds (at least 20).</returns>
    public static List<(Raster Frame, int Ms)> Decode(string path, int maxFrames, int maxWidth)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("unknown or damaged image format");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frameInfo = codec.FrameInfo;
        int count = Math.Min(Math.Max(1, frameInfo.Length), Math.Max(1, maxFrames));
        var result = new List<(Raster, int)>(count);

        int tw = info.Width, th = info.Height;
        if (maxWidth > 0 && tw > maxWidth) { th = Math.Max(1, (int)Math.Round(th * (double)maxWidth / tw)); tw = maxWidth; }

        // a full-size frame stays in memory only while a later frame still builds on it
        var lastUse = new int[count];
        for (int i = 0; i < count; i++) lastUse[i] = -1;
        for (int i = 0; i < count && i < frameInfo.Length; i++)
        {
            var req = frameInfo[i].RequiredFrame;
            if (req >= 0 && req < count) lastUse[req] = i;
        }
        var fulls = new SKBitmap?[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                var bmp = new SKBitmap(info);
                int req = i < frameInfo.Length ? frameInfo[i].RequiredFrame : -1;
                if (req >= 0 && fulls[req] is { } prior) prior.GetPixelSpan().CopyTo(bmp.GetPixelSpan());
                else { bmp.Erase(SKColors.Transparent); req = -1; }
                var opts = new SKCodecOptions(i) { PriorFrame = req };
                var rc = codec.GetPixels(info, bmp.GetPixels(), bmp.RowBytes, opts);
                if (rc is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) { bmp.Dispose(); break; }
                fulls[i] = bmp;

                using var full = new Raster(bmp.Copy());
                var shown = (tw == info.Width && th == info.Height) ? full.Clone() : full.Resize(tw, th);
                var ms = i < frameInfo.Length ? frameInfo[i].Duration : 100;
                result.Add((shown, Math.Max(20, ms)));

                for (int j = 0; j <= i; j++)
                    if (fulls[j] is { } f && lastUse[j] <= i && j != i) { f.Dispose(); fulls[j] = null; }
                if (lastUse[i] <= i) { fulls[i]?.Dispose(); fulls[i] = null; }
            }
        }
        finally { foreach (var f in fulls) f?.Dispose(); }
        return result;
    }
}
