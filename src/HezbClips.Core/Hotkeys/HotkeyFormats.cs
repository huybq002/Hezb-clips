using System.Text.RegularExpressions;

namespace HezbClips.Core.Hotkeys;

/// <summary>Converts the app's "Ctrl+Alt+S" combos to Linux desktop formats.</summary>
public static class HotkeyFormats
{
    static (string[] Mods, string Key) Split(string combo)
    {
        HotkeyParser.Parse(combo);   // validates; throws FormatException
        var parts = combo.Split('+').Select(p => p.Trim()).ToArray();
        var mods = new List<string>();
        string key = "";
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods.Add("Ctrl"); break;
                case "shift": mods.Add("Shift"); break;
                case "alt": mods.Add("Alt"); break;
                case "win": mods.Add("Super"); break;
                default: key = p; break;
            }
        }
        return (mods.ToArray(), key);
    }

    static string KeyName(string k) => k.ToLowerInvariant() switch
    {
        "space" => "space", "printscreen" => "Print", "tab" => "Tab", "enter" => "Return",
        var s when s.Length == 1 => s,
        var s => "F" + s[1..],   // F1..F24
    };

    /// <summary>GNOME custom keybinding format, e.g. "&lt;Ctrl&gt;&lt;Alt&gt;s".</summary>
    public static string ToGnome(string combo)
    {
        var (mods, key) = Split(combo);
        return string.Concat(mods.Select(m => $"<{m}>")) + KeyName(key);
    }

    /// <summary>XDG GlobalShortcuts preferred_trigger, e.g. "CTRL+ALT+s".</summary>
    public static string ToPortalTrigger(string combo)
    {
        var (mods, key) = Split(combo);
        return string.Join("+", mods.Select(m => m.ToUpperInvariant()).Append(KeyName(key)));
    }
}

/// <summary>Reads/writes the GVariant string-array text that `gsettings` prints.</summary>
public static class GsettingsList
{
    public static IReadOnlyList<string> Parse(string text) =>
        Regex.Matches(text, @"'([^']*)'").Select(m => m.Groups[1].Value).ToList();

    public static string Format(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Select(i => $"'{i}'")) + "]";
}
