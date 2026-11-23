using System.Drawing;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Tests;

public class WebcamLayoutTests
{
    static readonly Rectangle Region = new(100, 200, 1000, 600);

    [Theory]
    [InlineData(WebcamShape.Circle, WebcamSize.Small, 140, 140)]
    [InlineData(WebcamShape.Rounded, WebcamSize.Medium, 190, 190)]
    [InlineData(WebcamShape.Circle, WebcamSize.Large, 260, 260)]
    public void Round_and_rounded_shapes_are_square(WebcamShape shape, WebcamSize size, int w, int h) =>
        Assert.Equal((w, h), new WebcamLayout(shape, WebcamCorner.BottomRight, size).Dimensions());

    [Fact] public void Wide_shape_is_16_9()
    {
        var (w, h) = new WebcamLayout(WebcamShape.Wide, WebcamCorner.BottomRight, WebcamSize.Medium).Dimensions();
        Assert.True(w > h);
        Assert.InRange((double)w / h, 1.76, 1.79);
    }

    [Theory]
    [InlineData(WebcamCorner.BottomRight, 1100 - 200 - 12, 800 - 150 - 12)]
    [InlineData(WebcamCorner.BottomLeft, 112, 800 - 150 - 12)]
    [InlineData(WebcamCorner.TopRight, 1100 - 200 - 12, 212)]
    [InlineData(WebcamCorner.TopLeft, 112, 212)]
    public void Corner_places_the_window_inside_the_region(WebcamCorner corner, int x, int y) =>
        Assert.Equal((x, y), new WebcamLayout(WebcamShape.Circle, corner).Position(Region, 200, 150));

    [Fact] public void Circle_radius_is_half_the_side() =>
        Assert.Equal(95, new WebcamLayout(WebcamShape.Circle, WebcamCorner.BottomRight, WebcamSize.Medium).CornerRadius());

    [Fact] public void Settings_round_trip_through_json()
    {
        var s = new AppSettings { WebcamShape = WebcamShape.Wide, WebcamCorner = WebcamCorner.TopLeft, WebcamSize = WebcamSize.Large };
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal((WebcamShape.Wide, WebcamCorner.TopLeft, WebcamSize.Large), (back.WebcamShape, back.WebcamCorner, back.WebcamSize));
    }
}
