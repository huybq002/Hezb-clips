using System.Text.Json;

namespace HezbClips.Core.Output;

public class MediaMeta
{
    public bool Starred { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>Stars and tags for library files, keyed by file name and kept in one small JSON file.</summary>
public class LibraryMetaStore(string path)
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    Dictionary<string, MediaMeta>? _data;

    Dictionary<string, MediaMeta> Data => _data ??= Load();

    Dictionary<string, MediaMeta> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, MediaMeta>>(File.ReadAllText(path));
                if (d is not null) return new Dictionary<string, MediaMeta>(d, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Dictionary<string, MediaMeta>(StringComparer.OrdinalIgnoreCase);
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(Data, Opts));
    }

    public MediaMeta Get(string filePath) =>
        Data.TryGetValue(Path.GetFileName(filePath), out var m) ? m : new MediaMeta();

    public bool IsStarred(string filePath) => Get(filePath).Starred;

    public bool ToggleStar(string filePath)
    {
        var m = GetOrAdd(filePath); m.Starred = !m.Starred; Prune(filePath, m); Save();
        return m.Starred;
    }

    public void SetTags(string filePath, IEnumerable<string> tags)
    {
        var m = GetOrAdd(filePath);
        m.Tags = tags.Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Prune(filePath, m); Save();
    }

    /// <summary>Split "a, b #c" style input into tags.</summary>
    public static IEnumerable<string> ParseTags(string text) =>
        text.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0);

    MediaMeta GetOrAdd(string filePath)
    {
        var key = Path.GetFileName(filePath);
        if (!Data.TryGetValue(key, out var m)) Data[key] = m = new MediaMeta();
        return m;
    }

    void Prune(string filePath, MediaMeta m)
    {
        if (!m.Starred && m.Tags.Count == 0) Data.Remove(Path.GetFileName(filePath));
    }
}

public static class LibraryQuery
{
    /// <summary>Filter by free text (file name or tag), starred-only and media kind.</summary>
    public static IReadOnlyList<MediaItem> Filter(IEnumerable<MediaItem> items, LibraryMetaStore meta, string? text, bool starredOnly, MediaKind? kind)
    {
        var q = (text ?? "").Trim();
        return items.Where(i =>
        {
            if (kind is not null && i.Kind != kind) return false;
            var m = meta.Get(i.Path);
            if (starredOnly && !m.Starred) return false;
            if (q.Length == 0) return true;
            return Path.GetFileName(i.Path).Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.Tags.Any(t => t.Contains(q.TrimStart('#'), StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}

public static class LibraryCleaner
{
    static readonly System.Text.RegularExpressions.Regex Own =
        new(@"^(Clip|Scroll)_\d{8}_\d{6}", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Only files named the way Hezb Clips names them (Clip_/Scroll_ + timestamp, plus _edit/_trim/_1080p variants).</summary>
    public static bool IsOwnCapture(string path) => Own.IsMatch(Path.GetFileName(path));

    /// <summary>Files older than the retention period, never including starred ones. Zero or negative days = keep everything.</summary>
    public static IReadOnlyList<MediaItem> FindExpired(IEnumerable<MediaItem> items, LibraryMetaStore meta, int retentionDays, DateTime now)
    {
        if (retentionDays <= 0) return Array.Empty<MediaItem>();
        var cutoff = now - TimeSpan.FromDays(retentionDays);
        return items.Where(i => i.Modified < cutoff && IsOwnCapture(i.Path) && !meta.IsStarred(i.Path)).ToList();
    }
}
