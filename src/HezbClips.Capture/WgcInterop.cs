using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace HezbClips.Capture;

static class WgcInterop
{
    static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(ref Guid iid);
    }

    public static GraphicsCaptureItem ItemForMonitor(IntPtr hmon)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = ItemIid;
        var p = interop.CreateForMonitor(hmon, ref iid);
        try { return GraphicsCaptureItem.FromAbi(p); } finally { Marshal.Release(p); }
    }

    public static GraphicsCaptureItem ItemForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = ItemIid;
        var p = interop.CreateForWindow(hwnd, ref iid);
        try { return GraphicsCaptureItem.FromAbi(p); } finally { Marshal.Release(p); }
    }

    public static ID3D11Device CreateDevice()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? dev).CheckError();
        return dev!;
    }

    public static IDirect3DDevice ToWinRt(ID3D11Device dev)
    {
        using var dxgi = dev.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var p));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(p); } finally { Marshal.Release(p); }
    }

    public static ID3D11Texture2D TextureOf(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = Texture2DIid;
        return new ID3D11Texture2D(access.GetInterface(ref iid));
    }
}

/// <summary>Copies a cropped region of a captured frame into a BGRA byte buffer.</summary>
sealed class FrameReader(ID3D11Device dev) : IDisposable
{
    readonly ID3D11DeviceContext _ctx = dev.ImmediateContext;
    ID3D11Texture2D? _staging; int _sw, _sh;

    public void Read(Direct3D11CaptureFrame frame, Rectangle crop, byte[] dst)
    {
        using var tex = WgcInterop.TextureOf(frame.Surface);
        int w = crop.Width, h = crop.Height;
        if (_staging is null || _sw != w || _sh != h)
        {
            _staging?.Dispose();
            _staging = dev.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read, MiscFlags = ResourceOptionFlags.None
            });
            _sw = w; _sh = h;
        }
        _ctx.CopySubresourceRegion(_staging, 0, 0, 0, 0, tex, 0,
            new Vortice.Mathematics.Box(crop.Left, crop.Top, 0, crop.Right, crop.Bottom, 1));
        var map = _ctx.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(IntPtr.Add(map.DataPointer, y * (int)map.RowPitch), dst, y * w * 4, w * 4);
        }
        finally { _ctx.Unmap(_staging, 0); }
    }

    public void Dispose() { _staging?.Dispose(); _ctx.Dispose(); }
}
