using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HezbClips.App.Windows;

/// <summary>Plays an animated GIF in a loop with no click needed. Frames are decoded fully composed (ImageSharp),
/// shrunk to the display size, and capped to the first few seconds to keep memory small.</summary>
public sealed class GifPlayer : System.Windows.Controls.Image
{
    readonly string _path;
    readonly int _width, _maxFrames;
    readonly DispatcherTimer _timer = new();
    List<(BitmapSource Frame, int Ms)>? _frames;
    int _index;
    bool _loading;

    public GifPlayer(string path, int decodeWidth, int maxFrames)
    {
        _path = path; _width = decodeWidth; _maxFrames = maxFrames;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer.Tick += (_, _) => Next();
        Loaded += async (_, _) => await StartAsync();
        Unloaded += (_, _) => { _timer.Stop(); _frames = null; Source = null; };
    }

    async Task StartAsync()
    {
        if (_loading || _frames is not null) return;
        _loading = true;
        try
        {
            var frames = await Task.Run(Decode);
            if (!IsLoaded || frames.Count == 0) return;
            _frames = frames; _index = -1;
            Next();
        }
        catch (Exception) { /* unreadable GIF: leave empty */ }
        finally { _loading = false; }
    }

    void Next()
    {
        if (_frames is null || _frames.Count == 0) return;
        _index = (_index + 1) % _frames.Count;
        var (frame, ms) = _frames[_index];
        Source = frame;
        _timer.Interval = TimeSpan.FromMilliseconds(ms);
        if (_frames.Count > 1) _timer.Start(); else _timer.Stop();
    }

    List<(BitmapSource, int)> Decode()
    {
        var opts = new DecoderOptions { MaxFrames = (uint)_maxFrames };
        using var img = SixLabors.ImageSharp.Image.Load<Bgra32>(opts, _path);
        if (img.Width > _width) img.Mutate(c => c.Resize(_width, 0));
        var list = new List<(BitmapSource, int)>(img.Frames.Count);
        var buf = new byte[img.Width * img.Height * 4];
        for (int i = 0; i < img.Frames.Count; i++)
        {
            var f = img.Frames[i];
            f.CopyPixelDataTo(buf);
            var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, buf, img.Width * 4);
            bmp.Freeze();
            var delay = f.Metadata.GetGifMetadata().FrameDelay; // 1/100 s
            list.Add((bmp, Math.Max(2, delay) * 10));
        }
        return list;
    }
}
