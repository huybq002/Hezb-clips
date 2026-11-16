using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using HezbClips.Core.Hotkeys;

namespace HezbClips.App.Hotkeys;

/// <summary>RegisterHotKey on a message-only window; raises <see cref="Pressed"/> with the hotkey id.</summary>
public sealed class Win32Hotkeys : IHotkeyNative, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly HwndSource _src;
    public event Action<int>? Pressed;

    public Win32Hotkeys()
    {
        var p = new HwndSourceParameters("HezbClipsHotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _src = new HwndSource(p);
        _src.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) { Pressed?.Invoke(wParam.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }

    public bool Register(int id, uint modifiers, uint vk) => RegisterHotKey(_src.Handle, id, modifiers | MOD_NOREPEAT, vk);
    public void Unregister(int id) => UnregisterHotKey(_src.Handle, id);
    public void Dispose() => _src.Dispose();
}
