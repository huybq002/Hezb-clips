namespace HezbClips.Core.Settings;

/// <summary>Single-letter shortcuts for the image editor tools (configurable in Settings).</summary>
public static class EditorShortcuts
{
    /// <summary>Tool ids in toolbar order. Keys used by the editor itself and not assignable: C (custom colour).</summary>
    public static readonly string[] Tools = { "Pen", "Highlighter", "Line", "Arrow", "Rect", "Ellipse", "Text", "Number", "Mosaic" };

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["Pen"] = "P", ["Highlighter"] = "H", ["Line"] = "L", ["Arrow"] = "A", ["Rect"] = "R",
        ["Ellipse"] = "O", ["Text"] = "T", ["Number"] = "N", ["Mosaic"] = "M",
    };

    const string Reserved = "C";

    /// <summary>Fill gaps with defaults, drop junk, uppercase.</summary>
    public static Dictionary<string, string> Resolve(IDictionary<string, string>? user)
    {
        var map = new Dictionary<string, string>();
        foreach (var t in Tools)
        {
            var v = user is not null && user.TryGetValue(t, out var k) ? k?.Trim().ToUpperInvariant() : null;
            map[t] = IsValidKey(v) ? v! : Defaults[t];
        }
        return map;
    }

    public static bool IsValidKey(string? k) => k is { Length: 1 } && k[0] is >= 'A' and <= 'Z' && k != Reserved;

    /// <summary>Returns an error message, or null when every tool has a unique valid letter.</summary>
    public static string? Validate(IDictionary<string, string> keys)
    {
        foreach (var t in Tools)
            if (!keys.TryGetValue(t, out var k) || !IsValidKey(k?.Trim().ToUpperInvariant()))
                return $"Phím cho công cụ '{t}' phải là một chữ cái A-Z (trừ C).";
        var dup = Tools.GroupBy(t => keys[t].Trim().ToUpperInvariant()).FirstOrDefault(g => g.Count() > 1);
        return dup is null ? null : $"Phím {dup.Key} bị dùng cho nhiều công cụ.";
    }
}
