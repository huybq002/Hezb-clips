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
        var useAi = !downscale && p.VulkanAvailable && realEsrganExe is not null && File.Exists(realEsrganExe) && EnoughTempSpace(job.InputPath);
        var tmpOut = job.OutputPath + ".part.mp4";
        try
        {
            var done = false;
            if (useAi)
            {
                var factor = Math.Max(job.Target.W / (double)sw, job.Target.H / (double)sh) <= 2.0 ? 2 : 4;
                try { await AiPathAsync(job, fps, factor, tmpOut, progress, ct); done = true; }
                catch (InvalidOperationException) { /* AI tool failed: fall back to Lanczos below */ }
            }
            if (!done) await Run(ffmpeg,
                $"-y -i \"{job.InputPath}\" -vf scale={job.Target.W}:{job.Target.H}:flags=lanczos {FfmpegCommandBuilder.EncoderArgs(enc)} -pix_fmt yuv420p -c:a copy \"{tmpOut}\"", ct);
            File.Move(tmpOut, job.OutputPath, true);
            progress.Report(1);
        }
        finally { try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch (IOException) { } }
    }

    /// <summary>The AI path writes every frame as PNG (large): only use it with plenty of free space in %TEMP%
    /// (about 400x the compressed clip, at least 20 GB free). Otherwise Lanczos is used.</summary>
    static bool EnoughTempSpace(string input)
    {
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!).AvailableFreeSpace;
            var need = Math.Max(20L << 30, new FileInfo(input).Length * 400);
            return free > need;
        }
        catch (Exception) { return false; }
    }

    async Task AiPathAsync(UpscaleJob job, double fps, int factor, string tmpOut, IProgress<double> progress, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("hezb-up").FullName;
        var inDir = Path.Combine(work, "in"); var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(inDir); Directory.CreateDirectory(outDir);
        try
        {
            await Run(ffmpeg, $"-y -i \"{job.InputPath}\" -vsync 0 \"{Path.Combine(inDir, "%08d.png")}\"", ct);
            var total = Directory.GetFiles(inDir).Length;
            var esr = Run(realEsrganExe!, $"-i \"{inDir}\" -o \"{outDir}\" -s {factor} -f png", ct);
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
        if (!File.Exists(Ffprobe)) return await ProbeWithFfmpegAsync(path, ct);
        var o = await RunCapture(Ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=p=0 \"{path}\"", ct);
        var parts = o.Trim().Split(',');
        var rate = parts[2].Split('/');
        var fps = rate.Length == 2 ? double.Parse(rate[0], CultureInfo.InvariantCulture) / double.Parse(rate[1], CultureInfo.InvariantCulture)
                                   : double.Parse(rate[0], CultureInfo.InvariantCulture);
        return (int.Parse(parts[0]), int.Parse(parts[1]), fps);
    }

    /// <summary>Fallback when only ffmpeg.exe is installed: read size and fps from "ffmpeg -i" output.</summary>
    async Task<(int W, int H, double Fps)> ProbeWithFfmpegAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg, $"-hide_banner -i \"{path}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("cannot start ffmpeg");
        var err = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var m = System.Text.RegularExpressions.Regex.Match(err, @"Video:.*?(\d{2,5})x(\d{2,5}).*?([\d.]+) fps");
        if (!m.Success) throw new InvalidOperationException("cannot read video size: " + err[^Math.Min(err.Length, 300)..]);
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
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
