namespace HezbClips.Core.Output;

public enum MediaKind { Image, Video, Gif }

public record MediaItem(string Path, MediaKind Kind, DateTime Modified, long Size);

/// <summary>Lists captured images, videos and GIFs in the output folder, newest first.</summary>
public static class LibraryScanner
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    public static IReadOnlyList<MediaItem> Scan(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<MediaItem>();
        var list = new List<MediaItem>();
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var ext = System.IO.Path.GetExtension(f);
            MediaKind? kind = ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ? MediaKind.Gif
                : ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ? MediaKind.Video
                : Images.Contains(ext) ? MediaKind.Image : null;
            if (kind is null) continue;
            var fi = new FileInfo(f);
            list.Add(new MediaItem(f, kind.Value, fi.LastWriteTime, fi.Length));
        }
        return list.OrderByDescending(i => i.Modified).ToList();
    }
}
