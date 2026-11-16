using System.Diagnostics;
using HezbClips.Core.Video;

namespace HezbClips.Core.Hardware;

/// <summary>Real probes that shell out to ffmpeg / realesrgan. Verified manually (see plan task 14).</summary>
public static class HardwareProbes
{
    public static string FfmpegEncoders(string ffmpeg) => Run(ffmpeg, "-hide_banner -encoders", out _);

    public static bool Vulkan(string? realEsrganExe)
    {
        if (realEsrganExe is null || !File.Exists(realEsrganExe)) return false;
        Run(realEsrganExe, "-h", out var code);
        return code is 0 or 1; // the tool prints usage; it loads Vulkan lazily, so a start without crash is enough
    }

    public static double BenchmarkFps(string ffmpeg, VideoEncoder enc)
    {
        const int frames = 150;
        var args = "-hide_banner -loglevel error -f lavfi -i testsrc2=size=1920x1080:rate=60 " +
                   $"-frames:v {frames} -vf scale=3840:2160:flags=lanczos {FfmpegCommandBuilder.EncoderArgs(enc)} -f null -";
        var sw = Stopwatch.StartNew();
        Run(ffmpeg, args, out var code);
        sw.Stop();
        return code == 0 ? frames / sw.Elapsed.TotalSeconds : 0;
    }

    static string Run(string exe, string args, out int exitCode)
    {
        var psi = new ProcessStartInfo(exe, args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"cannot start {exe}");
        var o = p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        exitCode = p.ExitCode;
        return o.Result;
    }
}
