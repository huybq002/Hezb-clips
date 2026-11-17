using HezbClips.Core.Settings;

namespace HezbClips.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Roundtrip_keeps_values()
    {
        var p = Path.GetTempFileName(); var s = new SettingsStore(p);
        s.Save(new AppSettings { Quality = ExportQuality.P2160, CaptureMicrophone = true });
        var r = s.Load();
        Assert.Equal(ExportQuality.P2160, r.Quality);
        Assert.True(r.CaptureMicrophone);
    }

    [Fact]
    public void Missing_or_corrupt_file_gives_defaults()
    {
        var p = Path.GetTempFileName(); File.WriteAllText(p, "{not json");
        Assert.Equal(ExportQuality.P1080, new SettingsStore(p).Load().Quality);
        Assert.Equal(ExportQuality.P1080, new SettingsStore(p + ".none").Load().Quality);
        Assert.Equal(ExportQuality.P1080, new AppSettings().VideoQuality);
    }

    [Fact]
    public void Quality_below_1080_is_clamped_to_1080()
    {
        var p = Path.GetTempFileName(); File.WriteAllText(p, "{\"Quality\":720}");
        Assert.Equal(ExportQuality.P1080, new SettingsStore(p).Load().Quality);
    }
}

public class SettingsMigrationTests
{
    [Fact] public void Old_file_gets_auto_copy_on()
    {
        var p = Path.GetTempFileName(); File.WriteAllText(p, "{\"AutoCopyAfterCapture\":false}");
        Assert.True(new SettingsStore(p).Load().AutoCopyAfterCapture);
    }

    [Fact] public void Current_file_keeps_user_choice()
    {
        var p = Path.GetTempFileName(); File.WriteAllText(p, "{\"AutoCopyAfterCapture\":false,\"SchemaVersion\":2}");
        Assert.False(new SettingsStore(p).Load().AutoCopyAfterCapture);
    }

    [Fact] public void Missing_file_defaults_on() => Assert.True(new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Load().AutoCopyAfterCapture);
}
