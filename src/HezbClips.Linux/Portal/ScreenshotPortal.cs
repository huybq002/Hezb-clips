using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tmds.DBus;

namespace HezbClips.Linux.Portal;

[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IScreenshotPortal : IDBusObject
{
    Task<ObjectPath> ScreenshotAsync(string parentWindow, IDictionary<string, object> options);
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IPortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint response, IDictionary<string, object> results)> handler);
}

/// <summary>Whole-desktop screenshot through xdg-desktop-portal (works on GNOME/KDE Wayland and X11).</summary>
public static class ScreenshotPortal
{
    const string Service = "org.freedesktop.portal.Desktop";
    static readonly ObjectPath DesktopPath = new("/org/freedesktop/portal/desktop");

    /// <summary>Silent grab first; if the desktop refuses (no permission yet), retry interactively so the
    /// user can approve it through the desktop's own dialog.</summary>
    public static async Task<Image<Rgba32>> GrabAsync(TimeSpan? timeout = null)
    {
        try { return await GrabOnceAsync(false, timeout); }
        catch (PortalDeniedException) { return await GrabOnceAsync(true, timeout); }
    }

    sealed class PortalDeniedException(uint code) : InvalidOperationException($"Screenshot portal failed (response {code}).");

    static async Task<Image<Rgba32>> GrabOnceAsync(bool interactive, TimeSpan? timeout)
    {
        using var conn = new Connection(Address.Session!);
        var info = await conn.ConnectAsync();

        // Predict the Request object path so the Response signal cannot be missed.
        var token = "hezb" + Guid.NewGuid().ToString("N");
        var sender = info.LocalName.TrimStart(':').Replace('.', '_');
        var requestPath = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{token}");

        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = conn.CreateProxy<IPortalRequest>(Service, requestPath);
        using var _ = await request.WatchResponseAsync(r => tcs.TrySetResult(r));

        var portal = conn.CreateProxy<IScreenshotPortal>(Service, DesktopPath);
        await portal.ScreenshotAsync("", new Dictionary<string, object>
        {
            ["handle_token"] = token,
            ["interactive"] = interactive,
        });

        var (code, results) = await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
        if (code == 1) throw new InvalidOperationException("Screenshot cancelled.");
        if (code != 0 && !interactive) throw new PortalDeniedException(code);
        if (code != 0 || !results.TryGetValue("uri", out var uriObj))
            throw new InvalidOperationException($"Screenshot portal failed (response {code}).");

        var path = new Uri((string)uriObj).LocalPath;
        try { return await Image.LoadAsync<Rgba32>(path); }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
