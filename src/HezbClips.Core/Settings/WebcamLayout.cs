namespace HezbClips.Core.Settings;

public enum WebcamShape { Circle, Rounded, Wide }
public enum WebcamCorner { BottomRight, BottomLeft, TopRight, TopLeft }
public enum WebcamSize { Small, Medium, Large }

/// <summary>Look and place of the webcam picture shown (and recorded) in a corner of the capture region.</summary>
public record WebcamLayout(WebcamShape Shape = WebcamShape.Circle, WebcamCorner Corner = WebcamCorner.BottomRight, WebcamSize Size = WebcamSize.Medium)
{
    /// <summary>Window size in device-independent pixels: round and rounded-square shapes are square, "wide" is 16:9.</summary>
    public (int W, int H) Dimensions()
    {
        var side = Size switch { WebcamSize.Small => 140, WebcamSize.Large => 260, _ => 190 };
        return Shape == WebcamShape.Wide ? ((int)(side * 1.5), (int)(side * 1.5 * 9 / 16)) : (side, side);
    }

    /// <summary>Corner radius in device-independent pixels (0 = square corners are not used; round = half the side).</summary>
    public int CornerRadius()
    {
        var (w, h) = Dimensions();
        return Shape switch { WebcamShape.Circle => Math.Min(w, h) / 2, WebcamShape.Rounded => Math.Min(w, h) / 5, _ => Math.Min(w, h) / 8 };
    }

    /// <summary>Top-left of a w x h window placed in the given corner of the region (all in physical pixels).</summary>
    public (int X, int Y) Position(System.Drawing.Rectangle region, int w, int h, int margin = 12) => Corner switch
    {
        WebcamCorner.BottomLeft => (region.Left + margin, region.Bottom - h - margin),
        WebcamCorner.TopRight => (region.Right - w - margin, region.Top + margin),
        WebcamCorner.TopLeft => (region.Left + margin, region.Top + margin),
        _ => (region.Right - w - margin, region.Bottom - h - margin),
    };
}
