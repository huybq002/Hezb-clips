namespace HezbClips.Core.Video;

public record Segment(string Path, bool Scaled);

public class SegmentPlan
{
    readonly List<Segment> _segments = new();
    public IReadOnlyList<Segment> Segments => _segments;
    public bool CurrentScaled => _segments.Count > 0 && _segments[^1].Scaled;
    public void Add(string path, bool scaled) => _segments.Add(new Segment(path, scaled));
    public void Clear() => _segments.Clear();
    public IReadOnlyList<string> SegmentsNeedingUpscale() => _segments.Where(s => !s.Scaled).Select(s => s.Path).ToList();
}
