using HezbClips.Core.Hardware;
using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class FfmpegUpscaleRunnerTests
{
    static string? FindOnPath(string exe) =>
        Environment.GetEnvironmentVariable("FFMPEG_DIR") is { } d0 && File.Exists(Path.Combine(d0, exe)) ? Path.Combine(d0, exe) :
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);

    // Integration test: needs ffmpeg + ffprobe on PATH or in FFMPEG_DIR; returns early (vacuous pass) otherwise.
    [Fact] public async Task Lanczos_path_scales_video_to_target()
    {
        var ffmpeg = FindOnPath("ffmpeg.exe");
        if (ffmpeg is null || FindOnPath("ffprobe.exe") is null) return;
        var dir = Directory.CreateTempSubdirectory().FullName;
        var inp = Path.Combine(dir, "in.mp4"); var outp = Path.Combine(dir, "out.mp4");
        var gen = System.Diagnostics.Process.Start(ffmpeg,
            $"-loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=30 -t 1 -pix_fmt yuv420p \"{inp}\"")!;
        await gen.WaitForExitAsync();
        var runner = new FfmpegUpscaleRunner(new HardwareProfile(4, false, 0, false, 0), ffmpeg, null);
        await runner.RunAsync(new UpscaleJob(Guid.NewGuid(), inp, outp, (1920, 1080)), new Progress<double>(), default);
        var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"),
            $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{outp}\"")
        { RedirectStandardOutput = true, UseShellExecute = false })!;
        var o = (await probe.StandardOutput.ReadToEndAsync()).Trim();
        Assert.Equal("1920,1080", o);
    }
}
