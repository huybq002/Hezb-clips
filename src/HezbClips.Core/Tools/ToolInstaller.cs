using System.IO.Compression;

namespace HezbClips.Core.Tools;

/// <summary>Downloads and unpacks optional helper tools (the Real-ESRGAN AI upscaler) into the user's data folder.</summary>
public class ToolInstaller(HttpClient http)
{
    public const string RealEsrganUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip";
    public const string RealEsrganExe = "realesrgan-ncnn-vulkan.exe";

    /// <summary>Download the zip (reporting 0..1 progress), unpack into toolsDir/realesrgan, return the exe path.</summary>
    public async Task<string> InstallRealEsrganAsync(string toolsDir, IProgress<double>? progress = null, CancellationToken ct = default, string url = RealEsrganUrl)
    {
        var dest = Path.Combine(toolsDir, "realesrgan");
        var zip = Path.Combine(Path.GetTempPath(), "hezb-esrgan-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await DownloadAsync(url, zip, progress, ct);
            var staging = dest + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ExtractSafely(zip, staging);

            // some archives wrap everything in one folder: find the exe wherever it is
            var exe = Directory.EnumerateFiles(staging, RealEsrganExe, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("Gói tải về không chứa " + RealEsrganExe);
            var root = Path.GetDirectoryName(exe)!;
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(root, dest);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            return Path.Combine(dest, RealEsrganExe);
        }
        finally { try { File.Delete(zip); } catch (IOException) { } }
    }

    public async Task DownloadAsync(string url, string destFile, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destFile);
        var buf = new byte[81920]; long done = 0; int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            if (total is > 0) progress?.Report((double)done / total.Value);
        }
        progress?.Report(1);
    }

    /// <summary>Unzip, refusing entries that would land outside the target folder.</summary>
    public static void ExtractSafely(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Gói tải về có đường dẫn không hợp lệ: " + entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
