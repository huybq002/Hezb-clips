using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HezbClips.App.Windows;

/// <summary>An image pinned on top of everything. Drag to move, wheel to zoom, Alt+wheel for opacity,
/// double-click / right-click / Esc to close.</summary>
public sealed class PinWindow : Window
{
    readonly Image _img = new() { Stretch = Stretch.Uniform };
    readonly double _aspect;

    public PinWindow(BitmapSource bmp, System.Drawing.Point? atPhysical = null)
    {
        _aspect = bmp.PixelWidth / (double)bmp.PixelHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.Manual;
        ToolTip = "Kéo để di chuyển · lăn chuột để phóng/thu · Alt+lăn đổi độ mờ · nhấp đúp hoặc Esc để đóng";
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        _img.Source = bmp;

        var wa = SystemParameters.WorkArea;
        var scale = Math.Min(1.0, Math.Min(wa.Width * 0.7 / bmp.PixelWidth, wa.Height * 0.7 / bmp.PixelHeight));
        _img.Width = Math.Max(80, bmp.PixelWidth * scale); _img.Height = _img.Width / _aspect;
        Content = new Border
        {
            BorderBrush = Ui.Brush("Accent"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Child = _img,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 }, Margin = new Thickness(8),
        };
        SizeToContent = SizeToContent.WidthAndHeight;
        Loaded += (_, _) =>
        {
            Left = wa.Left + (wa.Width - ActualWidth) / 2; Top = wa.Top + (wa.Height - ActualHeight) / 2;
            if (atPhysical is { } p)
                WinNative.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, WinNative.HWND_TOPMOST, p.X, p.Y, 0, 0, 0x0001 /*NOSIZE*/);
        };
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Close(); else { try { DragMove(); } catch (InvalidOperationException) { } } };
        MouseRightButtonUp += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) { Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1.0); return; }
            var f = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var w = Math.Clamp(_img.Width * f, 60, wa.Width * 2);
            _img.Width = w; _img.Height = w / _aspect;
        };
    }

    public static PinWindow FromFile(string path, System.Drawing.Point? atPhysical = null)
    {
        var b = new BitmapImage();
        b.BeginInit(); b.UriSource = new Uri(path); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
        return new PinWindow(b, atPhysical);
    }
}
