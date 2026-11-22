using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using HezbClips.App.Hotkeys;
using HezbClips.App.Tray;
using HezbClips.App.Windows;
using HezbClips.Capture;
using HezbClips.Core.Audio;
using HezbClips.Core.Capture;
using HezbClips.Core.Hardware;
using HezbClips.Core.Hotkeys;
using HezbClips.Core.Imaging;
using HezbClips.Core.Settings;
using HezbClips.Core.Video;
using Microsoft.Win32;

namespace HezbClips.App;

/// <summary>Composition root and user flows: hotkeys/tray -> region select -> capture/record -> popup.</summary>
public sealed class AppController : IDisposable
{
    readonly Application _app;
    readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HezbClips");
    readonly AppSettings _s;
    readonly SettingsStore _store;
    readonly HardwareProfileStore _hwStore;
    HardwareProfile _profile = new(Environment.ProcessorCount, false, 0, false, 0);
    VideoEncoder _enc = VideoEncoder.Libx264;
    string? _ffmpeg, _esrgan;
    TrayHost? _tray;
    Win32Hotkeys? _hk;
    HotkeyService? _hotkeys;
    IReadOnlyList<HotkeyFailure> _failures = Array.Empty<HotkeyFailure>();
    BackgroundUpscaleQueue? _queue;
    VideoRecorder? _recorder;
    RecordControlBar? _bar;
    MainWindow? _main;
    bool _busy, _recording, _restoreMain;

    public AppController(Application app)
    {
        _app = app;
        _store = new SettingsStore(Path.Combine(_dataDir, "settings.json"));
        _hwStore = new HardwareProfileStore(Path.Combine(_dataDir, "hardware.json"));
        _s = _store.Load();
    }

    public void Start(bool showMain = true, string? editPath = null)
    {
        ThemeManager.Apply(_s.Theme);
        ForegroundTracker.Start();
        if (string.IsNullOrEmpty(_s.BrowserToken))
        {
            _s.BrowserToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            try { _store.Save(_s); } catch (Exception) { }
        }
        _bridge = new BrowserBridge(() => _s.BrowserToken, OnBrowserPage);
        ApplyBridge();
        _tray = new TrayHost(new (string, Action)[]
        {
            ("Chụp vùng", () => _ = RegionShotAsync()), ("Chụp toàn màn hình", () => _ = FullShotAsync()),
            ("Chụp cửa sổ đang focus", () => _ = WindowShotAsync()), ("Chụp trễ", () => _ = DelayShotAsync()),
            ("Chụp vùng và ghim", () => _ = PinRegionShotAsync()),
            ("Chụp cuộn trang", () => _ = ScrollShotAsync()), ("Quay GIF", () => _ = ToggleRecordAsync(gif: true)),
        }, () => _ = ToggleRecordAsync(), OpenSettings, () => _app.Shutdown(), OpenLibrary);
        _hk = new Win32Hotkeys();
        _hotkeys = new HotkeyService(_hk);
        _hk.Pressed += OnHotkey;
        RegisterHotkeys();
        _esrgan = ToolLocator.FindRealEsrgan();
        _ffmpeg = ToolLocator.FindFfmpeg();
        if (_ffmpeg is null)
            _tray.Balloon("Hezb Clips", "Không tìm thấy ffmpeg. Quay video và upscale video bị tắt. Hãy đặt ffmpeg.exe vào thư mục 'tools' cạnh ứng dụng.");
        else
            DetectHardware(force: false);
        if (_failures.Count > 0)
            _tray.Balloon("Phím tắt bị chiếm", string.Join(", ", _failures.Select(f => f.Combo)) + ". Bấm để đổi trong Cài đặt.", OpenSettings);
        CleanupLibrary();
        AutoCheckUpdate();
        if (showMain) OpenLibrary();
        if (editPath is not null && File.Exists(editPath)) OpenEditor(editPath);
    }

    /// <summary>Second launch of the app, or the tray: bring up the main window.</summary>
    public void ShowMain() => OpenLibrary();

    /// <summary>A second "HezbClips.exe --edit file" handed its file over: open it in the editor of this running instance.</summary>
    public void EditFromOtherInstance(string imagePath) { if (File.Exists(imagePath)) OpenEditor(imagePath); else OpenLibrary(); }

    /// <summary>Last-resort handler for exceptions nobody caught (logged, shown, app keeps running).</summary>
    public void ReportUnhandled(Exception ex) => Report("Lỗi không mong muốn", ex);

    void Report(string title, Exception ex)
    {
        try { Directory.CreateDirectory(_dataDir); File.AppendAllText(Path.Combine(_dataDir, "log.txt"), $"{DateTime.Now:s} {title}: {ex}{Environment.NewLine}"); }
        catch (Exception) { }
        _tray?.Balloon(title, ex.Message);
    }

    // ---- hotkeys ----

    void RegisterHotkeys(bool withPause = false)
    {
        _hotkeys!.UnregisterAll();
        _failures = _hotkeys.RegisterAll(new Dictionary<string, string>
        {
            ["RegionShot"] = _s.HotkeyRegionShot, ["FullShot"] = _s.HotkeyFullShot, ["Record"] = _s.HotkeyRecord,
            ["Gif"] = _s.HotkeyGif, ["Library"] = _s.HotkeyLibrary,
            ["CopyRegion"] = _s.HotkeyCopyRegion, ["WindowShot"] = _s.HotkeyWindowShot, ["DelayShot"] = _s.HotkeyDelayShot,
            ["PinShot"] = _s.HotkeyPinShot, ["Scroll"] = _s.HotkeyScroll,
        }.Concat(withPause ? new[] { KeyValuePair.Create("Pause", _s.HotkeyPause) } : Array.Empty<KeyValuePair<string, string>>())
         .ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    void OnHotkey(int id)
    {
        if (_hotkeys!.IdToName.TryGetValue(id, out var name))
            switch (name)
            {
                case "RegionShot": _ = RegionShotAsync(); break;
                case "FullShot": _ = FullShotAsync(); break;
                case "Record": _ = ToggleRecordAsync(); break;
                case "Gif": _ = ToggleRecordAsync(gif: true); break;
                case "Library": OpenLibrary(); break;
                case "CopyRegion": _ = CopyRegionShotAsync(); break;
                case "WindowShot": _ = WindowShotAsync(); break;
                case "DelayShot": _ = DelayShotAsync(); break;
                case "PinShot": _ = PinRegionShotAsync(); break;
                case "Scroll": _ = ScrollShotAsync(); break;
                case "Pause": TogglePause(); break;
            }
    }

    // ---- hardware / video init ----

    void DetectHardware(bool force)
    {
        var ffmpeg = _ffmpeg!;
        Task.Run(() =>
        {
            try
            {
                var encoders = HardwareProbes.FfmpegEncoders(ffmpeg);
                var enc = EncoderDetector.Pick(encoders);
                if (force) _hwStore.Clear();
                var profile = _hwStore.Load() ?? new HardwareProfiler(() => encoders,
                    () => HardwareProbes.Vulkan(_esrgan), () => HardwareProbes.BenchmarkFps(ffmpeg, enc)).Detect();
                _hwStore.Save(profile);
                _app.Dispatcher.Invoke(() => InitVideo(profile, enc));
            }
            catch (Exception ex) { _app.Dispatcher.Invoke(() => Report("Dò cấu hình lỗi", ex)); }
        });
    }

    void InitVideo(HardwareProfile profile, VideoEncoder enc)
    {
        _profile = profile; _enc = enc;
        if (_recording) return; // keep the running recorder; new profile applies after restart
        _queue = new BackgroundUpscaleQueue(new FfmpegUpscaleRunner(profile, _ffmpeg!, _esrgan, enc));
        _queue.Progress += (_, p) => _app.Dispatcher.Invoke(() => _tray?.SetTooltip($"Hezb Clips — upscale {p:P0}"));
        _queue.Completed += (_, path) => _app.Dispatcher.Invoke(() =>
        {
            _tray?.SetTooltip("Hezb Clips");
            if (path.StartsWith(_s.OutputDir, StringComparison.OrdinalIgnoreCase))
                _tray?.Balloon("Upscale xong", Path.GetFileName(path), () => Process.Start("explorer.exe", $"/select,\"{path}\""));
        });
        _queue.Failed += (_, ex) => _app.Dispatcher.Invoke(() =>
        {
            _tray?.SetTooltip("Hezb Clips");
            Report("Upscale lỗi", ex);
        });
        _recorder = new VideoRecorder(new WgcFrameSource(), new AudioRecorder(new NAudioInputFactory()),
            () => new FfmpegProcess(_ffmpeg!), _queue, enc, _s);
        _recorder.Finalized += path => _app.Dispatcher.Invoke(() => ShowPopup(path, true, null));
        _recorder.FinalizeFailed += (ex, dir) => _app.Dispatcher.Invoke(() =>
            _tray?.Balloon("Ghép video lỗi", $"{ex.Message} (file tạm: {dir})"));
    }

    // ---- screenshots ----

    ScreenshotService Shots() => new(new WgcCaptureSource(), ImageResizer.CreateUpscaler(_profile, _esrgan), _s);

    enum ShotMode { Normal, CopyOnly, Pin }

    Task RegionShotAsync() => FrozenShotAsync(ShotMode.Normal);
    Task CopyRegionShotAsync() => FrozenShotAsync(ShotMode.CopyOnly);
    Task PinRegionShotAsync() => FrozenShotAsync(ShotMode.Pin);

    /// <summary>Region capture with the screen frozen: every monitor is captured first, the selector shows that still
    /// picture, and the chosen region is cut out of it — so menus, videos and animations stay exactly as they were.</summary>
    async Task FrozenShotAsync(ShotMode mode)
    {
        if (_busy || _recording) return;
        _busy = true;
        await HideMainAsync();
        Dictionary<System.Drawing.Rectangle, HezbClips.Core.Imaging.Raster>? shots = null;
        try
        {
            var (imgs, pics) = await Task.Run(() =>
            {
                var src = new WgcCaptureSource();
                var d = new Dictionary<System.Drawing.Rectangle, HezbClips.Core.Imaging.Raster>();
                var b = new Dictionary<System.Drawing.Rectangle, System.Windows.Media.Imaging.BitmapSource>();
                foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                {
                    try { var img = src.CaptureRegion(sc.Bounds); d[sc.Bounds] = img; b[sc.Bounds] = ToBitmap(img); }
                    catch (Exception) { /* a screen we cannot capture just is not frozen */ }
                }
                return (d, b);
            });
            shots = imgs;
            var r = await RegionSelector.SelectAsync(pics);
            if (r is null) return;
            var region = r.Value;
            // cut the region out of the frozen pictures (stitched when it spans several monitors)
            var cut = FrozenComposer.Compose(shots, region);
            string path;
            if (cut is not null) path = await Task.Run(() => Shots().SaveAsync(cut));
            else
            {
                await Task.Delay(150);
                path = await Task.Run(() => Shots().TakeRegionAsync(region));
            }
            AfterImage(path, mode, region);
        }
        catch (Exception ex) { Report("Lỗi chụp ảnh", ex); }
        finally
        {
            if (shots is not null) foreach (var i in shots.Values) i.Dispose();
            _busy = false; RestoreMain();
        }
    }

    static System.Windows.Media.Imaging.BitmapSource ToBitmap(HezbClips.Core.Imaging.Raster img)
    {
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32, null, img.Bitmap.GetPixels(), img.Stride * img.Height, img.Stride);
        bmp.Freeze();
        return bmp;
    }
    Task FullShotAsync() => ShotAsync(() => Task.FromResult<System.Drawing.Rectangle?>(CursorScreen()), ShotMode.Normal);

    Task WindowShotAsync() => ShotAsync(() =>
    {
        var r = WindowFinder.Foreground(OwnHandles());
        if (r is null) Toast.Show("Không xác định được cửa sổ đang làm việc");
        return Task.FromResult(r);
    }, ShotMode.Normal);

    Task DelayShotAsync() => ShotAsync(async () =>
    {
        await Countdown.RunAsync(Math.Clamp(_s.DelaySeconds, 1, 30));
        return (System.Drawing.Rectangle?)CursorScreen();
    }, ShotMode.Normal);

    static System.Drawing.Rectangle CursorScreen() => System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;

    HashSet<IntPtr> OwnHandles()
    {
        var set = new HashSet<IntPtr>();
        foreach (Window w in _app.Windows) set.Add(new System.Windows.Interop.WindowInteropHelper(w).Handle);
        return set;
    }

    /// <summary>One capture flow: hide the main window, pick the area, capture, then copy / pin / open the editor.</summary>
    async Task ShotAsync(Func<Task<System.Drawing.Rectangle?>> pick, ShotMode mode)
    {
        if (_busy || _recording) return;
        _busy = true;
        await HideMainAsync();
        try
        {
            var r = await pick();
            if (r is null) return;
            await Task.Delay(150); // let overlays disappear before capturing
            var path = await Task.Run(() => Shots().TakeRegionAsync(r.Value));
            AfterImage(path, mode, r.Value);
        }
        catch (Exception ex) { Report("Lỗi chụp ảnh", ex); }
        finally { _busy = false; RestoreMain(); }
    }

    /// <summary>Hide the main window while capturing so it never ends up in the shot.</summary>
    async Task HideMainAsync()
    {
        if (_main is { IsVisible: true }) { _restoreMain = true; _main.Hide(); await Task.Delay(250); }
    }

    void RestoreMain()
    {
        if (!_restoreMain || _bar is not null) return; // a pending record bar keeps the main window out of the region
        _restoreMain = false;
        if (_main is not null) { _main.Show(); _main.RefreshLibrary(); }
    }

    /// <summary>Captured image: copy / pin / open the editor (or the quick popup when the editor is turned off).</summary>
    void AfterImage(string path, ShotMode mode, System.Drawing.Rectangle region)
    {
        if (mode == ShotMode.CopyOnly || _s.AutoCopyAfterCapture)
            if (CopyImage(path)) Toast.Show("Đã copy ảnh vào clipboard");
        if (mode == ShotMode.CopyOnly) return;
        if (mode == ShotMode.Pin)
        {
            try { PinWindow.FromFile(path, new System.Drawing.Point(region.X - 8, region.Y - 8)).Show(); }
            catch (Exception ex) { Report("Không ghim được ảnh", ex); }
            return;
        }
        if (_s.OpenEditorAfterCapture) OpenEditor(path); else ShowPopup(path, false, null);
    }

    static bool CopyImage(string path)
    {
        System.Windows.Media.Imaging.BitmapSource? bmp = null;
        try
        {
            var b = new System.Windows.Media.Imaging.BitmapImage();
            b.BeginInit(); b.UriSource = new Uri(path); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
            bmp = b;
        }
        catch (Exception)
        {
            // WebP and other formats WPF cannot decode: go through Skia
            try { using var img = HezbClips.Core.Imaging.Raster.Load(path); bmp = ToBitmap(img); }
            catch (Exception) { return false; }
        }
        for (int i = 0; i < 3; i++)
        {
            try { Clipboard.SetImage(bmp); return true; }
            catch (Exception) { System.Threading.Thread.Sleep(50); } // clipboard held by another app
        }
        return false;
    }

    // ---- recording ----

    RegionFocusOverlay? _focus;

    void CloseFocus() { _focus?.Close(); _focus = null; }

    async Task ToggleRecordAsync(bool gif = false)
    {
        if (_recording) { await StopRecordingAsync(); return; }
        if (_bar is not null) { _bar.Close(); _bar = null; CloseFocus(); RestoreMain(); return; } // hotkey again cancels the pending record bar
        if (_busy) return;
        if (_recorder is null)
        {
            _tray?.Balloon("Hezb Clips", _ffmpeg is null ? "Chưa có ffmpeg nên chưa quay được." : "Đang dò cấu hình máy, thử lại sau vài giây.");
            return;
        }
        _busy = true;
        await HideMainAsync();
        var barShown = false;
        try
        {
            var picked = await RegionSelector.SelectAsync();
            if (picked is null) return;
            // the frame source captures one screen: record only the part on that screen, with even sizes for the encoder
            var mon = System.Windows.Forms.Screen.FromRectangle(picked.Value).Bounds;
            var rec = System.Drawing.Rectangle.Intersect(picked.Value, mon);
            rec = new System.Drawing.Rectangle(rec.X, rec.Y, rec.Width & ~1, rec.Height & ~1);
            if (rec.Width < 16 || rec.Height < 16) return;
            // grey out everything outside the chosen region and outline it, until the recording ends
            _focus = RegionFocusOverlay.Show(rec);
            var bar = new RecordControlBar(rec, _s);
            _bar = bar;
            if (gif) bar.PresetGif();
            bar.RecordClicked += c => StartRecording(rec, c);
            bar.StopClicked += () => _ = StopRecordingAsync();
            bar.PauseToggled += p => ApplyPause(p);
            bar.Cancelled += () => bar.Close();
            // however the bar gets closed (Hủy, Alt+F4): cancel, or stop a running recording
            bar.Closed += (_, _) =>
            {
                if (_bar != bar) return;
                _bar = null;
                if (_recording) _ = StopRecordingAsync();
                else { CloseFocus(); RestoreMain(); }
            };
            bar.Show();
            barShown = true;
        }
        finally { _busy = false; if (!barShown) { CloseFocus(); RestoreMain(); } } // with the bar up, main stays hidden until stop/cancel
    }

    bool _gifMode;
    System.Windows.Threading.DispatcherTimer? _gifLimit;

    void StartRecording(System.Drawing.Rectangle region, RecordChoices c)
    {
        try
        {
            // remember what the user picked for next time
            _s.GifFps = c.GifFps; _s.GifWidth = c.GifWidth; _s.ShowClicks = c.Clicks; _s.ShowKeys = c.Keys; _s.Webcam = c.Webcam;
            if (!c.Gif) { _s.VideoQuality = c.Quality; _s.CaptureSystemAudio = c.Audio.HasFlag(AudioSources.System); _s.CaptureMicrophone = c.Audio.HasFlag(AudioSources.Microphone); }
            try { _store.Save(_s); } catch (Exception) { /* not worth failing the recording over */ }

            // GIF: native-size capture at the chosen fps, no audio, no upscale; converted to .gif on stop and capped at 60 s.
            var req = c.Gif
                ? new RecordRequest(region, c.GifFps, AudioSources.None, ExportQuality.P1080, RecordMode.Background, SkipUpscale: true)
                : new RecordRequest(region, _s.Fps, c.Audio, c.Quality, ModeSelector.Choose(_profile, c.Quality, _s.VideoMode));
            _recorder!.Start(req);
            _gifMode = c.Gif;
            if (c.Gif)
            {
                _gifLimit = new System.Windows.Threading.DispatcherTimer { Interval = FfmpegCommandBuilder.GifMaxDuration };
                _gifLimit.Tick += (_, _) => { _gifLimit?.Stop(); _ = StopRecordingAsync(); };
                _gifLimit.Start();
                _gifStarted = DateTime.UtcNow;
            }
            _recording = true;
            RegisterHotkeys(withPause: true); // the pause hotkey only exists while recording
            _focus?.SetState(recording: true);
            StartEffects(region, c);
            _tray?.SetRecording(true);
            _quality = c.Quality;
        }
        catch (Exception ex)
        {
            Report("Không bắt đầu quay được", ex);
            _bar?.Close(); _bar = null; CloseFocus(); RestoreMain();
        }
    }

    DateTime _gifStarted;
    TimeSpan _gifLeft;

    /// <summary>Pause or resume (bar button or hotkey): the paused stretch is left out of the video and audio,
    /// and the 60 s GIF limit stops counting too.</summary>
    void ApplyPause(bool pause)
    {
        if (!_recording || _recorder is null) return;
        if (pause != _recorder.IsPaused)
        {
            if (pause) _recorder.Pause(); else _recorder.Resume();
            if (_gifLimit is not null)
            {
                if (pause) { _gifLeft = _gifLimit.Interval - (DateTime.UtcNow - _gifStarted); _gifLimit.Stop(); }
                else
                {
                    _gifLimit.Interval = _gifLeft > TimeSpan.Zero ? _gifLeft : TimeSpan.FromMilliseconds(1);
                    _gifStarted = DateTime.UtcNow; _gifLimit.Start();
                }
            }
        }
        _bar?.SetPaused(pause);
        _focus?.SetState(recording: true, paused: pause);
    }

    void TogglePause()
    {
        if (_recording && _recorder is not null) ApplyPause(!_recorder.IsPaused);
    }

    ExportQuality _quality;

    async Task StopRecordingAsync()
    {
        if (!_recording) return;
        _recording = false;
        _busy = true; // no new capture/recording until this one is fully written
        RegisterHotkeys();
        _gifLimit?.Stop(); _gifLimit = null;
        var wasGif = _gifMode; _gifMode = false;
        _tray?.SetRecording(false);
        StopEffects();
        _bar?.Close(); _bar = null; CloseFocus();
        try
        {
            var r = await Task.Run(() => _recorder!.StopAsync());
            if (wasGif)
            {
                var gifPath = await Task.Run(() => ConvertToGif(r.FinalPath));
                if (gifPath is not null) ShowPopup(gifPath, false, $"GIF {_s.GifFps} fps, tối đa 60 giây.");
                else _tray?.Balloon("Hezb Clips", "Không tạo được GIF. Video gốc vẫn được giữ: " + Path.GetFileName(r.FinalPath));
                return;
            }
            var notes = new List<string>(r.Warnings);
            if (r.PendingUpscaleJob is not null && File.Exists(r.FinalPath))
                notes.Add($"Đang upscale lên {(int)_quality}p ở nền; file mới sẽ có tên *_{(int)_quality}p.mp4.");
            if (File.Exists(r.FinalPath)) ShowPopup(r.FinalPath, true, notes.Count > 0 ? string.Join(" ", notes) : null);
            else _tray?.Balloon("Hezb Clips", "Đang ghép video (upscale phần cuối ở nền). Sẽ báo khi xong.");
        }
        catch (Exception ex) { Report("Lỗi khi dừng quay", ex); }
        finally { _busy = false; RestoreMain(); }
    }

    // effects drawn on screen during recording (click ripples, key badges, webcam) — filled in by RecordingEffects
    RecordingEffects? _effects;

    void StartEffects(System.Drawing.Rectangle region, RecordChoices c)
    {
        if (!(c.Clicks || c.Keys || c.Webcam)) return;
        try
        {
            _effects = new RecordingEffects(region, c.Clicks, c.Keys, c.Webcam, msg => Toast.Show(msg));
            _effects.Start();
        }
        catch (Exception ex) { Report("Không bật được hiệu ứng khi quay", ex); }
    }

    void StopEffects() { _effects?.Dispose(); _effects = null; }

    // ---- popup / trim / settings ----

    void ShowPopup(string path, bool video, string? note)
    {
        var popup = new PostCapturePopup(path, video, note, TrimVideo, OpenEditor);
        popup.Show();
    }

    string? ConvertToGif(string mp4)
    {
        if (_ffmpeg is null || !File.Exists(mp4)) return null;
        var gif = Path.ChangeExtension(mp4, ".gif");
        try
        {
            var p = new FfmpegProcess(_ffmpeg);
            p.Start(FfmpegCommandBuilder.GifArgs(mp4, gif, _s.GifFps, _s.GifWidth));
            if (p.ExitAsync().GetAwaiter().GetResult() != 0 || !File.Exists(gif)) return null;
            File.Delete(mp4);
            return gif;
        }
        catch (Exception ex) { _app.Dispatcher.Invoke(() => Report("Tạo GIF lỗi", ex)); return null; }
    }

    void OpenLibrary() => OpenMain(settings: false);

    void OpenSettings() => OpenMain(settings: true);

    /// <summary>Show the single main window (library, or the settings sections when asked).</summary>
    void OpenMain(bool settings)
    {
        if (_main is null)
        {
            _main = new MainWindow(_s, _ffmpeg, _dataDir, _failures, new MainActions(
                Region: () => _ = RegionShotAsync(), Full: () => _ = FullShotAsync(),
                Window: () => _ = WindowShotAsync(), Delay: () => _ = DelayShotAsync(), CopyRegion: () => _ = CopyRegionShotAsync(),
                Pin: () => _ = PinRegionShotAsync(), Scroll: () => _ = ScrollShotAsync(), PinFile: PinFile,
                Record: () => _ = ToggleRecordAsync(), Gif: () => _ = ToggleRecordAsync(gif: true),
                Edit: OpenEditor, Save: OnSettingsSaved,
                Redetect: () => { if (_ffmpeg is not null) DetectHardware(force: true); },
                Theme: ThemeManager.Apply),
                advanced: (st, onApply) => AdvancedPage.Build(st, onApply, new AdvancedHost(
                    EsrganPath: () => _esrgan, InstallEsrgan: InstallEsrganAsync, CheckUpdate: CheckUpdateAsync,
                    DownloadUpdate: DownloadUpdateAsync, CurrentVersion: CurrentVersion.ToString(3),
                    BridgeStatus: () => _bridge?.Status ?? "Đang tắt", ExtensionDir: Path.Combine(AppContext.BaseDirectory, "browser-extension"))));
            _main.Closed += (_, _) => _main = null;
        }
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        if (settings) _main.ShowSettings(); else _main.ShowLibrary();
        _main.Topmost = true; _main.Topmost = false; // win the foreground race when called from the tray
        _main.Activate(); _main.Focus();
    }

    // ---- browser extension bridge: full-page screenshots without scrolling ----

    BrowserBridge? _bridge;

    void ApplyBridge()
    {
        if (_bridge is null) return;
        if (_s.BrowserBridgeEnabled) { if (!_bridge.Start()) _tray?.Balloon("Chụp cả trang", _bridge.Status); }
        else _bridge.Stop();
    }

    /// <summary>A finished full-page image arrived from the extension (worker thread): save it and open it like any capture.</summary>
    void OnBrowserPage(HezbClips.Core.Imaging.Raster img, string title)
    {
        try
        {
            string path;
            using (img)
            {
                Directory.CreateDirectory(_s.OutputDir);
                path = HezbClips.Core.Output.FileNamer.Next(_s.OutputDir, "Page", "png", DateTime.Now, File.Exists);
                img.SavePng(path);
                var (w, h) = (img.Width, img.Height);
                _app.Dispatcher.BeginInvoke(() => Toast.Show($"Đã nhận ảnh cả trang {w} × {h} px" + (title.Length > 0 ? $" — {title}" : "")));
            }
            _app.Dispatcher.BeginInvoke(() =>
            {
                _main?.RefreshLibrary();
                AfterImage(path, ShotMode.Normal, System.Drawing.Rectangle.Empty);
            });
        }
        catch (Exception ex) { _app.Dispatcher.BeginInvoke(() => Report("Không lưu được ảnh cả trang", ex)); }
    }

    // ---- optional tools and updates ----

    readonly System.Net.Http.HttpClient _http = NewHttp();

    static System.Net.Http.HttpClient NewHttp()
    {
        var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("HezbClips");
        return c;
    }

    static Version CurrentVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    async Task InstallEsrganAsync(IProgress<double> progress)
    {
        var exe = await new HezbClips.Core.Tools.ToolInstaller(_http).InstallRealEsrganAsync(Path.Combine(_dataDir, "tools"), progress);
        _esrgan = exe;
        if (_ffmpeg is not null) DetectHardware(force: true); // re-probe: Vulkan now testable, upscaler picked up
    }

    Task<HezbClips.Core.Tools.UpdateInfo?> CheckUpdateAsync(string feedUrl) =>
        new HezbClips.Core.Tools.UpdateChecker(_http).CheckAsync(feedUrl, CurrentVersion);

    Task<string> DownloadUpdateAsync(HezbClips.Core.Tools.UpdateInfo info, IProgress<double> progress) =>
        new HezbClips.Core.Tools.UpdateChecker(_http).DownloadAsync(info, Path.Combine(Path.GetTempPath(), "HezbClips-update"), progress);

    /// <summary>Quiet check at startup; only speaks up when a newer version exists.</summary>
    void AutoCheckUpdate()
    {
        if (!_s.AutoCheckUpdates || string.IsNullOrWhiteSpace(_s.UpdateFeedUrl)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await CheckUpdateAsync(_s.UpdateFeedUrl);
                if (info is not null)
                    _app.Dispatcher.Invoke(() => _tray?.Balloon("Có bản cập nhật Hezb Clips", $"Phiên bản {info.Version} đã sẵn sàng. Bấm để mở Cài đặt > Nâng cao.", () => { OpenSettings(); _main?.ShowSettings("Nâng cao"); }));
            }
            catch (Exception) { /* offline or bad feed: stay quiet at startup */ }
        });
    }

    bool _scrolling, _scrollCancel;

    /// <summary>Pick the scrollable area, then scroll it with the mouse wheel and stitch the frames into one tall image.</summary>
    async Task ScrollShotAsync()
    {
        if (_scrolling) { _scrollCancel = true; return; } // pressing the hotkey again stops early
        if (_busy || _recording) return;
        _busy = true;
        await HideMainAsync();
        try
        {
            var r = await RegionSelector.SelectAsync();
            if (r is null) return;
            _scrolling = true; _scrollCancel = false;
            Toast.Show("Đang cuộn và chụp… đừng di chuột. Bấm lại phím tắt để dừng.");
            await Task.Delay(500);
            var path = await Task.Run(() => CaptureScrolling(r.Value));
            if (path is not null) AfterImage(path, ShotMode.Normal, r.Value);
        }
        catch (Exception ex) { Report("Lỗi chụp cuộn trang", ex); }
        finally { _scrolling = false; _busy = false; RestoreMain(); }
    }

    string? CaptureScrolling(System.Drawing.Rectangle region)
    {
        var src = new WgcCaptureSource();
        var cursor = System.Windows.Forms.Cursor.Position;
        // browsers scroll about 100 DIP per wheel notch: aim for ~40 % of the region per step so frames overlap well
        var scale = WinNative.MonitorScale(region.X + region.Width / 2, region.Y + region.Height / 2);
        var notches = Math.Clamp((int)Math.Round(region.Height * 0.4 / (100.0 * scale)), 1, 10);
        try
        {
            var (result, steps, reason) = ScrollCapture.Run(
                capture: () => src.CaptureRegion(region),
                scroll: n => WinNative.ScrollDown(region.X + region.Width / 2, region.Y + region.Height / 2, n),
                notches: notches, cancelled: () => _scrollCancel, sleep: ms => System.Threading.Thread.Sleep(ms),
                scrollFallback: WinNative.PageDown);
            using (result)
            {
                Directory.CreateDirectory(_s.OutputDir);
                var path = HezbClips.Core.Output.FileNamer.Next(_s.OutputDir, "Scroll", "png", DateTime.Now, File.Exists);
                result.SavePng(path);
                _app.Dispatcher.BeginInvoke(() => Toast.Show(reason == "trang không cuộn"
                    ? "Vùng này không cuộn được. Hãy chọn vùng nội dung của trang (không gồm thanh địa chỉ) rồi thử lại."
                    : $"Đã chụp {result.Height} px ({reason})"));
                return path;
            }
        }
        finally { System.Windows.Forms.Cursor.Position = cursor; }
    }

    void CleanupLibrary()
    {
        if (_s.LibraryRetentionDays <= 0) return;
        var dir = _s.OutputDir; var days = _s.LibraryRetentionDays;
        Task.Run(() =>
        {
            try
            {
                var meta = new HezbClips.Core.Output.LibraryMetaStore(Path.Combine(_dataDir, "library.json"));
                var old = HezbClips.Core.Output.LibraryCleaner.FindExpired(HezbClips.Core.Output.LibraryScanner.Scan(dir), meta, days, DateTime.Now);
                var n = 0;
                foreach (var item in old)
                {
                    try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); n++; }
                    catch (Exception) { /* in use or already gone */ }
                }
                if (n > 0) _app.Dispatcher.Invoke(() => _tray?.Balloon("Hezb Clips", $"Đã chuyển {n} file cũ hơn {days} ngày vào thùng rác."));
            }
            catch (Exception ex) { _app.Dispatcher.Invoke(() => Report("Dọn thư viện lỗi", ex)); }
        });
    }

    void PinFile(string path)
    {
        try { PinWindow.FromFile(path).Show(); }
        catch (Exception ex) { Report("Không ghim được ảnh", ex); }
    }

    void OpenEditor(string imagePath)
    {
        try
        {
            var w = new ImageEditorWindow(imagePath, _s.EditorKeys, _s.AutoCopyAfterCapture);
            w.Show(); w.Activate();
        }
        catch (Exception ex) { Report("Không mở được trình chỉnh sửa", ex); }
    }

    async Task<string?> TrimVideo(string path, TimeSpan from, TimeSpan to)
    {
        if (_ffmpeg is null) return null;
        var stem = Path.ChangeExtension(path, null) + "_trim";
        var output = stem + ".mp4";
        for (int i = 1; File.Exists(output); i++) output = $"{stem}_{i}.mp4"; // never overwrite an earlier trim
        try
        {
            var p = new FfmpegProcess(_ffmpeg);
            p.Start(TrimService.BuildArgs(path, output, from, to, _enc));
            return await p.ExitAsync() == 0 ? output : null; // re-encode runs without freezing the UI
        }
        catch (Exception ex) { Report("Cắt video lỗi", ex); return null; }
    }

    void OnSettingsSaved(AppSettings s)
    {
        try { _store.Save(_s); }
        catch (Exception ex) { Report("Không lưu được cài đặt", ex); }
        RegisterHotkeys(withPause: _recording); // saving settings mid-recording must not drop the pause hotkey
        ApplyBridge();
        _main?.SetFolder(_s.OutputDir);
        _main?.UpdateHotkeys(_s);
        if (_failures.Count > 0)
            _tray?.Balloon("Phím tắt bị chiếm", string.Join(", ", _failures.Select(f => f.Combo)));
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (_s.StartWithWindows) key?.SetValue("HezbClips", $"\"{Environment.ProcessPath}\" --tray");
            else key?.DeleteValue("HezbClips", throwOnMissingValue: false);
        }
        catch (Exception) { /* registry blocked: ignore */ }
    }

    public void Dispose()
    {
        _bridge?.Dispose();
        if (_recording && _recorder is not null)
        {
            // exiting mid-recording: finish the clip instead of leaving raw segments in %TEMP%
            _recording = false; StopEffects(); _bar?.Close(); CloseFocus();
            try { Task.Run(() => _recorder.StopAsync()).Wait(TimeSpan.FromSeconds(30)); } catch (Exception) { }
        }
        StopEffects();
        _hotkeys?.UnregisterAll();
        _hk?.Dispose();
        _tray?.Dispose();
    }
}
