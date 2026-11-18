using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using HezbClips.Core.Hardware;
using HezbClips.Core.Imaging;
using HezbClips.Core.Capture;
using HezbClips.Core.Settings;
using HezbClips.Core.Audio;
using HezbClips.Core.Hotkeys;
using HezbClips.Core.Video;
using HezbClips.Linux.Portal;
using HezbClips.Linux.Windows;
using SixLabors.ImageSharp.Processing;
using SDRect = System.Drawing.Rectangle;

namespace HezbClips.Linux;

/// <summary>Tray, launcher window and the screenshot flow. UI thread only.</summary>
public sealed class AppController
{
    readonly IClassicDesktopStyleApplicationLifetime _life;
    readonly SettingsStore _store;
    readonly AppSettings _s;
    readonly HardwareProfile _hw = new(Environment.ProcessorCount, false, 0, false, 0);   // Lanczos only for now
    MainWindow? _main;
    SettingsWindow? _settings;
    GlobalShortcutsPortal? _portal;
    IDisposable? _ipc;
    BackgroundUpscaleQueue? _queue;
    VideoRecorder? _rec;
    ScreenCastSession? _cast;
    RecordControlBar? _bar;
    VideoEncoder _enc = VideoEncoder.Libx264;
    bool _busy;

    public AppController(IClassicDesktopStyleApplicationLifetime life)
    {
        _life = life;
        var cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HezbClips");
        var path = Path.Combine(cfgDir, "settings.json");
        bool first = !File.Exists(path);
        _store = new SettingsStore(path);
        _s = _store.Load();
        if (first)
        {   // Windows default is ~/Videos; screenshots belong in Pictures.
            _s.OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "HezbClips");
            TrySave();
        }
    }

    public void Start(string[] args)
    {
        var cmd = args.FirstOrDefault(a => a is "--region" or "--full" or "--record" or "--stop")?.TrimStart('-');
        if (cmd != null && Ipc.TrySend(cmd)) { _life.Shutdown(); return; }   // a running instance handles it
        if (cmd == "stop") { _life.Shutdown(); return; }
        if (args.Contains("--install-shortcuts")) { RunCli(InstallGnomeShortcuts); return; }
        if (args.Contains("--remove-shortcuts")) { RunCli(() => { GnomeShortcuts.Remove(ShortcutIds); Console.WriteLine("Đã gỡ phím tắt GNOME."); }); return; }
        if (args.Contains("--region")) { _ = RunOnceAsync(ShotRegionAsync); return; }
        if (args.Contains("--full")) { _ = RunOnceAsync(ShotFullAsync); return; }

        _main = new MainWindow(() => _ = ShotRegionAsync(), () => _ = ShotFullAsync(),
            () => ClipboardTool.OpenFolder(EnsureDir()), ShowSettings, () => InstallGnomeShortcuts(),
            () => _ = ToggleRecordAsync(false), () => _ = ToggleRecordAsync(true));
        _main.Closed += (_, _) => _life.Shutdown();
        _life.MainWindow = _main;
        _life.Exit += (_, _) => _ipc?.Dispose();
        _main.Show();
        SetupTray();
        _ = StartShortcutsAsync();
        try { _ipc = Ipc.Listen(c => Dispatcher.UIThread.Post(() => OnCommand(c))); }
        catch (Exception ex) { Console.Error.WriteLine("IPC unavailable: " + ex.Message); }
        if (cmd == "record") _ = ToggleRecordAsync(false);
    }

    void OnCommand(string c)
    {
        switch (c)
        {
            case "region": _ = ShotRegionAsync(); break;
            case "full": _ = ShotFullAsync(); break;
            case "record": _ = ToggleRecordAsync(false); break;
            case "stop": if (_rec != null) _ = StopRecordingAsync(); break;
        }
    }

    void SetupTray()
    {
        try
        {
            using var icon = AssetLoader.Open(new Uri("avares://hezbclips/Assets/tray.png"));
            var menu = new NativeMenu();
            void Add(string t, Action a) { var i = new NativeMenuItem(t); i.Click += (_, _) => a(); menu.Add(i); }
            Add("Chụp vùng", () => _ = ShotRegionAsync());
            Add("Chụp toàn màn hình", () => _ = ShotFullAsync());
            Add("Mở thư mục ảnh", () => ClipboardTool.OpenFolder(EnsureDir()));
            Add("Cài đặt", ShowSettings);
            Add("Thoát", () => _life.Shutdown());
            var tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Hezb Clips", Menu = menu, IsVisible = true };
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        catch (Exception) { /* no tray on this desktop: the launcher window still works */ }
    }

    // ---------- shortcuts ----------

    static readonly string[] ShortcutIds = { "region", "full", "record" };

    void RunCli(Action a)
    {
        try { a(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        finally { _life.Shutdown(); }
    }

    void InstallGnomeShortcuts()
    {
        try
        {
            GnomeShortcuts.Install(new Dictionary<string, (string, string, string)>
            {
                ["region"] = ("Hezb Clips: chụp vùng", _s.HotkeyRegionShot, "--region"),
                ["full"] = ("Hezb Clips: chụp toàn màn hình", _s.HotkeyFullShot, "--full"),
                ["record"] = ("Hezb Clips: bắt đầu/dừng quay", _s.HotkeyRecord, "--record"),
            });
            Console.WriteLine($"Đã cài phím tắt GNOME: {_s.HotkeyRegionShot} (vùng), {_s.HotkeyFullShot} (toàn màn hình), {_s.HotkeyRecord} (quay).");
            _main?.SetShortcutStatus($"Đã cài phím tắt GNOME: {_s.HotkeyRegionShot} (vùng), {_s.HotkeyFullShot} (toàn màn hình), {_s.HotkeyRecord} (quay)", false);
        }
        catch (Exception ex) { Report(ex); }
    }

    async Task StartShortcutsAsync()
    {
        _portal = new GlobalShortcutsPortal();
        _portal.Activated += id => Dispatcher.UIThread.Post(() =>
        {
            if (id == "region") _ = ShotRegionAsync();
            else if (id == "full") _ = ShotFullAsync();
            else if (id == "record") _ = ToggleRecordAsync(false);
        });
        var ok = await _portal.TryStartAsync(new Dictionary<string, (string, string)>
        {
            ["region"] = ("Chụp vùng", _s.HotkeyRegionShot),
            ["full"] = ("Chụp toàn màn hình", _s.HotkeyFullShot),
            ["record"] = ("Bắt đầu/dừng quay", _s.HotkeyRecord),
        });
        if (ok) { _main?.SetShortcutStatus("Phím tắt toàn cục đã bật (xdg portal).", false); return; }
        _portal.Dispose(); _portal = null;
        _main?.SetShortcutStatus(GnomeShortcuts.IsGnome
            ? "Desktop này không có portal phím tắt. Cài phím tắt qua cài đặt GNOME:"
            : "Desktop này không hỗ trợ phím tắt toàn cục. Gán lệnh 'hezbclips --region' vào phím tắt của desktop.",
            GnomeShortcuts.IsGnome);
    }

    // ---------- recording ----------

    BackgroundUpscaleQueue Queue()
    {
        if (_queue != null) return _queue;
        _queue = new BackgroundUpscaleQueue(new FfmpegUpscaleRunner(_hw, "ffmpeg", null, _enc));
        _queue.Completed += (_, path) =>
        {
            if (path.StartsWith(_s.OutputDir, StringComparison.Ordinal)) Notify("Upscale xong", Path.GetFileName(path));
        };
        _queue.Failed += (_, ex) => Notify("Upscale lỗi", ex.Message);
        return _queue;
    }

    async Task ToggleRecordAsync(bool fullScreen)
    {
        if (_rec != null) { await StopRecordingAsync(); return; }
        if (_busy) return;
        _busy = true;
        try
        {
            var screens = ScreensOrProbe();
            var primary = Math.Max(0, Array.FindIndex(screens.ToArray(), r => r.X == 0 && r.Y == 0));
            var screen = screens[primary];
            System.Drawing.Rectangle? sel = null; double sx = 1, sy = 1;   // sel: relative to the primary screen, in screenshot px

            if (!fullScreen)
            {
                using var shot = await GrabHiddenAsync();
                var shown = CropMath.ScreenRegionInImage(screen, CropMath.Union(screens), shot.Width, shot.Height);
                var r = await RegionSelectorWindow.SelectAsync(shot, shown);
                if (r is null) return;
                sel = new System.Drawing.Rectangle(r.Value.X - shown.X, r.Value.Y - shown.Y, r.Value.Width, r.Value.Height);
                sx = shown.Width; sy = shown.Height;   // screenshot extent of this screen
                await Task.Delay(300);                  // let the overlay disappear
            }

            var cast = await ScreenCastPortal.StartAsync();   // the desktop asks which screen to share
            try
            {
                var (w, h) = cast.Size ?? (screen.Width, screen.Height);
                var region = sel is { } s0
                    ? new System.Drawing.Rectangle((int)Math.Round(s0.X * w / sx), (int)Math.Round(s0.Y * h / sy),
                                                   (int)Math.Round(s0.Width * w / sx), (int)Math.Round(s0.Height * h / sy))
                    : new System.Drawing.Rectangle(0, 0, w, h);
                region = System.Drawing.Rectangle.Intersect(region, new System.Drawing.Rectangle(0, 0, w, h));

                var flags = (_s.CaptureSystemAudio ? AudioSources.System : AudioSources.None)
                          | (_s.CaptureMicrophone ? AudioSources.Microphone : AudioSources.None);
                var mode = ModeSelector.Choose(_hw, _s.VideoQuality, _s.VideoMode);

                var rec = new VideoRecorder(new PipeWireFrameSource(cast.NodeId, w, h), new AudioRecorder(new PwAudioInputFactory()),
                    () => new FfmpegProcess("ffmpeg"), Queue(), _enc, _s);
                rec.Finalized += path => Dispatcher.UIThread.Post(() => ShowVideoPopup(path));
                rec.FinalizeFailed += (ex, dir) => Notify("Ghép video lỗi", $"{ex.Message} (file tạm: {dir})");
                rec.Start(new RecordRequest(region, _s.Fps, flags, _s.VideoQuality, mode));

                _rec = rec; _cast = cast;
                _bar = new RecordControlBar();
                _bar.StopRequested += () => _ = StopRecordingAsync();
                _bar.PauseChanged += p => { if (p) rec.Pause(); else rec.Resume(); };
                _bar.Show();
            }
            catch { cast.Dispose(); throw; }
        }
        catch (OperationCanceledException) { /* user cancelled screen sharing */ }
        catch (Exception ex) { Report(ex); }
        finally { _busy = false; }
    }

    async Task StopRecordingAsync()
    {
        var rec = _rec; if (rec is null) return;
        var cast = _cast; var bar = _bar;
        _rec = null; _cast = null; _bar = null;
        bar?.Close();
        try
        {
            var result = await Task.Run(() => rec.StopAsync());
            foreach (var w in result.Warnings) Notify("Cảnh báo khi quay", w);
            if (File.Exists(result.FinalPath))
            {
                if (result.PendingUpscaleJob != null) Notify("Đã lưu bản gốc", "Bản upscale đang chạy nền.");
                ShowVideoPopup(result.FinalPath);
            }
            else Notify("Đang xử lý nền", "Video sẽ có sau khi upscale xong.");
        }
        catch (Exception ex) { Report(ex); }
        finally { cast?.Dispose(); }
    }

    void ShowVideoPopup(string path) => new PostCapturePopup(path, false, isVideo: true).Show();

    static void Notify(string title, string text)
    {
        Console.Error.WriteLine($"{title}: {text}");
        try { Process.Start(new ProcessStartInfo("notify-send", new[] { "-a", "Hezb Clips", title, text }) { UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ---------- screenshots ----------

    async Task RunOnceAsync(Func<Task> action)
    {
        try { await action(); }
        finally { _life.Shutdown(); }
    }

    IReadOnlyList<SDRect> Screens() =>
        _main?.Screens.All.Select(s => new SDRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList()
        ?? (IReadOnlyList<SDRect>)new[] { new SDRect(0, 0, 1920, 1080) };

    // Avalonia needs a window to query screens; in --region/--full mode we use a hidden one.
    IReadOnlyList<SDRect> ScreensOrProbe()
    {
        if (_main != null) return Screens();
        var probe = new Window { Width = 1, Height = 1, ShowInTaskbar = false, Opacity = 0 };
        probe.Show();
        var list = probe.Screens.All.Select(s => new SDRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();
        probe.Close();
        return list;
    }

    async Task ShotRegionAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var screens = ScreensOrProbe();
            var shot = await GrabHiddenAsync();
            using (shot)
            {
                var primary = Math.Max(0, Array.FindIndex(screens.ToArray(), r => r.X == 0 && r.Y == 0));
                var shown = CropMath.ScreenRegionInImage(screens[primary], CropMath.Union(screens), shot.Width, shot.Height);
                var sel = await RegionSelectorWindow.SelectAsync(shot, shown);
                if (sel is null) return;
                var r = sel.Value;
                var crop = shot.Clone(c => c.Crop(new SixLabors.ImageSharp.Rectangle(r.X, r.Y, r.Width, r.Height)));
                await SaveAndShowAsync(crop);
            }
        }
        catch (Exception ex) { Report(ex); }
        finally { _busy = false; }
    }

    async Task ShotFullAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var screens = ScreensOrProbe();
            var shot = await GrabHiddenAsync();
            var primary = Math.Max(0, Array.FindIndex(screens.ToArray(), r => r.X == 0 && r.Y == 0));
            await SaveAndShowAsync(PortalCaptureSource.CropToScreen(shot, screens, primary));
        }
        catch (Exception ex) { Report(ex); }
        finally { _busy = false; }
    }

    /// <summary>Hide our own windows, wait for the compositor, then grab the desktop.</summary>
    async Task<SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>> GrabHiddenAsync()
    {
        var state = _main?.WindowState;
        if (_main != null) _main.WindowState = WindowState.Minimized;
        await Task.Delay(400);
        try { return await ScreenshotPortal.GrabAsync(); }
        finally { if (_main != null && state != null) _main.WindowState = state.Value; }
    }

    async Task SaveAndShowAsync(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        var svc = new ScreenshotService(new PortalCaptureSource(ScreensOrProbe()),
            ImageResizer.CreateUpscaler(_hw, null), _s);
        var path = await Task.Run(() => svc.SaveAsync(image));
        bool copied = _s.AutoCopyAfterCapture && ClipboardTool.CopyImage(path);
        new PostCapturePopup(path, copied).Show();
    }

    // ---------- helpers ----------

    void ShowSettings()
    {
        if (_settings != null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_s);
        _settings.Saved += TrySave;
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    string EnsureDir() { Directory.CreateDirectory(_s.OutputDir); return _s.OutputDir; }

    void TrySave() { try { _store.Save(_s); } catch (IOException) { } }

    void Report(Exception ex)
    {
        Console.Error.WriteLine(ex);
        var w = new Window { Title = "Hezb Clips - Lỗi", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        w.Content = new TextBlock { Text = ex.Message, Margin = new Thickness(16), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        w.Show();
    }
}
