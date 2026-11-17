using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace HezbClips.App.Windows;

/// <summary>Finds the window (and nested child control) under a physical-pixel point, skipping given handles.
/// Used for ShareX-style "hover to detect a window/frame, click to select it".</summary>
public static class WindowFinder
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    const int DWMWA_CLOAKED = 13, DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const uint CWP_SKIPINVISIBLE = 0x1, CWP_SKIPTRANSPARENT = 0x4;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    /// <summary>Bounds of the window the user is working in (skips our own windows, desktop, taskbar).</summary>
    public static Rectangle? Foreground(ICollection<IntPtr> ignore)
    {
        // the current foreground is our tray menu / main window when started from there: fall back to the last app window
        foreach (var h in new[] { GetForegroundWindow(), ForegroundTracker.Last })
        {
            if (h == IntPtr.Zero || ignore.Contains(h) || IsOwnProcess(h) || !IsWindowVisible(h) || IsIconic(h) || IsDesktop(h)) continue;
            if (TryBounds(h, out var r) && r.Width >= 4 && r.Height >= 4) return r;
        }
        return null;
    }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    internal static bool IsOwnProcess(IntPtr h) { GetWindowThreadProcessId(h, out var pid); return pid == (uint)Environment.ProcessId; }
    internal static bool IsShell(IntPtr h) => IsDesktop(h);

    public static Rectangle? FromPoint(Point physical, ICollection<IntPtr> ignore)
    {
        IntPtr hit = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (ignore.Contains(h) || !IsWindowVisible(h) || IsIconic(h) || IsCloaked(h) || IsDesktop(h)) return true;
            if (!TryBounds(h, out var r) || r.Width < 4 || r.Height < 4 || !r.Contains(physical)) return true;
            hit = h; return false;
        }, IntPtr.Zero);
        if (hit == IntPtr.Zero) return null;

        var best = hit;
        for (int i = 0; i < 6; i++) // descend into nested child controls
        {
            var p = new POINT { X = physical.X, Y = physical.Y };
            ScreenToClient(best, ref p);
            var child = ChildWindowFromPointEx(best, p, CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == best) break;
            best = child;
        }
        if (TryBounds(best, out var cr) && cr.Width >= 4 && cr.Height >= 4) return cr;
        return TryBounds(hit, out var tr) ? tr : null;
    }

    static bool TryBounds(IntPtr h, out Rectangle r)
    {
        // Extended frame bounds exclude the invisible resize border of Win10/11 top-level windows.
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT d, Marshal.SizeOf<RECT>()) == 0 && d.Right > d.Left)
        { r = Rectangle.FromLTRB(d.Left, d.Top, d.Right, d.Bottom); return true; }
        if (GetWindowRect(h, out var w)) { r = Rectangle.FromLTRB(w.Left, w.Top, w.Right, w.Bottom); return true; }
        r = default; return false;
    }

    static bool IsCloaked(IntPtr h) => DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int v, sizeof(int)) == 0 && v != 0;

    static bool IsDesktop(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        var c = sb.ToString();
        return c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}

/// <summary>Remembers the last foreground window that belongs to another app (not ours, not the desktop/taskbar).</summary>
public static class ForegroundTracker
{
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    static WinEventProc? _proc; // kept alive for the hook
    static IntPtr _hook;

    public static IntPtr Last { get; private set; }

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = (_, _, hwnd, _, _, _, _) =>
        {
            if (hwnd != IntPtr.Zero && !WindowFinder.IsOwnProcess(hwnd) && !WindowFinder.IsShell(hwnd)) Last = hwnd;
        };
        _hook = SetWinEventHook(3 /*EVENT_SYSTEM_FOREGROUND*/, 3, IntPtr.Zero, _proc, 0, 0, 0 /*WINEVENT_OUTOFCONTEXT*/);
    }
}
