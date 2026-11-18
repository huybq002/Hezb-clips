using Avalonia;
using Avalonia.Controls;

namespace HezbClips.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
}
