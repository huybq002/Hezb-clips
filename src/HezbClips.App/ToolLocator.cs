using System;
using System.IO;
using System.Linq;

namespace HezbClips.App;

/// <summary>Finds ffmpeg and realesrgan: HEZB_FFMPEG env var, app "tools" folder, PATH, then WinGet's install folder.</summary>
public static class ToolLocator
{
    public static string? FindFfmpeg()
    {
        var env = Environment.GetEnvironmentVariable("HEZB_FFMPEG");
        if (env is not null && File.Exists(env)) return env;
        var local = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(local)) return local;
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(d => d.Length > 0).Select(d => Path.Combine(d, "ffmpeg.exe")).FirstOrDefault(File.Exists);
        if (onPath is not null) return onPath;
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(winget))
            return Directory.EnumerateFiles(winget, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
        return null;
    }

    public static string? FindRealEsrgan()
    {
        // installed from Settings (user data folder) first, then next to the app
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HezbClips", "tools", "realesrgan", "realesrgan-ncnn-vulkan.exe");
        if (File.Exists(data)) return data;
        var p = Path.Combine(AppContext.BaseDirectory, "tools", "realesrgan", "realesrgan-ncnn-vulkan.exe");
        return File.Exists(p) ? p : null;
    }
}
