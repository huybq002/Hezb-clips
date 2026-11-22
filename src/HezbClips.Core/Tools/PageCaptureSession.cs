using System.Security.Cryptography;
using System.Text;
using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tools;

/// <summary>Collects the PNG slices a browser extension sends for one full-page screenshot and joins them top to bottom.
/// Slices may arrive in any order; the page is complete when all of them are in.</summary>
public sealed class PageCaptureSession
{
    public const int MaxParts = 64;
    public const long MaxPartBytes = 80L << 20;

    readonly object _lock = new();
    readonly Dictionary<string, Pending> _pending = new();

    sealed class Pending
    {
        public int Total;
        public readonly SortedDictionary<int, byte[]> Parts = new();
        public DateTime Touched = DateTime.UtcNow;
    }

    /// <summary>Add one slice. Returns the finished page image when this was the last missing slice, otherwise null.</summary>
    public Raster? Add(string sessionId, int index, int total, byte[] png)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64) throw new ArgumentException("bad session id");
        if (total < 1 || total > MaxParts) throw new ArgumentException("bad part count");
        if (index < 0 || index >= total) throw new ArgumentException("bad part index");
        if (png.Length == 0 || png.Length > MaxPartBytes) throw new ArgumentException("bad part size");

        Dictionary<int, byte[]> ready;
        lock (_lock)
        {
            Cleanup();
            if (!_pending.TryGetValue(sessionId, out var p)) _pending[sessionId] = p = new Pending { Total = total };
            if (p.Total != total) throw new ArgumentException("part count changed during the session");
            p.Parts[index] = png; p.Touched = DateTime.UtcNow;
            if (p.Parts.Count < p.Total) return null;
            ready = new Dictionary<int, byte[]>(p.Parts);
            _pending.Remove(sessionId);
        }
        return Join(ready.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList());
    }

    /// <summary>Decode the slices and stack them. Slices must have the same width.</summary>
    public static Raster Join(IReadOnlyList<byte[]> pngs)
    {
        var images = new List<Raster>();
        try
        {
            foreach (var b in pngs) images.Add(Raster.Load(b));
            var w = images[0].Width;
            if (images.Any(i => i.Width != w)) throw new InvalidDataException("slices have different widths");
            return ScrollStitcher.Assemble(images);
        }
        finally { foreach (var i in images) i.Dispose(); }
    }

    void Cleanup()
    {
        var old = _pending.Where(kv => DateTime.UtcNow - kv.Value.Touched > TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList();
        foreach (var k in old) _pending.Remove(k);
    }

    /// <summary>Compare tokens without leaking where they differ.</summary>
    public static bool TokenMatches(string? expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Only the browser extension itself (chrome-extension:// or moz-extension://) may call the bridge, never a web page.</summary>
    public static bool OriginAllowed(string? origin) =>
        origin is null || origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                       || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);
}
