using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HezbClips.Core.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.App;

/// <summary>Tiny local web server (127.0.0.1 only) the browser extension posts full-page screenshot slices to.
/// Every request needs the secret token and must come from the extension itself, never from a web page.</summary>
public sealed class BrowserBridge : IDisposable
{
    public const int Port = 47831;
    readonly Func<string> _token;
    readonly Action<Image<Rgba32>, string> _onPage;
    readonly PageCaptureSession _session = new();
    HttpListener? _listener;

    public bool Running => _listener?.IsListening == true;
    public string Status { get; private set; } = "Đang tắt";

    /// <param name="onPage">Called (on a worker thread) with a finished page image and its title. The callee disposes the image.</param>
    public BrowserBridge(Func<string> token, Action<Image<Rgba32>, string> onPage) { _token = token; _onPage = onPage; }

    public bool Start()
    {
        if (Running) return true;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            Status = $"Đang lắng nghe tại 127.0.0.1:{Port}";
            _ = Task.Run(Loop);
            return true;
        }
        catch (Exception ex)
        {
            Status = "Không mở được cổng " + Port + ": " + ex.Message;
            try { _listener?.Close(); } catch (Exception) { }
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch (Exception) { }
        _listener = null; Status = "Đang tắt";
    }

    async Task Loop()
    {
        var l = _listener;
        while (l is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await l.GetContextAsync(); }
            catch (Exception) { break; } // listener stopped
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request; var res = ctx.Response;
        try
        {
            var origin = req.Headers["Origin"];
            // blocks DNS-rebinding style calls from web pages and any foreign origin
            if (req.Url?.Host != "127.0.0.1" || !PageCaptureSession.OriginAllowed(origin)) { Reply(res, 403, "forbidden"); return; }
            if (origin is not null)
            {
                res.Headers["Access-Control-Allow-Origin"] = origin;
                res.Headers["Access-Control-Allow-Headers"] = "X-Hezb-Token, Content-Type";
                res.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";
                res.Headers["Access-Control-Max-Age"] = "600";
                res.Headers["Vary"] = "Origin";
            }
            if (req.HttpMethod == "OPTIONS") { Reply(res, 204, ""); return; }
            if (!PageCaptureSession.TokenMatches(_token(), req.Headers["X-Hezb-Token"])) { Reply(res, 401, "bad token"); return; }

            var path = req.Url!.AbsolutePath;
            if (req.HttpMethod == "GET" && path == "/ping") { Reply(res, 200, "hezb-clips"); return; }
            if (req.HttpMethod == "POST" && path == "/part")
            {
                var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                if (!int.TryParse(q["i"], out var index) || !int.TryParse(q["t"], out var total)) { Reply(res, 400, "bad part numbers"); return; }
                if (req.ContentLength64 > PageCaptureSession.MaxPartBytes) { Reply(res, 413, "too large"); return; }
                using var ms = new MemoryStream();
                req.InputStream.CopyTo(ms);
                Image<Rgba32>? done;
                try { done = _session.Add(q["s"] ?? "", index, total, ms.ToArray()); }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or UnknownImageFormatException or InvalidImageContentException)
                { Reply(res, 400, ex.Message); return; }
                if (done is not null) _onPage(done, q["title"] ?? "");
                Reply(res, 200, done is null ? "{\"done\":false}" : "{\"done\":true}");
                return;
            }
            Reply(res, 404, "not found");
        }
        catch (Exception ex)
        {
            try { Reply(res, 500, ex.Message); } catch (Exception) { }
        }
    }

    static void Reply(HttpListenerResponse res, int code, string body)
    {
        res.StatusCode = code;
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentType = "text/plain; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) res.OutputStream.Write(bytes);
        res.Close();
    }

    public void Dispose() => Stop();
}
