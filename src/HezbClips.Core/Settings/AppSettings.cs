namespace HezbClips.Core.Settings;

public enum ExportQuality { P1080 = 1080, P1440 = 1440, P2160 = 2160 }
public enum ImageFormat { Png, WebP, Jpeg }
public enum VideoMode { Auto, Live, Background }
public enum AppTheme { Dark, Light, System }

public class AppSettings
{
    public ExportQuality Quality { get; set; } = ExportQuality.P1080;
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
    public string HotkeyCopyRegion { get; set; } = "Ctrl+Shift+C";
    public string HotkeyWindowShot { get; set; } = "Ctrl+Shift+W";
    public string HotkeyDelayShot { get; set; } = "Ctrl+Shift+T";
    public string HotkeyPinShot { get; set; } = "Ctrl+Shift+P";
    public string HotkeyScroll { get; set; } = "Ctrl+Shift+K";
    public string HotkeyPause { get; set; } = "Ctrl+Shift+E";
    public bool AutoCopyAfterCapture { get; set; } = false;
    public int DelaySeconds { get; set; } = 3;
    public int GifFps { get; set; } = 15;
    public int GifWidth { get; set; } = 960;
    public bool ShowClicks { get; set; } = true;
    public bool ShowKeys { get; set; } = false;
    public bool Webcam { get; set; } = false;
    public int LibraryRetentionDays { get; set; } = 0;
    public string UpdateFeedUrl { get; set; } = "";
    public bool AutoCheckUpdates { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
}
