using System.Drawing;
using System.Runtime.InteropServices;

namespace HezbClips.Capture;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [DllImport("d3d11.dll")]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static List<(IntPtr Handle, Rectangle Bounds)> Monitors()
    {
        var list = new List<(IntPtr, Rectangle)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT r, IntPtr _) =>
        {
            list.Add((h, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static (IntPtr Handle, Rectangle Bounds) MonitorContaining(Rectangle region)
    {
        var all = Monitors();
        if (all.Count == 0) throw new InvalidOperationException("no monitors");
        var center = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
        foreach (var m in all) if (m.Bounds.Contains(center)) return m;
        return all[0];
    }
}
