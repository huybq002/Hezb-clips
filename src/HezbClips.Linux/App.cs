using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace HezbClips.Linux;

public class App : Application
{
    AppController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _controller = new AppController(desktop);
            _controller.Start(desktop.Args ?? Array.Empty<string>());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
