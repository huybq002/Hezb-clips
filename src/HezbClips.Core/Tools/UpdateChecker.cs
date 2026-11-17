using System.Security.Cryptography;
using System.Text.Json;

namespace HezbClips.Core.Tools;

/// <summary>What the update feed says: {"version":"1.2.0","url":"https://…","notes":"…","sha256":"optional"}.</summary>
public record UpdateInfo(string Version, string Url, string Notes, string? Sha256);

public class UpdateChecker(HttpClient http)
{
    /// <summary>Returns the feed's update when it is newer than `current`, otherwise null. Only https feeds are accepted.</summary>
    public async Task<UpdateInfo?> CheckAsync(string feedUrl, Version current, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Địa chỉ cập nhật phải bắt đầu bằng https://", nameof(feedUrl));
        var json = await http.GetStringAsync(u, ct);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var ver = r.GetProperty("version").GetString() ?? "";
        var url = r.GetProperty("url").GetString() ?? "";
        if (!Version.TryParse(ver, out var latest)) throw new FormatException("Phiên bản trong feed không hợp lệ: " + ver);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var dl) || dl.Scheme != Uri.UriSchemeHttps)
            throw new FormatException("Link tải trong feed phải là https.");
        if (latest <= current) return null;
        return new UpdateInfo(ver, url,
            r.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
            r.TryGetProperty("sha256", out var h) ? h.GetString() : null);
    }

    /// <summary>Download the update into destDir and verify its SHA-256 when the feed gave one. Returns the file path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string destDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        var name = Path.GetFileName(new Uri(info.Url).LocalPath);
        if (string.IsNullOrWhiteSpace(name)) name = $"HezbClips-{info.Version}.bin";
        var path = Path.Combine(destDir, name);
        await new ToolInstaller(http).DownloadAsync(info.Url, path, progress, ct);
        if (info.Sha256 is { Length: > 0 } expected)
        {
            await using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                fs.Close(); File.Delete(path);
                throw new InvalidDataException("File tải về không khớp mã SHA-256, đã hủy để an toàn.");
            }
        }
        return path;
    }
}
