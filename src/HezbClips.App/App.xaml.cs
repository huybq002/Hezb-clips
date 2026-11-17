using System.Linq;
using System.Threading;
using System.Windows;

namespace HezbClips.App;

public partial class App : Application
{
    Mutex? _single;
    AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, "HezbClips.SingleInstance", out var created);
        if (!created) { Shutdown(); return; }
        _controller = new AppController(this);
        _controller.Start(showMain: !e.Args.Contains("--tray"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
