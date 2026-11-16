using HezbClips.Core.Hotkeys;

namespace HezbClips.Core.Tests;

public class HotkeyTests
{
    class FakeNative(uint failFor) : IHotkeyNative
    {
        public bool Register(int id, uint modifiers, uint vk) => vk != failFor;
        public void Unregister(int id) { }
    }

    [Fact] public void Parses_ctrl_shift_s() => Assert.Equal((6u, 0x53u), HotkeyParser.Parse("Ctrl+Shift+S"));

    [Fact] public void Bad_combo_throws() => Assert.Throws<FormatException>(() => HotkeyParser.Parse("Ctrl+"));

    [Fact] public void Conflict_is_reported_not_thrown()
    {
        var svc = new HotkeyService(new FakeNative(failFor: 0x52)); // R is taken
        var fails = svc.RegisterAll(new Dictionary<string, string> { ["Record"] = "Ctrl+Shift+R", ["Shot"] = "Ctrl+Shift+S" });
        Assert.Equal(new[] { new HotkeyFailure("Record", "Ctrl+Shift+R") }, fails);
    }
}
