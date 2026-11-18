using System.Diagnostics;

namespace HezbClips.Linux;

/// <summary>Image/file helpers that shell out (Avalonia 11 cannot put images on the clipboard).</summary>
public static class ClipboardTool
{
    /// <summary>Copies a PNG/JPEG file as an image. Returns false when no clipboard tool is installed.</summary>
    public static bool CopyImage(string path)
    {
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "image/png",
        };
        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var attempts = wayland
            ? new[] { ("wl-copy", $"--type {mime}"), ("xclip", $"-selection clipboard -t {mime} -i") }
            : new[] { ("xclip", $"-selection clipboard -t {mime} -i"), ("wl-copy", $"--type {mime}") };
        foreach (var (exe, args) in attempts)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p is null) continue;
                using (var f = File.OpenRead(path)) f.CopyTo(p.StandardInput.BaseStream);
                p.StandardInput.Close();
                return true;   // wl-copy/xclip stay alive to serve the selection; do not wait
            }
            catch (System.ComponentModel.Win32Exception) { /* not installed */ }
        }
        return false;
    }

    public static void OpenFolder(string pathOrDir)
    {
        var dir = Directory.Exists(pathOrDir) ? pathOrDir : Path.GetDirectoryName(pathOrDir)!;
        try { Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
