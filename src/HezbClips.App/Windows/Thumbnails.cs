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
using SixLabors.ImageSharp.Processing;

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
        catch (Exception) { return null; }
        finally { Gate.Release(); }
    }

    static ImageSource? Build(MediaItem item, string? ffmpeg, string cacheDir)
    {
        if (item.Kind == MediaKind.Video) return VideoFrame(item, ffmpeg, cacheDir);
        try { return Decode(item.Path); }
        catch (Exception) { return ViaImageSharp(item.Path); }
    }

    static BitmapImage Decode(string path)
    {
        var b = new BitmapImage();
        b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = Width; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit();
        b.Freeze(); return b;
    }

    static ImageSource? ViaImageSharp(string path)
    {
        try
        {
            using var img = SixLabors.ImageSharp.Image.Load(path);
            img.Mutate(c => c.Resize(Width, 0));
            using var ms = new MemoryStream();
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms);
            ms.Position = 0;
            var b = new BitmapImage();
            b.BeginInit(); b.StreamSource = ms; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
            return b;
        }
        catch (Exception) { return null; }
    }

    static ImageSource? VideoFrame(MediaItem item, string? ffmpeg, string cacheDir)
    {
        if (ffmpeg is null) return null;
        Directory.CreateDirectory(cacheDir);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(item.Path + item.Modified.Ticks + item.Size)));
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
