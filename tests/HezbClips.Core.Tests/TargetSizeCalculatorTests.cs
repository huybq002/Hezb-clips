using HezbClips.Core.Imaging;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Tests;

public class TargetSizeCalculatorTests
{
    [Theory]
    [InlineData(1280, 720, ExportQuality.P2160, 3840, 2160)]
    [InlineData(3840, 2160, ExportQuality.P1080, 1920, 1080)]
    [InlineData(1001, 501, ExportQuality.P1080, 1920, 960)]
    [InlineData(1080, 1920, ExportQuality.P1080, 606, 1080)]
    public void Fits_box_keeps_aspect_and_even(int sw, int sh, ExportQuality q, int w, int h)
        => Assert.Equal((w, h), TargetSizeCalculator.Calculate(sw, sh, q));
}
