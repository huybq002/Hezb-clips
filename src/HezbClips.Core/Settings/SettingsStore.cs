using System.Text.Json;

namespace HezbClips.Core.Settings;

public class SettingsStore(string path)
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return Migrate(new AppSettings());
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            if (!Enum.IsDefined(s.Quality)) s.Quality = ExportQuality.P1080;
            if (!Enum.IsDefined(s.VideoQuality)) s.VideoQuality = ExportQuality.P1080;
            return Migrate(s);
        }
        catch (JsonException) { return Migrate(new AppSettings()); }
        catch (IOException) { return Migrate(new AppSettings()); }
        catch (UnauthorizedAccessException) { return Migrate(new AppSettings()); }
    }

    public const int CurrentSchema = 3;

    /// <summary>v2: copying to the clipboard after every capture became the default.
    /// v3: default hotkeys moved from Ctrl+Shift+X (clashes with browsers/editors) to Ctrl+Alt+X;
    /// only hotkeys the user never changed are moved.</summary>
    static AppSettings Migrate(AppSettings s)
    {
        if (s.SchemaVersion < 2) s.AutoCopyAfterCapture = true;
        if (s.SchemaVersion < 3)
        {
            static string Move(string v, char key) => string.Equals(v?.Replace(" ", ""), "Ctrl+Shift+" + key, StringComparison.OrdinalIgnoreCase) ? "Ctrl+Alt+" + key : v!;
            s.HotkeyRegionShot = Move(s.HotkeyRegionShot, 'S'); s.HotkeyFullShot = Move(s.HotkeyFullShot, 'F');
            s.HotkeyRecord = Move(s.HotkeyRecord, 'R'); s.HotkeyGif = Move(s.HotkeyGif, 'G'); s.HotkeyLibrary = Move(s.HotkeyLibrary, 'L');
            s.HotkeyCopyRegion = Move(s.HotkeyCopyRegion, 'C'); s.HotkeyWindowShot = Move(s.HotkeyWindowShot, 'W');
            s.HotkeyDelayShot = Move(s.HotkeyDelayShot, 'T'); s.HotkeyPinShot = Move(s.HotkeyPinShot, 'P');
            s.HotkeyScroll = Move(s.HotkeyScroll, 'K'); s.HotkeyPause = Move(s.HotkeyPause, 'E');
        }
        s.SchemaVersion = CurrentSchema;
        return s;
    }

    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // write a temp file then swap it in, so a crash mid-write never leaves a broken settings file
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opts));
        File.Move(tmp, path, overwrite: true);
    }
}
