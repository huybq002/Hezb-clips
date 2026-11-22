using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class OcrDataTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    static string Hex(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
    static string TmpDir() => Directory.CreateTempSubdirectory("ocrdata").FullName;

    static IReadOnlyList<OcrData.DataFile> Pinned(byte[] vie, byte[] eng, string? wrongVie = null) => new[]
    {
        new OcrData.DataFile("vie.traineddata", "https://x/vie", wrongVie ?? Hex(vie), vie.Length),
        new OcrData.DataFile("eng.traineddata", "https://x/eng", Hex(eng), eng.Length),
    };

    static HttpClient Serve(byte[] vie, byte[] eng) => new(new FakeHandler(r =>
        new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath == "/vie" ? vie : eng) }));

    [Fact] public void Pinned_urls_are_fixed_tag_and_hashes_look_valid()
    {
        foreach (var f in OcrData.Files)
        {
            Assert.Contains("/4.1.0/", f.Url);
            Assert.Matches("^[0-9a-f]{64}$", f.Sha256);
        }
        Assert.Equal("vie+eng", OcrData.Languages);
    }

    [Fact] public void Candidates_are_app_tools_then_local_appdata()
    {
        var c = OcrData.Candidates(@"C:\app", @"C:\Users\u\AppData\Local");
        Assert.Equal(Path.Combine(@"C:\app", "tools", "tessdata"), c[0]);
        Assert.Equal(Path.Combine(@"C:\Users\u\AppData\Local", "HezbClips", "tessdata"), c[1]);
    }

    [Fact] public void FindDir_needs_every_file_and_prefers_first()
    {
        byte[] a = [1, 2], b = [3];
        var files = Pinned(a, b);
        var d1 = TmpDir(); var d2 = TmpDir();
        File.WriteAllBytes(Path.Combine(d1, "vie.traineddata"), a); // incomplete
        File.WriteAllBytes(Path.Combine(d2, "vie.traineddata"), a); File.WriteAllBytes(Path.Combine(d2, "eng.traineddata"), b);
        Assert.Equal(d2, OcrData.FindDir([d1, d2], files));
        File.WriteAllBytes(Path.Combine(d1, "eng.traineddata"), b);
        Assert.Equal(d1, OcrData.FindDir([d1, d2], files));
        Assert.Null(OcrData.FindDir([Path.Combine(d1, "nope")], files));
    }

    [Fact] public async Task Ensure_downloads_verifies_and_leaves_no_tmp()
    {
        byte[] vie = [1, 2, 3, 4], eng = [9, 8, 7]; var dir = TmpDir();
        await OcrData.EnsureAsync(Serve(vie, eng), dir, files: Pinned(vie, eng));
        Assert.Equal(vie, File.ReadAllBytes(Path.Combine(dir, "vie.traineddata")));
        Assert.Equal(eng, File.ReadAllBytes(Path.Combine(dir, "eng.traineddata")));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact] public async Task Ensure_rejects_wrong_hash_and_keeps_nothing()
    {
        byte[] vie = [1, 2, 3, 4], eng = [9, 8, 7]; var dir = TmpDir();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            OcrData.EnsureAsync(Serve(vie, eng), dir, files: Pinned(vie, eng, wrongVie: new string('0', 64))));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact] public async Task Ensure_skips_files_already_there()
    {
        byte[] vie = [1, 2, 3, 4], eng = [9, 8, 7]; var dir = TmpDir(); var calls = 0;
        File.WriteAllBytes(Path.Combine(dir, "vie.traineddata"), vie);
        var http = new HttpClient(new FakeHandler(_ => { calls++; return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(eng) }; }));
        await OcrData.EnsureAsync(http, dir, files: Pinned(vie, eng));
        Assert.Equal(1, calls);
    }

    [Fact] public void VerifySha256_matches_case_insensitively()
    {
        var p = Path.Combine(TmpDir(), "f"); File.WriteAllBytes(p, [5, 6]);
        Assert.True(OcrData.VerifySha256(p, Hex([5, 6]).ToUpperInvariant()));
        Assert.False(OcrData.VerifySha256(p, Hex([5, 7])));
    }
}
