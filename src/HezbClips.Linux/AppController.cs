using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using HezbClips.Core.Hardware;
using HezbClips.Core.Imaging;
using HezbClips.Core.Capture;
using HezbClips.Core.Settings;
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
        if (args.Contains("--region")) { _ = RunOnceAsync(ShotRegionAsync); return; }
        if (args.Contains("--full")) { _ = RunOnceAsync(ShotFullAsync); return; }

        _main = new MainWindow(() => _ = ShotRegionAsync(), () => _ = ShotFullAsync(),
            () => ClipboardTool.OpenFolder(EnsureDir()), ShowSettings);
        _main.Closed += (_, _) => _life.Shutdown();
        _life.MainWindow = _main;
        _main.Show();
        SetupTray();
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
