namespace HezbClips.Core.Hotkeys;

public class HotkeyService(IHotkeyNative native)
{
    readonly List<int> _ids = new();
    int _next = 1;

    public IReadOnlyList<HotkeyFailure> RegisterAll(IDictionary<string, string> nameToCombo)
    {
        var failures = new List<HotkeyFailure>();
        foreach (var (name, combo) in nameToCombo)
        {
            try
            {
                var (mods, vk) = HotkeyParser.Parse(combo);
                var id = _next++;
                if (native.Register(id, mods, vk)) _ids.Add(id);
                else failures.Add(new HotkeyFailure(name, combo));
            }
            catch (FormatException) { failures.Add(new HotkeyFailure(name, combo)); }
        }
        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids) native.Unregister(id);
        _ids.Clear();
    }
}
