using System.Net.Sockets;
using System.Text;

namespace HezbClips.Linux;

/// <summary>Tiny unix-socket channel so `hezbclips --record` (a desktop shortcut) can drive the running app.</summary>
public static class Ipc
{
    static string SocketPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir, "hezbclips.sock");
        }
    }

    /// <summary>Sends a command to a running instance. False when none is listening.</summary>
    public static bool TrySend(string command)
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.SendTimeout = 500;
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            s.Send(Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch (SocketException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>Starts listening; <paramref name="onCommand"/> runs on a background thread.</summary>
    public static IDisposable Listen(Action<string> onCommand)
    {
        try { File.Delete(SocketPath); } catch (IOException) { }   // stale socket from a crashed run
        var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(SocketPath));
        server.Listen(4);
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var c = server.Accept();
                    using var r = new StreamReader(new NetworkStream(c));
                    var line = r.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(line)) onCommand(line);
                }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (IOException) { }
            }
        }) { IsBackground = true, Name = "ipc" };
        t.Start();
        return new Cleanup(server);
    }

    sealed class Cleanup(Socket s) : IDisposable
    {
        public void Dispose()
        {
            s.Dispose();
            try { File.Delete(SocketPath); } catch (IOException) { }
        }
    }
}
