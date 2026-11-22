using System.Runtime.InteropServices;
using SkiaSharp;

namespace HezbClips.Core.Imaging;

/// <summary>Straight (non-premultiplied) RGBA colour, used to read and write single pixels of a <see cref="Raster"/>.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>The app's raster image: a thin wrapper over SKBitmap, always BGRA8888 with premultiplied alpha
/// (screenshots are opaque, so premultiplication changes nothing for them). Pixel rows are available as raw bytes
/// in B,G,R,A order. Owns its bitmap; dispose when done.</summary>
public sealed class Raster : IDisposable
{
    static readonly SKImageInfo Template = new(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);

    /// <summary>Default lossy quality for JPEG and WebP (the previous encoder defaults).</summary>
    public const int DefaultQuality = 75;

    public SKBitmap Bitmap { get; }
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;
    public int Stride => Bitmap.RowBytes;

    internal Raster(SKBitmap bmp) { Bitmap = bmp; }

    /// <summary>Transparent black image.</summary>
    public Raster(int width, int height) : this(Alloc(width, height)) { }

    public Raster(int width, int height, Rgba fill) : this(Alloc(width, height))
    {
        Bitmap.Erase(new SKColor(fill.R, fill.G, fill.B, fill.A));
    }

    static SKBitmap Alloc(int w, int h)
    {
        if (w < 1 || h < 1) throw new ArgumentOutOfRangeException(nameof(w), "image size must be positive");
        var bmp = new SKBitmap(Template.WithSize(w, h));
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    // ---------- creation ----------

    /// <summary>From a tightly packed top-down BGRA buffer (e.g. a Windows capture).</summary>
    public static Raster FromBgra(byte[] bgra, int width, int height)
    {
        if (bgra.Length < (long)width * height * 4) throw new ArgumentException("buffer too small", nameof(bgra));
        var r = new Raster(width, height);
        var span = r.Bitmap.GetPixelSpan();
        var rowBytes = width * 4;
        for (int y = 0; y < height; y++)
            bgra.AsSpan(y * rowBytes, rowBytes).CopyTo(span.Slice(y * r.Stride, rowBytes));
        return r;
    }

    /// <summary>Decode any format Skia knows (PNG, JPEG, WebP, GIF first frame, BMP...).</summary>
    public static Raster Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public static Raster Load(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        return Load(ms);
    }

    public static Raster Load(Stream stream)
    {
        using var skStream = new SKManagedStream(stream);
        using var codec = SKCodec.Create(skStream) ?? throw new InvalidDataException("unknown or damaged image format");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels(), bmp.RowBytes, new SKCodecOptions(0));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new InvalidDataException($"cannot decode image ({result})");
        }
        return new Raster(bmp);
    }

    // ---------- saving ----------

    public byte[] ToPng() => Encode(SKEncodedImageFormat.Png, 100);
    public byte[] ToJpeg(int quality = DefaultQuality) => Encode(SKEncodedImageFormat.Jpeg, quality);
    public byte[] ToWebp(int quality = DefaultQuality) => Encode(SKEncodedImageFormat.Webp, quality);

    public void SavePng(string path) => File.WriteAllBytes(path, ToPng());
    public void SaveJpeg(string path, int quality = DefaultQuality) => File.WriteAllBytes(path, ToJpeg(quality));
    public void SaveWebp(string path, int quality = DefaultQuality) => File.WriteAllBytes(path, ToWebp(quality));

    byte[] Encode(SKEncodedImageFormat format, int quality)
    {
        using var pixmap = Bitmap.PeekPixels();
        using var data = pixmap.Encode(format, quality) ?? throw new InvalidOperationException($"cannot encode {format}");
        return data.ToArray();
    }

    // ---------- geometry ----------

    public Raster Clone() => new(Bitmap.Copy());

    /// <summary>Rows/columns inside the rectangle as a new image (the rectangle is clipped to the image).</summary>
    public Raster Crop(int x, int y, int w, int h)
    {
        var r = System.Drawing.Rectangle.Intersect(new(x, y, w, h), new(0, 0, Width, Height));
        if (r.Width <= 0 || r.Height <= 0) throw new ArgumentOutOfRangeException(nameof(w), "crop is outside the image");
        var dst = new Raster(r.Width, r.Height);
        var src = Bitmap.GetPixelSpan();
        var dsp = dst.Bitmap.GetPixelSpan();
        var rb = r.Width * 4;
        for (int row = 0; row < r.Height; row++)
            src.Slice((r.Y + row) * Stride + r.X * 4, rb).CopyTo(dsp.Slice(row * dst.Stride, rb));
        return dst;
    }

    /// <summary>Draw another image onto this one at (x, y), source-over.</summary>
    public void DrawImage(Raster other, int x, int y)
    {
        using var canvas = new SKCanvas(Bitmap);
        canvas.DrawBitmap(other.Bitmap, x, y, new SKSamplingOptions(SKFilterMode.Nearest), null);
    }

    /// <summary>Raw pixel row, B,G,R,A bytes.</summary>
    public ReadOnlySpan<byte> GetRow(int y) => Bitmap.GetPixelSpan().Slice(y * Stride, Width * 4);

    public Rgba this[int x, int y]
    {
        get
        {
            var p = Bitmap.GetPixelSpan().Slice(y * Stride + x * 4, 4);
            byte a = p[3];
            if (a == 255 || a == 0) return new Rgba(p[2], p[1], p[0], a);
            return new Rgba(Unpre(p[2], a), Unpre(p[1], a), Unpre(p[0], a), a);
        }
        set
        {
            var p = Bitmap.GetPixelSpan().Slice(y * Stride + x * 4, 4);
            p[0] = Pre(value.B, value.A); p[1] = Pre(value.G, value.A); p[2] = Pre(value.R, value.A); p[3] = value.A;
        }
    }

    static byte Pre(byte c, byte a) => a == 255 ? c : (byte)((c * a + 127) / 255);
    static byte Unpre(byte c, byte a) => (byte)Math.Min(255, (c * 255 + a / 2) / a);

    // ---------- resampling ----------

    /// <summary>Lanczos3 resample (separable, antialiased when shrinking) to exactly w x h.</summary>
    public Raster Resize(int w, int h)
    {
        if (w == Width && h == Height) return Clone();
        var dst = new Raster(w, h);
        var mid = new byte[(long)w * Height * 4];
        var hx = Weights(Width, w);
        var vy = Weights(Height, h);
        var src = Bitmap.GetPixelSpan().ToArray(); // spans cannot cross the lambda below
        int srcStride = Stride, srcH = Height, srcW = Width;

        Parallel.For(0, srcH, y =>
        {
            int rowIn = y * srcStride, rowOut = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                var (first, ws) = hx[x];
                float b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < ws.Length; k++)
                {
                    int i = rowIn + (first + k) * 4; var wk = ws[k];
                    b += src[i] * wk; g += src[i + 1] * wk; r += src[i + 2] * wk; a += src[i + 3] * wk;
                }
                int o = rowOut + x * 4;
                mid[o] = ToByte(b); mid[o + 1] = ToByte(g); mid[o + 2] = ToByte(r); mid[o + 3] = ToByte(a);
            }
        });

        var dstPtr = dst.Bitmap.GetPixels();
        int dstStride = dst.Stride;
        Parallel.For(0, h, y =>
        {
            var (first, ws) = vy[y];
            var row = new byte[w * 4];
            for (int x = 0; x < w; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < ws.Length; k++)
                {
                    int i = ((first + k) * w + x) * 4; var wk = ws[k];
                    b += mid[i] * wk; g += mid[i + 1] * wk; r += mid[i + 2] * wk; a += mid[i + 3] * wk;
                }
                byte ab = ToByte(a);
                row[x * 4] = Math.Min(ToByte(b), ab); row[x * 4 + 1] = Math.Min(ToByte(g), ab);
                row[x * 4 + 2] = Math.Min(ToByte(r), ab); row[x * 4 + 3] = ab; // premultiplied: colour never exceeds alpha
            }
            Marshal.Copy(row, 0, dstPtr + y * dstStride, row.Length);
        });
        return dst;
    }

    static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);

    static double Lanczos3(double x)
    {
        x = Math.Abs(x);
        if (x < 1e-8) return 1;
        if (x >= 3) return 0;
        var px = Math.PI * x;
        return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
    }

    /// <summary>For each output index: first source index and normalised kernel weights.</summary>
    static (int First, float[] W)[] Weights(int srcLen, int dstLen)
    {
        double scale = (double)srcLen / dstLen;
        double support = 3 * Math.Max(1, scale);
        double stretch = Math.Max(1, scale);
        var result = new (int, float[])[dstLen];
        for (int i = 0; i < dstLen; i++)
        {
            double center = (i + 0.5) * scale;
            int lo = Math.Max(0, (int)Math.Floor(center - support + 0.5));
            int hi = Math.Min(srcLen - 1, (int)Math.Ceiling(center + support - 0.5));
            if (hi < lo) { lo = hi = Math.Clamp((int)center, 0, srcLen - 1); }
            var ws = new float[hi - lo + 1];
            double sum = 0;
            for (int k = 0; k < ws.Length; k++)
            {
                var wv = Lanczos3((lo + k + 0.5 - center) / stretch);
                ws[k] = (float)wv; sum += wv;
            }
            if (Math.Abs(sum) < 1e-9) { ws = new float[] { 1f }; lo = Math.Clamp((int)center, 0, srcLen - 1); sum = 1; }
            else for (int k = 0; k < ws.Length; k++) ws[k] = (float)(ws[k] / sum);
            result[i] = (lo, ws);
        }
        return result;
    }

    /// <summary>Gaussian unsharp (sigma, amount 1): out = 2*src - blur on the colour channels. Returns a new image.</summary>
    public Raster Sharpen(double sigma = 0.9)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var k = new float[radius * 2 + 1];
        float sum = 0;
        for (int i = -radius; i <= radius; i++) { k[i + radius] = (float)Math.Exp(-(i * i) / (2 * sigma * sigma)); sum += k[i + radius]; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        int w = Width, h = Height, stride = Stride;
        var src = Bitmap.GetPixelSpan().ToArray();
        var tmp = new float[(long)w * h * 3];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float b = 0, g = 0, r = 0;
                for (int t = -radius; t <= radius; t++)
                {
                    int xx = Math.Clamp(x + t, 0, w - 1); int i = y * stride + xx * 4; var wk = k[t + radius];
                    b += src[i] * wk; g += src[i + 1] * wk; r += src[i + 2] * wk;
                }
                int o = (y * w + x) * 3; tmp[o] = b; tmp[o + 1] = g; tmp[o + 2] = r;
            }
        });
        var dst = new Raster(w, h);
        var dstPtr = dst.Bitmap.GetPixels(); int dstStride = dst.Stride;
        Parallel.For(0, h, y =>
        {
            var row = new byte[w * 4];
            for (int x = 0; x < w; x++)
            {
                float b = 0, g = 0, r = 0;
                for (int t = -radius; t <= radius; t++)
                {
                    int yy = Math.Clamp(y + t, 0, h - 1); int o = (yy * w + x) * 3; var wk = k[t + radius];
                    b += tmp[o] * wk; g += tmp[o + 1] * wk; r += tmp[o + 2] * wk;
                }
                int i = y * stride + x * 4;
                byte a = src[i + 3];
                row[x * 4] = Math.Min(ToByte(2 * src[i] - b), a);
                row[x * 4 + 1] = Math.Min(ToByte(2 * src[i + 1] - g), a);
                row[x * 4 + 2] = Math.Min(ToByte(2 * src[i + 2] - r), a);
                row[x * 4 + 3] = a;
            }
            Marshal.Copy(row, 0, dstPtr + y * dstStride, row.Length);
        });
        return dst;
    }

    public void Dispose() => Bitmap.Dispose();
}
