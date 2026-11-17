using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HezbClips.App.Windows;

/// <summary>Shared look: brushes/styles come from Theme.xaml.</summary>
public static class Ui
{
    public static Brush Brush(string key) => ThemeManager.Get(key) ?? (Brush)Application.Current.FindResource(key);
    public static System.Windows.Media.Imaging.BitmapImage Logo(string name = "logo-dark.png", int decodeWidth = 0)
    {
        var b = new System.Windows.Media.Imaging.BitmapImage();
        b.BeginInit(); b.UriSource = new System.Uri("pack://application:,,,/Assets/" + name);
        if (decodeWidth > 0) b.DecodePixelWidth = decodeWidth;
        b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
        return b;
    }

    /// <summary>Logo image that swaps between the dark and light variants when the theme changes.</summary>
    public sealed class LogoImage : System.Windows.Controls.Image
    {
        public LogoImage()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            Source = Current();
            Loaded += (_, _) => { ThemeManager.Changed += Update; Update(); };
            Unloaded += (_, _) => ThemeManager.Changed -= Update;
        }
        static System.Windows.Media.Imaging.BitmapImage Current() => Logo(ThemeManager.IsDark ? "logo-dark.png" : "logo-light.png", 128);
        void Update() => Source = Current();
    }

    /// <summary>Looping animated GIF that starts by itself (see GifPlayer). Fills its parent, cropped to fit.</summary>
    public static GifPlayer AnimatedGif(string path, double height, int decodeWidth = 220, int maxFrames = 75) =>
        new(path, decodeWidth, maxFrames) { Height = height, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };

    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    /// <summary>Rounded dark card with soft shadow. Host window must set AllowsTransparency + transparent background.</summary>
    public static Border Card(UIElement child, double padding = 12) => new()
    {
        Background = Brush("Bg"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), Margin = new Thickness(12),
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black },
        Child = child,
    };

    public static TextBlock Label(string text, bool dim = false, double size = 13) => new()
    {
        Text = text, Foreground = Brush(dim ? "TextDim" : "Text"), FontSize = size, TextWrapping = TextWrapping.Wrap,
    };
}
