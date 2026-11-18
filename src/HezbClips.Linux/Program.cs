using Avalonia;
using Avalonia.Controls;

namespace HezbClips.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Forward shortcut commands to a running instance without starting the UI.
        var cmd = args.FirstOrDefault(a => a is "--region" or "--full" or "--record" or "--stop")?.TrimStart('-');
        if (cmd != null && Ipc.TrySend(cmd)) return 0;
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
