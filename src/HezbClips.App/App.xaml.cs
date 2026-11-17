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
        // One UI font with full Vietnamese coverage, crisp (pixel-snapped) text everywhere, including tooltips and menus.
        var font = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        foreach (var t in new[] { typeof(Window), typeof(System.Windows.Controls.ToolTip), typeof(System.Windows.Controls.ContextMenu) })
        {
            System.Windows.Documents.TextElement.FontFamilyProperty.OverrideMetadata(t, new FrameworkPropertyMetadata(font, FrameworkPropertyMetadataOptions.Inherits));
            System.Windows.Media.TextOptions.TextFormattingModeProperty.OverrideMetadata(t, new FrameworkPropertyMetadata(System.Windows.Media.TextFormattingMode.Display, FrameworkPropertyMetadataOptions.Inherits));
        }
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
