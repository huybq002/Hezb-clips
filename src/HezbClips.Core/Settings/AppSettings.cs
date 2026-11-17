namespace HezbClips.Core.Settings;

public enum ExportQuality { P1080 = 1080, P1440 = 1440, P2160 = 2160 }
public enum ImageFormat { Png, WebP, Jpeg }
public enum VideoMode { Auto, Live, Background }
public enum AppTheme { Dark, Light, System }

public class AppSettings
{
    public ExportQuality Quality { get; set; } = ExportQuality.P1440;
    public ExportQuality VideoQuality { get; set; } = ExportQuality.P1080;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool OpenEditorAfterCapture { get; set; } = true;
    public ImageFormat ImageFormat { get; set; } = ImageFormat.Png;
    public VideoMode VideoMode { get; set; } = VideoMode.Auto;
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = false;
    public int Fps { get; set; } = 30;
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "HezbClips");
    public string HotkeyRegionShot { get; set; } = "Ctrl+Shift+S";
    public string HotkeyFullShot { get; set; } = "Ctrl+Shift+F";
    public string HotkeyRecord { get; set; } = "Ctrl+Shift+R";
    public string HotkeyGif { get; set; } = "Ctrl+Shift+G";
    public string HotkeyLibrary { get; set; } = "Ctrl+Shift+L";
    public Dictionary<string, string> EditorKeys { get; set; } = new(EditorShortcuts.Defaults);
    public bool StartWithWindows { get; set; } = false;
}
