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

    public void Start(bool showMain = true)
    {
        ThemeManager.Apply(_s.Theme);
        _tray = new TrayHost(new (string, Action)[]
        {
            ("Chụp vùng", () => _ = RegionShotAsync()), ("Chụp toàn màn hình", () => _ = FullShotAsync()),
            ("Chụp cửa sổ đang focus", () => _ = WindowShotAsync()), ("Chụp trễ", () => _ = DelayShotAsync()),
            ("Chụp vùng và copy", () => _ = CopyRegionShotAsync()), ("Chụp vùng và ghim", () => _ = PinRegionShotAsync()),
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
        if (showMain) OpenLibrary();
    }

    void Report(string title, Exception ex)
    {
        try { Directory.CreateDirectory(_dataDir); File.AppendAllText(Path.Combine(_dataDir, "log.txt"), $"{DateTime.Now:s} {title}: {ex}{Environment.NewLine}"); }
        catch (IOException) { }
        _tray?.Balloon(title, ex.Message);
    }

    // ---- hotkeys ----

    void RegisterHotkeys()
    {
        _hotkeys!.UnregisterAll();
        _failures = _hotkeys.RegisterAll(new Dictionary<string, string>
        {
            ["RegionShot"] = _s.HotkeyRegionShot, ["FullShot"] = _s.HotkeyFullShot, ["Record"] = _s.HotkeyRecord,
            ["Gif"] = _s.HotkeyGif, ["Library"] = _s.HotkeyLibrary,
            ["CopyRegion"] = _s.HotkeyCopyRegion, ["WindowShot"] = _s.HotkeyWindowShot, ["DelayShot"] = _s.HotkeyDelayShot,
            ["PinShot"] = _s.HotkeyPinShot, ["Scroll"] = _s.HotkeyScroll, ["Pause"] = _s.HotkeyPause,
        });
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

    Task RegionShotAsync() => ShotAsync(() => RegionSelector.SelectAsync(), ShotMode.Normal);
    Task CopyRegionShotAsync() => ShotAsync(() => RegionSelector.SelectAsync(), ShotMode.CopyOnly);
    Task PinRegionShotAsync() => ShotAsync(() => RegionSelector.SelectAsync(), ShotMode.Pin);
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
        if (!_restoreMain) return;
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
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            var k = g.DpiX / 96.0;
            try { PinWindow.FromFile(path, new Point(region.X / k - 8, region.Y / k - 8)).Show(); }
            catch (Exception ex) { Report("Không ghim được ảnh", ex); }
            return;
        }
        if (_s.OpenEditorAfterCapture) OpenEditor(path); else ShowPopup(path, false, null);
    }

    static bool CopyImage(string path)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
                Clipboard.SetImage(bmp);
                return true;
            }
            catch (Exception) { System.Threading.Thread.Sleep(80); } // clipboard busy or format not decodable (WebP)
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
            Report("Record not ready", new InvalidOperationException($"ffmpeg={_ffmpeg ?? "null"}"));
            _tray?.Balloon("Hezb Clips", _ffmpeg is null ? "Chưa có ffmpeg nên chưa quay được." : "Đang dò cấu hình máy, thử lại sau vài giây.");
            return;
        }
        _busy = true;
        await HideMainAsync();
        var barShown = false;
        try
        {
            var region = await RegionSelector.SelectAsync();
            if (region is null) return;
            // grey out everything outside the chosen region and outline it, until the recording ends
            _focus = RegionFocusOverlay.Show(region.Value);
            var bar = new RecordControlBar(region.Value, _s);
            _bar = bar;
            if (gif) bar.PresetGif();
            bar.RecordClicked += c => StartRecording(region.Value, c);
            bar.StopClicked += () => _ = StopRecordingAsync();
            bar.PauseToggled += p => ApplyPause(p);
            bar.Cancelled += () => { bar.Close(); _bar = null; CloseFocus(); RestoreMain(); };
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
            _store.Save(_s);

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
            }
            _recording = true;
            _focus?.SetState(recording: true);
            StartEffects(region, c);
            _tray?.SetRecording(true);
            _quality = c.Quality;
        }
        catch (Exception ex)
        {
            Report("Không bắt đầu quay được", ex);
            _bar?.Close(); _bar = null; CloseFocus();
        }
    }

    /// <summary>Pause or resume (bar button or hotkey): the paused stretch is left out of the video and audio.</summary>
    void ApplyPause(bool pause)
    {
        if (!_recording || _recorder is null) return;
        if (pause) _recorder.Pause(); else _recorder.Resume();
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
        finally { RestoreMain(); }
    }

    // effects drawn on screen during recording (click ripples, key badges, webcam) — filled in by RecordingEffects
    void StartEffects(System.Drawing.Rectangle region, RecordChoices c) { }
    void StopEffects() { }

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
                Theme: ThemeManager.Apply));
            _main.Closed += (_, _) => _main = null;
        }
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        if (settings) _main.ShowSettings(); else _main.ShowLibrary();
        _main.Activate();
    }

    Task ScrollShotAsync() { Toast.Show("Chụp cuộn trang sẽ có trong bản sau"); return Task.CompletedTask; }

    void PinFile(string path)
    {
        try { PinWindow.FromFile(path).Show(); }
        catch (Exception ex) { Report("Không ghim được ảnh", ex); }
    }

    void OpenEditor(string imagePath)
    {
        try
        {
            var w = new ImageEditorWindow(imagePath, _s.EditorKeys);
            w.Show(); w.Activate();
        }
        catch (Exception ex) { Report("Không mở được trình chỉnh sửa", ex); }
    }

    string? TrimVideo(string path, TimeSpan from, TimeSpan to)
    {
        if (_ffmpeg is null) return null;
        var output = Path.ChangeExtension(path, null) + "_trim.mp4";
        try
        {
            var p = new FfmpegProcess(_ffmpeg);
            p.Start(TrimService.BuildArgs(path, output, from, to, _enc));
            return p.ExitAsync().GetAwaiter().GetResult() == 0 ? output : null;
        }
        catch (Exception ex) { Report("Cắt video lỗi", ex); return null; }
    }

    void OnSettingsSaved(AppSettings s)
    {
        _store.Save(_s);
        RegisterHotkeys();
        _main?.SetFolder(_s.OutputDir);
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
        _hotkeys?.UnregisterAll();
        _hk?.Dispose();
        _tray?.Dispose();
    }
}
