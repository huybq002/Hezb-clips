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
    public string HotkeyRegionShot { get; set; } = "Ctrl+Alt+S";
    public string HotkeyFullShot { get; set; } = "Ctrl+Alt+F";
    public string HotkeyRecord { get; set; } = "Ctrl+Alt+R";
    public string HotkeyGif { get; set; } = "Ctrl+Alt+G";
    public string HotkeyLibrary { get; set; } = "Ctrl+Alt+L";
    public Dictionary<string, string> EditorKeys { get; set; } = new(EditorShortcuts.Defaults);
    public string HotkeyCopyRegion { get; set; } = "Ctrl+Alt+C";
    public string HotkeyWindowShot { get; set; } = "Ctrl+Alt+W";
    public string HotkeyDelayShot { get; set; } = "Ctrl+Alt+T";
    public string HotkeyPinShot { get; set; } = "Ctrl+Alt+P";
    public string HotkeyScroll { get; set; } = "Ctrl+Alt+K";
    public string HotkeyPause { get; set; } = "Ctrl+Alt+E";
    public bool AutoCopyAfterCapture { get; set; } = true;
    /// <summary>Settings file format; older files are upgraded on load (see SettingsStore).</summary>
    public int SchemaVersion { get; set; }
    public int DelaySeconds { get; set; } = 3;
    public int GifFps { get; set; } = 15;
    public int GifWidth { get; set; } = 960;
    public bool ShowClicks { get; set; } = true;
    public bool ShowKeys { get; set; } = false;
    public bool Webcam { get; set; } = false;
    public WebcamShape WebcamShape { get; set; } = WebcamShape.Circle;
    public WebcamCorner WebcamCorner { get; set; } = WebcamCorner.BottomRight;
    public WebcamSize WebcamSize { get; set; } = WebcamSize.Medium;
    public int LibraryRetentionDays { get; set; } = 0;
    public string UpdateFeedUrl { get; set; } = "";
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>Local bridge the browser extension uses to send full-page screenshots (127.0.0.1 only, secret token).</summary>
    public bool BrowserBridgeEnabled { get; set; } = true;
    public string BrowserToken { get; set; } = "";
    public bool StartWithWindows { get; set; } = false;
}
