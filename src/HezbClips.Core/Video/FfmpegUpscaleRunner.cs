using System.Diagnostics;
using System.Globalization;
using HezbClips.Core.Hardware;

namespace HezbClips.Core.Video;

public class FfmpegUpscaleRunner(HardwareProfile p, string ffmpeg, string? realEsrganExe, VideoEncoder enc = VideoEncoder.Libx264) : IUpscaleRunner
{
    string Ffprobe => Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe" + Path.GetExtension(ffmpeg));

    public async Task RunAsync(UpscaleJob job, IProgress<double> progress, CancellationToken ct)
    {
        progress.Report(0);
        var (sw, sh, fps) = await ProbeAsync(job.InputPath, ct);
        var downscale = sw >= job.Target.W && sh >= job.Target.H;
        var useAi = !downscale && p.VulkanAvailable && realEsrganExe is not null && File.Exists(realEsrganExe);
        var tmpOut = job.OutputPath + ".part.mp4";
        try
        {
            if (useAi) await AiPathAsync(job, fps, tmpOut, progress, ct);
            else await Run(ffmpeg,
                $"-y -i \"{job.InputPath}\" -vf scale={job.Target.W}:{job.Target.H}:flags=lanczos {FfmpegCommandBuilder.EncoderArgs(enc)} -pix_fmt yuv420p -c:a copy \"{tmpOut}\"", ct);
            File.Move(tmpOut, job.OutputPath, true);
            progress.Report(1);
        }
        finally { try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch (IOException) { } }
    }

    async Task AiPathAsync(UpscaleJob job, double fps, string tmpOut, IProgress<double> progress, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("hezb-up").FullName;
        var inDir = Path.Combine(work, "in"); var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(inDir); Directory.CreateDirectory(outDir);
        try
        {
            await Run(ffmpeg, $"-y -i \"{job.InputPath}\" -vsync 0 \"{Path.Combine(inDir, "%08d.png")}\"", ct);
            var total = Directory.GetFiles(inDir).Length;
            var esr = Run(realEsrganExe!, $"-i \"{inDir}\" -o \"{outDir}\" -s 4 -f png", ct);
            while (!esr.IsCompleted)
            {
                await Task.WhenAny(esr, Task.Delay(500, ct));
                if (total > 0) progress.Report(0.1 + 0.8 * Math.Min(1.0, Directory.GetFiles(outDir).Length / (double)total));
            }
            await esr;
            await Run(ffmpeg,
                $"-y -framerate {fps.ToString(CultureInfo.InvariantCulture)} -i \"{Path.Combine(outDir, "%08d.png")}\" -i \"{job.InputPath}\" -map 0:v -map 1:a? " +
                $"-vf scale={job.Target.W}:{job.Target.H}:flags=lanczos {FfmpegCommandBuilder.EncoderArgs(enc)} -pix_fmt yuv420p -c:a copy \"{tmpOut}\"", ct);
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }

    async Task<(int W, int H, double Fps)> ProbeAsync(string path, CancellationToken ct)
    {
        var o = await RunCapture(Ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=p=0 \"{path}\"", ct);
        var parts = o.Trim().Split(',');
        var rate = parts[2].Split('/');
        var fps = rate.Length == 2 ? double.Parse(rate[0], CultureInfo.InvariantCulture) / double.Parse(rate[1], CultureInfo.InvariantCulture)
                                   : double.Parse(rate[0], CultureInfo.InvariantCulture);
        return (int.Parse(parts[0]), int.Parse(parts[1]), fps);
    }

    static async Task Run(string exe, string args, CancellationToken ct) => await RunCapture(exe, args, ct);

    static async Task<string> RunCapture(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"cannot start {exe}");
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var o = await stdout; var e = await stderr;
        if (proc.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} exit {proc.ExitCode}: {e[^Math.Min(e.Length, 400)..]}");
        return o;
    }
}
