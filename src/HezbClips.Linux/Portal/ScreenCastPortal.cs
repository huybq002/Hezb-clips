using Tmds.DBus;

namespace HezbClips.Linux.Portal;

[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectSourcesAsync(ObjectPath session, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath session, string parentWindow, IDictionary<string, object> options);
}

[DBusInterface("org.freedesktop.portal.Session")]
public interface IPortalSession : IDBusObject
{
    Task CloseAsync();
}

/// <summary>A running screencast: the PipeWire node to read and (when the portal reports it) the stream size.</summary>
public sealed class ScreenCastSession : IDisposable
{
    readonly Connection _conn;
    readonly ObjectPath _session;
    public uint NodeId { get; }
    public (int W, int H)? Size { get; }

    internal ScreenCastSession(Connection conn, ObjectPath session, uint node, (int, int)? size)
    { _conn = conn; _session = session; NodeId = node; Size = size; }

    public void Dispose()
    {
        try { _conn.CreateProxy<IPortalSession>("org.freedesktop.portal.Desktop", _session).CloseAsync().Wait(2000); }
        catch (Exception) { }
        _conn.Dispose();
    }
}

/// <summary>org.freedesktop.portal.ScreenCast: the desktop asks the user which monitor to share.</summary>
public static class ScreenCastPortal
{
    const string Service = "org.freedesktop.portal.Desktop";
    static readonly ObjectPath DesktopPath = new("/org/freedesktop/portal/desktop");

    public static async Task<ScreenCastSession> StartAsync()
    {
        var conn = new Connection(Address.Session!);
        try
        {
            var info = await conn.ConnectAsync();
            var cast = conn.CreateProxy<IScreenCast>(Service, DesktopPath);

            var created = await Request(conn, info.LocalName, t => cast.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = t,
                ["session_handle_token"] = "hezbcast" + Guid.NewGuid().ToString("N"),
            }));
            var session = new ObjectPath((string)created["session_handle"]);

            try { await Select(conn, info.LocalName, cast, session, cursorEmbedded: true); }
            catch (DBusException) { await Select(conn, info.LocalName, cast, session, cursorEmbedded: false); }

            var started = await Request(conn, info.LocalName, t => cast.StartAsync(session, "",
                new Dictionary<string, object> { ["handle_token"] = t }), TimeSpan.FromMinutes(2));

            var (node, size) = ParseStream(started);
            return new ScreenCastSession(conn, session, node, size);
        }
        catch { conn.Dispose(); throw; }
    }

    static Task<IDictionary<string, object>> Select(Connection conn, string? name, IScreenCast cast, ObjectPath session, bool cursorEmbedded) =>
        Request(conn, name, t =>
        {
            var o = new Dictionary<string, object> { ["handle_token"] = t, ["types"] = 1u, ["multiple"] = false };
            if (cursorEmbedded) o["cursor_mode"] = 2u;
            return cast.SelectSourcesAsync(session, o);
        });

    static (uint Node, (int, int)? Size) ParseStream(IDictionary<string, object> results)
    {
        if (!results.TryGetValue("streams", out var raw)) throw new InvalidOperationException("Screencast returned no streams.");
        if (raw is not (uint, IDictionary<string, object>)[] streams || streams.Length == 0)
            throw new InvalidOperationException("Screencast returned no streams.");
        var (node, props) = streams[0];
        (int, int)? size = null;
        if (props.TryGetValue("size", out var sz) && sz is ValueTuple<int, int> t) size = t;
        return (node, size);
    }

    // Predict the Request path first so the Response signal cannot be missed.
    static async Task<IDictionary<string, object>> Request(Connection conn, string? localName,
        Func<string, Task<ObjectPath>> call, TimeSpan? timeout = null)
    {
        var token = "hezb" + Guid.NewGuid().ToString("N");
        var sender = localName!.TrimStart(':').Replace('.', '_');
        var path = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{token}");
        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var req = conn.CreateProxy<IPortalRequest>(Service, path);
        using var _ = await req.WatchResponseAsync(r => tcs.TrySetResult(r));
        await call(token);
        var (code, results) = await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15));
        if (code == 1) throw new OperationCanceledException("Screen sharing was cancelled.");
        if (code != 0) throw new InvalidOperationException($"ScreenCast portal failed (response {code}).");
        return results;
    }
}
