using System.Diagnostics;
using HezbClips.Core.Audio;

namespace HezbClips.Linux;

/// <summary>Records via `pw-record` (PipeWire): the default sink's monitor for system audio, the default source for mic.</summary>
public sealed class PwAudioInputFactory : IAudioInputFactory
{
    public IAudioInput? CreateSystem() => PwRecordInput.Available ? new PwRecordInput(systemMonitor: true) : null;
    public IAudioInput? CreateMicrophone() => PwRecordInput.Available ? new PwRecordInput(systemMonitor: false) : null;
}

sealed class PwRecordInput(bool systemMonitor) : IAudioInput
{
    const int Rate = 48000, Channels = 2, Bits = 16;
    Process? _p;
    Task? _copy;
    volatile bool _stopping, _paused;

    public event Action<Exception?>? Stopped;

    public static bool Available { get; } = OnPath("pw-record");

    static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, exe)));

    public void SetPaused(bool paused) => _paused = paused;

    public void Start(Stream wavOut)
    {
        var psi = new ProcessStartInfo("pw-record") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "--rate", "48000", "--channels", "2", "--format", "s16" }) psi.ArgumentList.Add(a);
        if (systemMonitor) { psi.ArgumentList.Add("-P"); psi.ArgumentList.Add("{ stream.capture.sink=true }"); }
        psi.ArgumentList.Add("-");
        _p = Process.Start(psi) ?? throw new InvalidOperationException("pw-record failed to start");
        _p.ErrorDataReceived += (_, _) => { };
        _p.BeginErrorReadLine();
        var p = _p;
        // pw-record writes raw PCM to stdout; wrap it in a WAV header (sizes patched when done).
        WriteHeader(wavOut, 0);
        _copy = Task.Run(() =>
        {
            var buf = new byte[16384]; long total = 0;
            var src = p.StandardOutput.BaseStream;
            try
            {
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (_paused) continue;   // drop audio so the track skips the paused stretch
                    wavOut.Write(buf, 0, n); total += n;
                }
            }
            catch (IOException) { }
            p.WaitForExit();
            try
            {
                if (wavOut.CanSeek) { wavOut.Flush(); wavOut.Position = 0; WriteHeader(wavOut, total); wavOut.Position = wavOut.Length; }
            }
            catch (IOException) { }
            if (!_stopping) Stopped?.Invoke(new IOException($"pw-record exited with code {p.ExitCode}"));
        });
    }

    static void WriteHeader(Stream o, long dataBytes)
    {
        using var w = new BinaryWriter(o, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8); w.Write((uint)(36 + dataBytes)); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16u); w.Write((ushort)1); w.Write((ushort)Channels);
        w.Write((uint)Rate); w.Write((uint)(Rate * Channels * Bits / 8));
        w.Write((ushort)(Channels * Bits / 8)); w.Write((ushort)Bits);
        w.Write("data"u8); w.Write((uint)dataBytes);
    }

    public void Stop()
    {
        _stopping = true;
        var p = _p; if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                // SIGINT lets pw-record finish cleanly; fall back to kill
                using var k = Process.Start("kill", $"-INT {p.Id}");
                k?.WaitForExit(1000);
                if (!p.WaitForExit(2000)) p.Kill();
            }
        }
        catch (InvalidOperationException) { }
        try { _copy?.Wait(2000); } catch (AggregateException) { }
        p.Dispose(); _p = null;
    }
}
