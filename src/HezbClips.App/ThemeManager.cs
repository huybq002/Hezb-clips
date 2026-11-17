using System;
using System.Windows;
using System.Windows.Media;
using HezbClips.Core.Settings;
using Microsoft.Win32;

namespace HezbClips.App;

/// <summary>Switches dark/light at runtime. Brushes are created here in code (never frozen) and the same
/// instances are recoloured on switch, so every open window updates immediately.</summary>
public static class ThemeManager
{
    static readonly (string Key, string Dark, string Light)[] Colors =
    {
        ("Bg", "#1B1C22", "#F4F5FA"), ("Surface", "#272832", "#FFFFFF"), ("SurfaceHover", "#33344A", "#E4E7F4"),
        ("Border", "#3A3C4E", "#D3D6E6"), ("Text", "#F2F3F8", "#1B1C22"), ("TextDim", "#9A9DB5", "#5C5F78"),
        ("Disabled", "#55576A", "#9A9DB5"),
    };

    static readonly System.Collections.Generic.Dictionary<string, SolidColorBrush> Brushes = new();

    static void Init()
    {
        if (Brushes.Count > 0) return;
        foreach (var (key, d, _) in Colors)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(d));
            Brushes[key] = b;
            Application.Current.Resources[key] = b.Clone(); // XAML templates (DynamicResource) get their own copy
        }
    }

    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
        Init();
        var dark = theme switch { AppTheme.Dark => true, AppTheme.Light => false, _ => !SystemUsesLight() };
        IsDark = dark;
        foreach (var (key, d, l) in Colors)
        {
            var c = (Color)ColorConverter.ConvertFromString(dark ? d : l);
            Brushes[key].Color = c;                                   // code-built UI: shared brush, recoloured in place
            Application.Current.Resources[key] = new SolidColorBrush(c); // XAML templates: DynamicResource swaps to the new brush
        }
        Changed?.Invoke();
    }

    /// <summary>Whether the Windows taskbar is light (for choosing the tray icon).</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Shared mutable brush for code-built UI, or null when the key is not a theme colour.</summary>
    public static Brush? Get(string key) { Init(); return Brushes.TryGetValue(key, out var b) ? b : null; }

    static bool SystemUsesLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }
}
