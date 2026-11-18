using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HezbClips.Core.Tools;

namespace HezbClips.Core.Tests;

public class ToolsTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> f) => new(new FakeHandler(f));
    static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    static byte[] Zip(params (string Name, string Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, b) in entries)
                using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write(b);
        return ms.ToArray();
    }

    static string TmpDir() => Directory.CreateTempSubdirectory("tools").FullName;

    [Fact] public async Task Installs_exe_from_flat_zip_and_reports_progress()
    {
        var dir = TmpDir(); var seen = new List<double>();
        var installer = new ToolInstaller(Client(_ => Ok(Zip((ToolInstaller.RealEsrganExe, "x"), ("models/a.bin", "m")))));
        var exe = await installer.InstallRealEsrganAsync(dir, new Progress<double>(seen.Add), url: "https://example.com/a.zip");
        Assert.True(File.Exists(exe));
        Assert.True(File.Exists(Path.Combine(dir, "realesrgan", "models", "a.bin")));
        Directory.Delete(dir, true);
    }

    [Fact] public async Task Installs_exe_when_zip_has_a_top_folder()
    {
        var dir = TmpDir();
        var installer = new ToolInstaller(Client(_ => Ok(Zip(("pack/" + ToolInstaller.RealEsrganExe, "x"), ("pack/models/a.bin", "m")))));
        var exe = await installer.InstallRealEsrganAsync(dir, url: "https://example.com/a.zip");
        Assert.Equal(Path.Combine(dir, "realesrgan", ToolInstaller.RealEsrganExe), exe);
        Assert.True(File.Exists(Path.Combine(dir, "realesrgan", "models", "a.bin")));
        Directory.Delete(dir, true);
    }

    [Fact] public async Task Rejects_zip_slip_entries()
    {
        var dir = TmpDir();
        var installer = new ToolInstaller(Client(_ => Ok(Zip(("../evil.txt", "x"), (ToolInstaller.RealEsrganExe, "x")))));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallRealEsrganAsync(dir, url: "https://example.com/a.zip"));
        Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
        Directory.Delete(dir, true);
    }

    [Fact] public async Task Fails_when_zip_has_no_exe()
    {
        var dir = TmpDir();
        var installer = new ToolInstaller(Client(_ => Ok(Zip(("readme.txt", "x")))));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallRealEsrganAsync(dir, url: "https://example.com/a.zip"));
        Directory.Delete(dir, true);
    }

    [Fact] public async Task Update_check_returns_newer_version_only()
    {
        var feed = "{\"version\":\"1.2.0\",\"url\":\"https://example.com/HezbClips-1.2.0.zip\",\"notes\":\"fix\"}";
        var c = new UpdateChecker(Client(_ => Json(feed)));
        var info = await c.CheckAsync("https://example.com/feed.json", new Version(1, 0, 0));
        Assert.Equal("1.2.0", info!.Version); Assert.Equal("fix", info.Notes);
        Assert.Null(await c.CheckAsync("https://example.com/feed.json", new Version(1, 2, 0)));
        Assert.Null(await c.CheckAsync("https://example.com/feed.json", new Version(2, 0, 0)));
    }

    [Fact] public async Task Update_check_rejects_non_https()
    {
        var c = new UpdateChecker(Client(_ => Json("{}")));
        await Assert.ThrowsAsync<ArgumentException>(() => c.CheckAsync("http://example.com/feed.json", new Version(1, 0)));
        var bad = new UpdateChecker(Client(_ => Json("{\"version\":\"2.0.0\",\"url\":\"http://example.com/a.zip\"}")));
        await Assert.ThrowsAsync<FormatException>(() => bad.CheckAsync("https://example.com/feed.json", new Version(1, 0)));
    }

    [Fact] public async Task Download_verifies_sha256()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var hash = Convert.ToHexString(SHA256.HashData(body));
        var c = new UpdateChecker(Client(_ => Ok(body)));
        var dir = TmpDir();
        var path = await c.DownloadAsync(new UpdateInfo("2.0.0", "https://example.com/app.zip", "", hash), dir);
        Assert.True(File.Exists(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadAsync(new UpdateInfo("2.0.0", "https://example.com/app2.zip", "", "DEADBEEF"), dir));
        Assert.False(File.Exists(Path.Combine(dir, "app2.zip")));
        Directory.Delete(dir, true);
    }
}

public class PageCaptureSessionTests
{
    static byte[] Png(int w, int h, SixLabors.ImageSharp.PixelFormats.Rgba32 c)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(w, h, c);
        using var ms = new MemoryStream(); SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms); return ms.ToArray();
    }

    [Fact] public void Slices_in_any_order_become_one_tall_image()
    {
        var s = new PageCaptureSession();
        Assert.Null(s.Add("a", 2, 3, Png(50, 30, new(0, 0, 255, 255))));
        Assert.Null(s.Add("a", 0, 3, Png(50, 10, new(255, 0, 0, 255))));
        using var img = s.Add("a", 1, 3, Png(50, 20, new(0, 255, 0, 255)))!;
        Assert.Equal(60, img.Height);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0, 255), img[5, 5]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 255, 0, 255), img[5, 15]);
        Assert.Equal(new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 255, 255), img[5, 45]);
    }

    [Fact] public void Sessions_do_not_mix_and_bad_input_is_rejected()
    {
        var s = new PageCaptureSession();
        Assert.Null(s.Add("x", 0, 2, Png(10, 10, new(1, 1, 1, 255))));
        using (var other = s.Add("y", 0, 1, Png(10, 5, new(2, 2, 2, 255)))!) Assert.Equal(5, other.Height);
        Assert.Throws<ArgumentException>(() => s.Add("x", 5, 2, Png(10, 10, new(1, 1, 1, 255))));
        Assert.Throws<ArgumentException>(() => s.Add("x", 1, 3, Png(10, 10, new(1, 1, 1, 255)))); // total changed
        Assert.Throws<ArgumentException>(() => s.Add("", 0, 1, Png(10, 10, new(1, 1, 1, 255))));
        Assert.Throws<ArgumentException>(() => s.Add("z", 0, 1, Array.Empty<byte>()));
    }

    [Fact] public void Different_widths_are_refused()
    {
        var s = new PageCaptureSession();
        s.Add("w", 0, 2, Png(10, 10, new(1, 1, 1, 255)));
        Assert.Throws<InvalidDataException>(() => s.Add("w", 1, 2, Png(20, 10, new(1, 1, 1, 255))));
    }

    [Fact] public void Token_and_origin_checks()
    {
        Assert.True(PageCaptureSession.TokenMatches("abc123", "abc123"));
        Assert.False(PageCaptureSession.TokenMatches("abc123", "abc124"));
        Assert.False(PageCaptureSession.TokenMatches("", ""));
        Assert.False(PageCaptureSession.TokenMatches("abc", null));
        Assert.True(PageCaptureSession.OriginAllowed("chrome-extension://abcdef"));
        Assert.True(PageCaptureSession.OriginAllowed(null));
        Assert.False(PageCaptureSession.OriginAllowed("https://evil.example"));
        Assert.False(PageCaptureSession.OriginAllowed("http://127.0.0.1:8080"));
    }
}
