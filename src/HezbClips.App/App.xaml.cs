using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace HezbClips.App;

public partial class App : Application
{
    const string MutexName = "HezbClips.SingleInstance", ShowEventName = "HezbClips.ShowMain";
    static readonly string HandOverFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HezbClips.edit-request.txt");
    Mutex? _single;
    EventWaitHandle? _show;
    AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            // already running (maybe hidden in the tray): ask it to show its window, then quit
            // (with --edit <file>, the file name is left in a small hand-over file for it to pick up)
            try
            {
                var i = Array.IndexOf(e.Args, "--edit");
                if (i >= 0 && i + 1 < e.Args.Length) System.IO.File.WriteAllText(HandOverFile, System.IO.Path.GetFullPath(e.Args[i + 1]));
                using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set();
            }
            catch (Exception) { }
            Shutdown(); return;
        }

        // One UI font with full Vietnamese coverage, crisp (pixel-snapped) text everywhere, including tooltips and menus.
        var font = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        foreach (var t in new[] { typeof(Window), typeof(System.Windows.Controls.ToolTip), typeof(System.Windows.Controls.ContextMenu) })
        {
            System.Windows.Documents.TextElement.FontFamilyProperty.OverrideMetadata(t, new FrameworkPropertyMetadata(font, FrameworkPropertyMetadataOptions.Inherits));
            System.Windows.Media.TextOptions.TextFormattingModeProperty.OverrideMetadata(t, new FrameworkPropertyMetadata(System.Windows.Media.TextFormattingMode.Display, FrameworkPropertyMetadataOptions.Inherits));
        }

        // nothing should silently kill the app: log, tell the user, keep running
        DispatcherUnhandledException += (_, ex) => { _controller?.ReportUnhandled(ex.Exception); ex.Handled = true; };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) => { Dispatcher.BeginInvoke(() => _controller?.ReportUnhandled(ex.Exception)); ex.SetObserved(); };

        _controller = new AppController(this);
        // "HezbClips.exe --edit picture.png" opens that picture straight in the editor
        var editIdx = Array.IndexOf(e.Args, "--edit");
        var editPath = editIdx >= 0 && editIdx + 1 < e.Args.Length ? e.Args[editIdx + 1] : null;
        _controller.Start(showMain: !e.Args.Contains("--tray") && editPath is null, editPath: editPath);

        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var waiter = new Thread(() =>
        {
            while (_show.WaitOne())
            {
                string? file = null;
                try { if (System.IO.File.Exists(HandOverFile)) { file = System.IO.File.ReadAllText(HandOverFile).Trim(); System.IO.File.Delete(HandOverFile); } } catch (Exception) { }
                Dispatcher.BeginInvoke(() => { if (!string.IsNullOrEmpty(file)) _controller?.EditFromOtherInstance(file); else _controller?.ShowMain(); });
            }
        }) { IsBackground = true, Name = "show-main" };
        waiter.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
