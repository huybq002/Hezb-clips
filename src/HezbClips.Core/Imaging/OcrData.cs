using System.Security.Cryptography;
using HezbClips.Core.Tools;

namespace HezbClips.Core.Imaging;

/// <summary>Tesseract language data (Vietnamese + English, "tessdata_fast" release 4.1.0): where to find it, and how to fetch and verify it.</summary>
public static class OcrData
{
    /// <summary>Tesseract language string: Vietnamese first, English for mixed text.</summary>
    public const string Languages = "vie+eng";

    public sealed record DataFile(string Name, string Url, string Sha256, long Size);

    const string Base = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/";

    public static readonly IReadOnlyList<DataFile> Files = new[]
    {
        new DataFile("vie.traineddata", Base + "vie.traineddata", "79df64caf7bcfb2a27df5042ecb6121e196eada34da774956995747636d5bfa1", 531275),
        new DataFile("eng.traineddata", Base + "eng.traineddata", "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2", 4113088),
    };

    /// <summary>Folders searched in order: next to the app (installer), then the user's data folder.</summary>
    public static IReadOnlyList<string> Candidates(string appDir, string localAppData) => new[]
    {
        Path.Combine(appDir, "tools", "tessdata"),
        UserDir(localAppData),
    };

    /// <summary>Where the app saves data it downloads itself.</summary>
    public static string UserDir(string localAppData) => Path.Combine(localAppData, "HezbClips", "tessdata");

    /// <summary>First folder that has every language file, or null.</summary>
    public static string? FindDir(IEnumerable<string> candidates, IReadOnlyList<DataFile>? files = null)
    {
        files ??= Files;
        return candidates.FirstOrDefault(d => files.All(f => File.Exists(Path.Combine(d, f.Name))));
    }

    public static bool VerifySha256(string path, string expectedHex)
    {
        using var s = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(s)), expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Download whatever is missing into <paramref name="dir"/> (progress 0..1). Each file is checked against its SHA-256
    /// and moved into place only when it matches, so a broken download never leaves a half file behind.</summary>
    public static async Task EnsureAsync(HttpClient http, string dir, IProgress<double>? progress = null, CancellationToken ct = default, IReadOnlyList<DataFile>? files = null)
    {
        files ??= Files;
        Directory.CreateDirectory(dir);
        var todo = files.Where(f => !File.Exists(Path.Combine(dir, f.Name))).ToList();
        double total = todo.Sum(f => (double)f.Size), done = 0;
        var installer = new ToolInstaller(http);
        foreach (var f in todo)
        {
            var tmp = Path.Combine(dir, f.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var before = done;
                await installer.DownloadAsync(f.Url, tmp, new Progress<double>(p => progress?.Report((before + p * f.Size) / total)), ct);
                if (!VerifySha256(tmp, f.Sha256)) throw new InvalidDataException("Dữ liệu OCR tải về không khớp mã SHA-256: " + f.Name);
                File.Move(tmp, Path.Combine(dir, f.Name), overwrite: true);
                done += f.Size;
            }
            finally { try { File.Delete(tmp); } catch (IOException) { } }
        }
        progress?.Report(1);
    }
}
