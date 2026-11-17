using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace HezbClips.App.Windows;

/// <summary>On-screen effects that are meant to be recorded: click ripples, pressed keys, and a webcam bubble.
/// They are normal topmost windows, so the screen recording captures them (unlike the focus overlay and record bar).</summary>
public sealed class RecordingEffects : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public POINT Pt; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KBLL { public uint Vk, Scan, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;

    readonly System.Drawing.Rectangle _region;
    readonly bool _clicks, _keys, _webcam;
    readonly Action<string> _notify;
    HookProc? _mouseProc, _keyProc; // kept in fields so the GC cannot collect the delegates
    IntPtr _mouseHook, _keyHook;
    EffectsWindow? _window;
    WebcamWindow? _cam;

    public RecordingEffects(System.Drawing.Rectangle region, bool clicks, bool keys, bool webcam, Action<string> notify)
    {
        _region = region; _clicks = clicks; _keys = keys; _webcam = webcam; _notify = notify;
    }

    public void Start()
    {
        if (_clicks || _keys)
        {
            _window = new EffectsWindow(_region); _window.Show();
            var mod = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
            if (_clicks)
            {
                _mouseProc = MouseHook;
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
            }
            if (_keys)
            {
                _keyProc = KeyHook;
                _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, mod, 0);
            }
        }
        if (_webcam)
        {
            _cam = new WebcamWindow(_region);
            _ = _cam.StartAsync().ContinueWith(t =>
            {
                if (t.IsFaulted) Application.Current.Dispatcher.Invoke(() =>
                {
                    _cam?.Stop(); _cam = null;
                    _notify("Không mở được webcam: " + t.Exception?.GetBaseException().Message);
                });
            });
        }
    }

    IntPtr MouseHook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var m = (int)w;
            if (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN || m == WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLL>(l);
                _window?.Ripple(info.Pt.X, info.Pt.Y, m == WM_RBUTTONDOWN);
            }
        }
        return CallNextHookEx(_mouseHook, code, w, l);
    }

    IntPtr KeyHook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && ((int)w == WM_KEYDOWN || (int)w == WM_SYSKEYDOWN))
            _window?.OnKey((int)Marshal.PtrToStructure<KBLL>(l).Vk);
        return CallNextHookEx(_keyHook, code, w, l);
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
        _window?.Close(); _window = null;
        _cam?.Stop(); _cam = null;
    }

    // ------------------------------------------------------------------

    /// <summary>Transparent click-through window laid exactly over the recorded region.</summary>
    sealed class EffectsWindow : Window
    {
        readonly System.Drawing.Rectangle _region;
        readonly Canvas _canvas = new() { IsHitTestVisible = false };
        readonly TextBlock _keyText = new() { Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI") };
        readonly Border _keyBadge;
        readonly StringBuilder _typed = new();
        readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(1600) };

        public EffectsWindow(System.Drawing.Rectangle region)
        {
            _region = region;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
            AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.Manual;
            _keyBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(205, 20, 22, 30)), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 8, 18, 8),
                Child = _keyText, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var grid = new Grid(); grid.Children.Add(_canvas);
            _keyBadge.VerticalAlignment = VerticalAlignment.Bottom; _keyBadge.Margin = new Thickness(0, 0, 0, 28);
            grid.Children.Add(_keyBadge);
            Content = grid;
            _fade.Tick += (_, _) => { _fade.Stop(); _typed.Clear(); _keyBadge.Visibility = Visibility.Collapsed; };
            SourceInitialized += (_, _) =>
            {
                WinNative.ClickThrough(this);
                WinNative.SetWindowPos(new WindowInteropHelper(this).Handle, WinNative.HWND_TOPMOST, _region.X, _region.Y, _region.Width, _region.Height,
                    WinNative.SWP_NOACTIVATE | WinNative.SWP_SHOWWINDOW);
            };
        }

        double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        public void Ripple(int sx, int sy, bool right)
        {
            if (!_region.Contains(sx, sy)) return;
            var k = Scale;
            var x = (sx - _region.X) / k; var y = (sy - _region.Y) / k;
            var color = right ? Color.FromRgb(255, 180, 50) : Color.FromRgb(255, 214, 10);
            var ring = new Ellipse
            {
                Width = 44, Height = 44, Stroke = new SolidColorBrush(color), StrokeThickness = 4,
                Fill = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)), RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.4, 0.4),
            };
            Canvas.SetLeft(ring, x - 22); Canvas.SetTop(ring, y - 22);
            _canvas.Children.Add(ring);
            var dur = TimeSpan.FromMilliseconds(520);
            var grow = new DoubleAnimation(0.4, 1.7, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => _canvas.Children.Remove(ring);
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }

        public void OnKey(int vk)
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System) return;
            var ctrl = GetKeyState(0x11) < 0; var alt = GetKeyState(0x12) < 0; var shift = GetKeyState(0x10) < 0; var win = GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0;
            var name = KeyName(key, shift);
            if (name is null) return;

            string label;
            if (ctrl || alt || win)
            {
                _typed.Clear();
                label = string.Join(" + ", new[] { ctrl ? "Ctrl" : null, alt ? "Alt" : null, win ? "Win" : null, shift ? "Shift" : null }.Where(x => x is not null).Append(name));
            }
            else if (name.Length == 1)
            {
                _typed.Append(name); if (_typed.Length > 18) _typed.Remove(0, _typed.Length - 18);
                label = _typed.ToString();
            }
            else { _typed.Clear(); label = shift ? "Shift + " + name : name; }

            _keyText.Text = label; _keyBadge.Visibility = Visibility.Visible;
            _fade.Stop(); _fade.Start();
        }

        static string? KeyName(Key k, bool shift)
        {
            if (k >= Key.A && k <= Key.Z) return shift ? k.ToString() : k.ToString().ToLowerInvariant();
            if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((int)(k - Key.NumPad0)).ToString();
            return k switch
            {
                Key.Space => "Space", Key.Return => "Enter", Key.Back => "⌫", Key.Tab => "Tab", Key.Escape => "Esc", Key.Delete => "Del",
                Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓", Key.Home => "Home", Key.End => "End",
                Key.PageUp => "PgUp", Key.PageDown => "PgDn", Key.Insert => "Ins", Key.CapsLock => null, Key.PrintScreen => "PrtSc",
                Key.OemPeriod => ".", Key.OemComma => ",", Key.OemMinus => "-", Key.OemPlus => "=", Key.OemQuestion => "/", Key.OemSemicolon => ";",
                Key.OemQuotes => "'", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\", Key.OemTilde => "`",
                >= Key.F1 and <= Key.F24 => k.ToString(),
                _ => null,
            };
        }
    }

    // ------------------------------------------------------------------

    /// <summary>Round webcam preview in the corner of the region; drag it to move. Recorded along with the screen.</summary>
    sealed class WebcamWindow : Window
    {
        const int Size = 190;
        readonly System.Drawing.Rectangle _region;
        readonly Image _img = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
        MediaCapture? _capture;
        MediaFrameReader? _reader;
        WriteableBitmap? _wb;
        volatile bool _pending;

        public WebcamWindow(System.Drawing.Rectangle region)
        {
            _region = region;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            AllowsTransparency = true; Background = Brushes.Transparent; Width = Size + 12; Height = Size + 12; WindowStartupLocation = WindowStartupLocation.Manual;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            var disc = new Grid { Width = Size, Height = Size, Clip = new EllipseGeometry(new Point(Size / 2.0, Size / 2.0), Size / 2.0, Size / 2.0) };
            disc.Children.Add(new Border { Background = Brushes.Black });
            disc.Children.Add(_img);
            Content = new Grid
            {
                Children =
                {
                    new Ellipse { Width = Size + 6, Height = Size + 6, Fill = Brushes.White, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.5 } },
                    disc,
                },
            };
            MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
            Left = -10000; Top = -10000; // real position set in physical pixels once the window exists
            Loaded += (_, _) =>
            {
                var k = VisualTreeHelper.GetDpi(this).DpiScaleX;
                int w = (int)(ActualWidth * k), h = (int)(ActualHeight * k);
                WinNative.SetWindowPos(new WindowInteropHelper(this).Handle, WinNative.HWND_TOPMOST,
                    _region.Right - w - 12, _region.Bottom - h - 12, 0, 0, 0x0001 /*NOSIZE*/ | WinNative.SWP_NOACTIVATE);
            };
        }

        volatile bool _stopped;

        public async Task StartAsync()
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color))
                ?? throw new InvalidOperationException("không tìm thấy camera");
            var capture = new MediaCapture();
            _capture = capture;
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group, SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            if (_stopped) { capture.Dispose(); return; } // recording ended while the camera was opening
            var source = capture.FrameSources.Values.First(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            if (_stopped) { reader.Dispose(); capture.Dispose(); return; }
            _reader = reader;
            reader.FrameArrived += OnFrame;
            await Application.Current.Dispatcher.InvokeAsync(Show);
            await reader.StartAsync();
            if (_stopped) Stop();
        }

        void OnFrame(MediaFrameReader reader, MediaFrameArrivedEventArgs e)
        {
            if (_pending) return;
            using var frame = reader.TryAcquireLatestFrame();
            var sb = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (sb is null) return;
            using var conv = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var w = conv.PixelWidth; var h = conv.PixelHeight;
            var buf = new byte[w * h * 4];
            conv.CopyToBuffer(buf.AsBuffer());
            _pending = true;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_wb is null || _wb.PixelWidth != w || _wb.PixelHeight != h) { _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null); _img.Source = _wb; }
                    _wb.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
                }
                finally { _pending = false; }
            });
        }

        public void Stop()
        {
            _stopped = true;
            var reader = _reader; var capture = _capture;
            _reader = null; _capture = null;
            // stop the reader, then release it and the camera (turns the camera light off)
            _ = Task.Run(async () =>
            {
                try { if (reader is not null) { reader.FrameArrived -= OnFrame; await reader.StopAsync(); reader.Dispose(); } } catch (Exception) { }
                try { capture?.Dispose(); } catch (Exception) { }
            });
            Dispatcher.BeginInvoke(Close);
        }
    }
}
