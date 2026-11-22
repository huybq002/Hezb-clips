using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HezbClips.Core.Output;
using HezbClips.Core.Video;

namespace HezbClips.App.Windows;

/// <summary>Builds small preview images for the library. Images/GIFs decode directly (WebP via ImageSharp),
/// videos use one ffmpeg-extracted frame cached on disk.</summary>
public static class Thumbnails
{
    const int Width = 320;
    static readonly SemaphoreSlim Gate = new(4);

    public static async Task<ImageSource?> LoadAsync(MediaItem item, string? ffmpeg, string cacheDir)
    {
        await Gate.WaitAsync();
        try { return await Task.Run(() => Build(item, ffmpeg, cacheDir)); }
        catch (Exception) { return null; } // unreadable file: the card keeps its placeholder
        finally { Gate.Release(); }
    }

    static ImageSource? Build(MediaItem item, string? ffmpeg, string cacheDir)
    {
        if (item.Kind == MediaKind.Video) return VideoFrame(item, ffmpeg, cacheDir);
        // small JPEG per file on disk: reopening the library never decodes the big originals again
        var cached = Path.Combine(cacheDir, Key(item) + ".jpg");
        try { if (File.Exists(cached)) return Decode(cached); } catch (Exception) { /* damaged cache entry: rebuild */ }
        ImageSource? bmp;
        try { bmp = Decode(item.Path); }
        catch (Exception) { bmp = ViaSkia(item.Path); }
        if (bmp is BitmapSource src) SaveCache(src, cached);
        return bmp;
    }

    static BitmapImage Decode(string path)
    {
        var b = new BitmapImage();
        b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = Width; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit();
        b.Freeze(); return b;
    }

    static ImageSource? ViaSkia(string path)
    {
        try
        {
            using var full = HezbClips.Core.Imaging.Raster.Load(path);
            using var img = full.Resize(Width, Math.Max(1, (int)Math.Round(full.Height * (double)Width / full.Width)));
            using var ms = new MemoryStream(img.ToPng());
            var b = new BitmapImage();
            b.BeginInit(); b.StreamSource = ms; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
            return b;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Delete cached thumbnails not written for a long time (their files were deleted or changed).</summary>
    public static void CleanCache(string cacheDir, TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(cacheDir)) return;
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.jpg"))
            {
                try { if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception) { /* best effort */ }
    }

    static string Key(MediaItem item) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(item.Path + item.Modified.Ticks + item.Size)));

    static void SaveCache(BitmapSource src, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var enc = new JpegBitmapEncoder { QualityLevel = 82 };
            enc.Frames.Add(BitmapFrame.Create(src));
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) enc.Save(fs);
            File.Move(tmp, path, true);
        }
        catch (Exception) { /* read-only profile or disk full: just no cache */ }
    }

    static ImageSource? VideoFrame(MediaItem item, string? ffmpeg, string cacheDir)
    {
        if (ffmpeg is null) return null;
        Directory.CreateDirectory(cacheDir);
        var key = Key(item);
        var thumb = Path.Combine(cacheDir, key + ".jpg");
        if (!File.Exists(thumb))
        {
            Run(ffmpeg, $"-y -ss 0.3 -i \"{item.Path}\" -frames:v 1 -vf scale={Width}:-2 -q:v 4 \"{thumb}\"");
            if (!File.Exists(thumb)) Run(ffmpeg, $"-y -i \"{item.Path}\" -frames:v 1 -vf scale={Width}:-2 -q:v 4 \"{thumb}\"");
        }
        return File.Exists(thumb) ? Decode(thumb) : null;
    }

    static void Run(string ffmpeg, string args)
    {
        var p = new FfmpegProcess(ffmpeg);
        p.Start(args);
        p.ExitAsync().GetAwaiter().GetResult();
    }
}
