using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HezbClips.App.Windows;

/// <summary>Plays an animated GIF in a loop with no click needed. Frames are decoded fully composed (Skia),
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
        // no point animating a hidden window (minimised to the tray, or behind a capture)
        IsVisibleChanged += (_, e) =>
        {
            if (_frames is null || _frames.Count < 2) return;
            if ((bool)e.NewValue) _timer.Start(); else _timer.Stop();
        };
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
        if (_frames.Count > 1 && IsVisible) _timer.Start(); else _timer.Stop();
    }

    List<(BitmapSource, int)> Decode()
    {
        var decoded = HezbClips.Core.Imaging.GifFrames.Decode(_path, _maxFrames, _width);
        var list = new List<(BitmapSource, int)>(decoded.Count);
        foreach (var (raster, ms) in decoded)
        {
            using (raster)
            {
                var bmp = BitmapSource.Create(raster.Width, raster.Height, 96, 96, PixelFormats.Pbgra32, null,
                    raster.Bitmap.GetPixels(), raster.Stride * raster.Height, raster.Stride);
                bmp.Freeze();
                list.Add((bmp, ms));
            }
        }
        return list;
    }
}
