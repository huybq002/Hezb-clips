using HezbClips.Core.Hotkeys;

namespace HezbClips.Core.Tests;

public class HotkeyFormatsTests
{
    [Fact] public void Gnome_format() => Assert.Equal("<Ctrl><Alt>s", HotkeyFormats.ToGnome("Ctrl+Alt+S"));
    [Fact] public void Gnome_special_keys() => Assert.Equal("<Shift>Print", HotkeyFormats.ToGnome("Shift+PrintScreen"));
    [Fact] public void Gnome_function_key() => Assert.Equal("<Super>F5", HotkeyFormats.ToGnome("Win+F5"));
    [Fact] public void Portal_trigger() => Assert.Equal("CTRL+ALT+s", HotkeyFormats.ToPortalTrigger("Ctrl+Alt+S"));
    [Fact] public void Bad_combo_throws() => Assert.Throws<FormatException>(() => HotkeyFormats.ToGnome("Ctrl+"));
    [Fact] public void Gsettings_list_roundtrip()
    {
        var l = GsettingsList.Parse("['/a/b/', '/c/d/']");
        Assert.Equal(new[] { "/a/b/", "/c/d/" }, l);
        Assert.Equal("['/a/b/', '/c/d/']", GsettingsList.Format(l));
    }
    [Fact] public void Empty_gsettings_list() => Assert.Empty(GsettingsList.Parse("@as []"));
}
