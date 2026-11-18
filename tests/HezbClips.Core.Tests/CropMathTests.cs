using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class CropMathTests
{
    [Fact] public void Second_monitor_is_offset_in_image() =>
        Assert.Equal(new Rectangle(1920, 0, 1280, 720),
            CropMath.ScreenRegionInImage(new Rectangle(1920, 0, 1280, 720), new Rectangle(0, 0, 3200, 1080), 3200, 1080));

    [Fact] public void Negative_origin_is_normalised() =>
        Assert.Equal(new Rectangle(0, 0, 1920, 1080),
            CropMath.ScreenRegionInImage(new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 3840, 1080), 3840, 1080));

    [Fact] public void Scaled_screenshot_scales_region() =>
        Assert.Equal(new Rectangle(0, 0, 1920, 1080),
            CropMath.ScreenRegionInImage(new Rectangle(0, 0, 1280, 720), new Rectangle(0, 0, 1280, 720), 1920, 1080));

    [Fact] public void Selection_dip_maps_to_pixels() =>
        Assert.Equal(new Rectangle(300, 150, 600, 300),
            CropMath.SelectionToImage(100, 50, 200, 100, 640, 360, new Rectangle(0, 0, 1920, 1080)));

    [Fact] public void Selection_is_clamped_to_screen() =>
        Assert.Equal(new Rectangle(1890, 990, 30, 90),
            CropMath.SelectionToImage(630, 330, 100, 100, 640, 360, new Rectangle(0, 0, 1920, 1080)));
}
