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
    }

    public const int CurrentSchema = 2;

    /// <summary>v2: copying to the clipboard after every capture became the default.</summary>
    static AppSettings Migrate(AppSettings s)
    {
        if (s.SchemaVersion < 2) s.AutoCopyAfterCapture = true;
        s.SchemaVersion = CurrentSchema;
        return s;
    }

    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(s, Opts));
    }
}
