using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class RasterTests
{
    static Raster Gradient(int w, int h)
    {
        var r = new Raster(w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) r[x, y] = new Rgba((byte)(x * 255 / w), (byte)(y * 255 / h), 90, 255);
        return r;
    }

    static string Tmp(string ext) => Path.Combine(Directory.CreateTempSubdirectory().FullName, "t." + ext);

    [Fact] public void Pixel_get_set_roundtrip()
    {
        using var r = new Raster(4, 3, new Rgba(1, 2, 3, 255));
        r[2, 1] = new Rgba(200, 100, 50, 255);
        Assert.Equal(new Rgba(200, 100, 50, 255), r[2, 1]);
        Assert.Equal(new Rgba(1, 2, 3, 255), r[0, 0]);
        Assert.Equal((4, 3), (r.Width, r.Height));
    }

    [Fact] public void New_image_is_transparent()
    {
        using var r = new Raster(2, 2);
        Assert.Equal(0, r[1, 1].A);
    }

    [Fact] public void Png_roundtrip_is_lossless()
    {
        using var src = Gradient(40, 30); var p = Tmp("png"); src.SavePng(p);
        using var back = Raster.Load(p);
        Assert.Equal((40, 30), (back.Width, back.Height));
        Assert.Equal(src[7, 9], back[7, 9]); Assert.Equal(src[39, 29], back[39, 29]);
    }

    [Theory] [InlineData("jpg")] [InlineData("webp")]
    public void Lossy_roundtrip_keeps_size_and_colour(string ext)
    {
        using var src = new Raster(64, 48, new Rgba(220, 40, 40, 255)); var p = Tmp(ext);
        if (ext == "jpg") src.SaveJpeg(p); else src.SaveWebp(p);
        using var back = Raster.Load(p);
        Assert.Equal((64, 48), (back.Width, back.Height));
        var c = back[30, 20];
        Assert.InRange(c.R, 205, 235); Assert.InRange(c.G, 25, 60); Assert.InRange(c.B, 25, 60); Assert.Equal(255, c.A);
    }

    [Fact] public void Load_from_bytes_and_bad_data()
    {
        using var src = Gradient(10, 10);
        using var back = Raster.Load(src.ToPng());
        Assert.Equal(10, back.Width);
        Assert.Throws<InvalidDataException>(() => Raster.Load(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Fact] public void Resize_gives_exact_size_and_keeps_flat_colour()
    {
        using var src = new Raster(100, 50, new Rgba(10, 200, 30, 255));
        using var up = src.Resize(333, 171);
        using var down = src.Resize(17, 9);
        Assert.Equal((333, 171), (up.Width, up.Height));
        Assert.Equal((17, 9), (down.Width, down.Height));
        Assert.Equal(new Rgba(10, 200, 30, 255), up[150, 80]);
        Assert.Equal(new Rgba(10, 200, 30, 255), down[8, 4]);
    }

    [Fact] public void Resize_keeps_edge_position()
    {
        using var src = new Raster(100, 10, new Rgba(0, 0, 0, 255));
        for (int y = 0; y < 10; y++) for (int x = 50; x < 100; x++) src[x, y] = new Rgba(255, 255, 255, 255);
        using var big = src.Resize(200, 20);
        Assert.True(big[90, 5].R < 30); Assert.True(big[110, 5].R > 225);
    }

    [Fact] public void Sharpen_leaves_flat_areas_and_boosts_edges()
    {
        using var src = new Raster(20, 5, new Rgba(100, 100, 100, 255));
        for (int y = 0; y < 5; y++) for (int x = 10; x < 20; x++) src[x, y] = new Rgba(150, 150, 150, 255);
        using var s = src.Sharpen(0.9);
        Assert.Equal(100, s[2, 2].R); Assert.Equal(150, s[17, 2].R);
        Assert.True(s[9, 2].R < 100); Assert.True(s[10, 2].R > 150);
    }

    [Fact] public void Crop_copies_the_rectangle()
    {
        using var src = Gradient(50, 40);
        using var c = src.Crop(10, 5, 20, 15);
        Assert.Equal((20, 15), (c.Width, c.Height));
        Assert.Equal(src[10, 5], c[0, 0]); Assert.Equal(src[29, 19], c[19, 14]);
    }

    [Fact] public void DrawImage_places_at_offset()
    {
        using var dst = new Raster(30, 30, new Rgba(0, 0, 0, 255));
        using var piece = new Raster(10, 10, new Rgba(255, 0, 0, 255));
        dst.DrawImage(piece, 5, 8);
        Assert.Equal(new Rgba(255, 0, 0, 255), dst[5, 8]); Assert.Equal(new Rgba(255, 0, 0, 255), dst[14, 17]);
        Assert.Equal(new Rgba(0, 0, 0, 255), dst[15, 18]); Assert.Equal(new Rgba(0, 0, 0, 255), dst[4, 8]);
    }

    [Fact] public void FromBgra_maps_channels()
    {
        using var r = Raster.FromBgra(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }, 2, 1);
        Assert.Equal(new Rgba(30, 20, 10, 255), r[0, 0]); Assert.Equal(new Rgba(60, 50, 40, 255), r[1, 0]);
    }
}
