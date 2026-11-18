using HezbClips.Core.Hotkeys;
using Tmds.DBus;

namespace HezbClips.Linux.Portal;

[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
public interface IGlobalShortcuts : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> BindShortcutsAsync(ObjectPath sessionHandle,
        (string, IDictionary<string, object>)[] shortcuts, string parentWindow, IDictionary<string, object> options);
    Task<IDisposable> WatchActivatedAsync(Action<(ObjectPath session, string id, ulong timestamp, IDictionary<string, object> options)> handler);
}

/// <summary>xdg-desktop-portal GlobalShortcuts (KDE, GNOME 48+). Absent on older GNOME: <see cref="TryStartAsync"/> returns false.</summary>
public sealed class GlobalShortcutsPortal : IDisposable
{
    const string Service = "org.freedesktop.portal.Desktop";
    static readonly ObjectPath DesktopPath = new("/org/freedesktop/portal/desktop");

    Connection? _conn;
    IDisposable? _watch;

    public event Action<string>? Activated;

    /// <summary>shortcuts: id → (description, combo like "Ctrl+Alt+S").</summary>
    public async Task<bool> TryStartAsync(IReadOnlyDictionary<string, (string Description, string Combo)> shortcuts)
    {
        try
        {
            _conn = new Connection(Address.Session!);
            var info = await _conn.ConnectAsync();
            var portal = _conn.CreateProxy<IGlobalShortcuts>(Service, DesktopPath);

            var sessionResults = await RequestAsync(info.LocalName, token => portal.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = "hezbsession" + Guid.NewGuid().ToString("N"),
            }));
            if (!sessionResults.TryGetValue("session_handle", out var sh)) return false;
            var session = new ObjectPath((string)sh);

            _watch = await portal.WatchActivatedAsync(a => Activated?.Invoke(a.id));

            var list = shortcuts.Select(kv => (kv.Key, (IDictionary<string, object>)new Dictionary<string, object>
            {
                ["description"] = kv.Value.Description,
                ["preferred_trigger"] = HotkeyFormats.ToPortalTrigger(kv.Value.Combo),
            })).ToArray();

            await RequestAsync(info.LocalName, token => portal.BindShortcutsAsync(session, list, "",
                new Dictionary<string, object> { ["handle_token"] = token }), TimeSpan.FromMinutes(2));
            return true;
        }
        catch (Exception ex) when (ex is DBusException or InvalidOperationException or TimeoutException or IOException)
        {
            Dispose();
            return false;
        }
    }

    // Predict the Request path first so the Response signal cannot be missed.
    async Task<IDictionary<string, object>> RequestAsync(string? localName, Func<string, Task<ObjectPath>> call, TimeSpan? timeout = null)
    {
        var token = "hezb" + Guid.NewGuid().ToString("N");
        var sender = localName!.TrimStart(':').Replace('.', '_');
        var path = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{token}");
        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var req = _conn!.CreateProxy<IPortalRequest>(Service, path);
        using var _ = await req.WatchResponseAsync(r => tcs.TrySetResult(r));
        await call(token);
        var (code, results) = await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15));
        if (code != 0) throw new InvalidOperationException($"GlobalShortcuts request failed (response {code}).");
        return results;
    }

    public void Dispose()
    {
        _watch?.Dispose(); _watch = null;
        _conn?.Dispose(); _conn = null;
    }
}
