namespace HezbClips.Core.Hotkeys;

public static class HotkeyParser
{
    public static (uint Modifiers, uint VirtualKey) Parse(string text)
    {
        uint mods = 0; uint? vk = null;
        foreach (var raw in text.Split('+'))
        {
            var t = raw.Trim();
            switch (t.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "alt": mods |= 0x1; break;
                case "win": mods |= 0x8; break;
                default:
                    if (vk is not null) throw new FormatException($"more than one key in '{text}'");
                    vk = Key(t, text);
                    break;
            }
        }
        if (vk is null) throw new FormatException($"no key in '{text}'");
        return (mods, vk.Value);
    }

    static uint Key(string t, string whole)
    {
        if (t.Length == 1 && char.IsAsciiLetter(t[0])) return char.ToUpperInvariant(t[0]);
        if (t.Length == 1 && char.IsAsciiDigit(t[0])) return t[0];
        if (t.Length >= 2 && (t[0] == 'F' || t[0] == 'f') && int.TryParse(t[1..], out var n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1);
        return t.ToLowerInvariant() switch
        {
            "space" => 0x20, "printscreen" => 0x2C, "tab" => 0x09, "enter" => 0x0D,
            _ => throw new FormatException($"unknown key '{t}' in '{whole}'"),
        };
    }
}
