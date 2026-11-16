using System.Drawing;
using HezbClips.Core.Imaging;
using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class DpiAndTrimTests
{
    [Fact] public void Dip_to_physical_at_150() =>
        Assert.Equal(new Rectangle(150, 75, 300, 150), DpiMath.ToPhysical(100, 50, 200, 100, 1.5));

    [Fact] public void Dip_to_physical_at_125_rounds() =>
        Assert.Equal(new Rectangle(125, 63, 250, 125), DpiMath.ToPhysical(100, 50, 200, 100, 1.25));

    [Fact] public void Trim_args_have_range_and_reencode()
    {
        var a = TrimService.BuildArgs("in.mp4", "out.mp4", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), VideoEncoder.Libx264);
        Assert.Contains("-ss 00:00:02", a);
        Assert.Contains("-to 00:00:10", a);
        Assert.Contains("libx264", a);
        Assert.DoesNotContain("-c copy", a);
    }
}
