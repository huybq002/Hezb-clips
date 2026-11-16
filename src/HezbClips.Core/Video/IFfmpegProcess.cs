using System.Diagnostics;
using System.Text;

namespace HezbClips.Core.Video;

public interface IFfmpegProcess
{
    Stream StdIn { get; }
    void Start(string args);
    Task<int> ExitAsync();
}

public sealed class FfmpegProcess(string ffmpegPath) : IFfmpegProcess
{
    Process? _p;
    public Stream StdIn => _p!.StandardInput.BaseStream;
    public string StderrTail { get; private set; } = "";

    public void Start(string args)
    {
        var psi = new ProcessStartInfo(ffmpegPath, "-hide_banner -loglevel error " + args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true };
        _p = Process.Start(psi) ?? throw new InvalidOperationException("cannot start ffmpeg");
        var sb = new StringBuilder();
        _p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            sb.AppendLine(e.Data);
            StderrTail = sb.Length > 600 ? sb.ToString(sb.Length - 600, 600) : sb.ToString();
        };
        _p.BeginErrorReadLine();
    }

    public async Task<int> ExitAsync()
    {
        await _p!.WaitForExitAsync();
        return _p.ExitCode;
    }
}
