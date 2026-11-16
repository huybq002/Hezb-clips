using System.Diagnostics;
using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Audio;
using HezbClips.Core.Capture;
using HezbClips.Core.Hardware;
using HezbClips.Core.Imaging;
using HezbClips.Core.Output;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Video;

public class VideoRecorder(IVideoFrameSource frames, AudioRecorder audio, Func<IFfmpegProcess> ffmpegFactory,
    BackgroundUpscaleQueue queue, VideoEncoder enc, AppSettings s)
{
    readonly object _lock = new();
    readonly SegmentPlan _plan = new();
    readonly List<string> _warnings = new();
    IFfmpegProcess? _proc;
    RecordRequest _req = null!;
    FrameDropMonitor _monitor = new();
    TimeSpan _interval;
    int _w, _h; (int W, int H) _target;
    string _dir = "";
    bool _writeFailed;

    /// <summary>Raised when a recording that was downgraded mid-way has been fully assembled.</summary>
    public event Action<string>? Finalized;
    public event Action<Exception, string>? FinalizeFailed;

    public void Start(RecordRequest r)
    {
        _req = r;
        _w = r.Region.Width & ~1; _h = r.Region.Height & ~1;
        if (_w < 2 || _h < 2) throw new ArgumentException("region too small", nameof(r));
        _target = TargetSizeCalculator.Calculate(_w, _h, r.Quality);
        _dir = Directory.CreateTempSubdirectory("hezb-rec").FullName;
        _plan.Clear(); _warnings.Clear(); _monitor = new FrameDropMonitor(); _writeFailed = false;
        _interval = TimeSpan.FromSeconds(1.0 / r.Fps);
        audio.Start(r.Audio, Path.Combine(_dir, "sys.wav"), Path.Combine(_dir, "mic.wav"));
        OpenSegment(scaled: r.Mode == RecordMode.Live);
        frames.Start(new Rectangle(r.Region.X, r.Region.Y, _w, _h), r.Fps, OnFrame);
    }

    void OpenSegment(bool scaled)
    {
        var path = Path.Combine(_dir, $"seg{_plan.Segments.Count}.mp4");
        var p = ffmpegFactory();
        p.Start(FfmpegCommandBuilder.VideoPipeArgs(_w, _h, _req.Fps, scaled ? _target : null, enc, path));
        _plan.Add(path, scaled);
        _proc = p;
    }

    void CloseCurrent()
    {
        var p = _proc; _proc = null;
        if (p is null) return;
        try { p.StdIn.Dispose(); } catch (IOException) { }
        if (p.ExitAsync().GetAwaiter().GetResult() != 0) _warnings.Add("ffmpeg segment exited with an error.");
    }

    void OnFrame(ReadOnlyMemory<byte> frame)
    {
        lock (_lock)
        {
            if (_proc is null || _writeFailed) return;
            var sw = Stopwatch.StartNew();
            try { _proc.StdIn.Write(frame.Span); }
            catch (IOException) { _writeFailed = true; _warnings.Add("Video encoder stopped accepting frames."); return; }
            _monitor.Report(sw.Elapsed > _interval * 1.5);
            if (_req.Mode == RecordMode.Live && _plan.CurrentScaled && _monitor.ShouldDowngrade)
            {
                CloseCurrent();
                OpenSegment(scaled: false);
                _monitor = new FrameDropMonitor();
            }
        }
    }

    public async Task<RecordResult> StopAsync()
    {
        frames.Stop();
        // Stop audio first: flushing the video encoder can take seconds, and audio must not outlast the video.
        var a = audio.Stop();
        lock (_lock) CloseCurrent();
        _warnings.AddRange(a.Warnings);

        Directory.CreateDirectory(s.OutputDir);
        var final = FileNamer.Next(s.OutputDir, "Clip", "mp4", DateTime.Now, File.Exists);
        var segs = _plan.Segments;
        Guid? job = null;

        if (segs.Count == 1)
        {
            await RunFfmpeg(FfmpegCommandBuilder.MuxArgs(segs[0].Path, a.SystemWav, a.MicWav, final));
            if (!segs[0].Scaled)
            {
                var up = Path.ChangeExtension(final, null) + $"_{(int)_req.Quality}p.mp4";
                job = queue.Enqueue(final, up, _target);
            }
            TryDeleteDir(_dir);
        }
        else
        {
            job = FinalizeAfterUpscale(segs, a, final);
        }
        return new RecordResult(final, job, _warnings.ToArray());
    }

    // Live recording that was downgraded: [scaled segment, unscaled segment]. Upscale the tail in the
    // background queue, then concat + mux and raise Finalized.
    Guid FinalizeAfterUpscale(IReadOnlyList<Segment> segs, AudioResult a, string final)
    {
        var tail = segs[^1].Path;
        var dir = _dir; var head = segs[0].Path;
        var tailUp = Path.Combine(dir, "tail_up.mp4");
        var gate = new object(); Guid? mine = null; var early = new List<Guid>();

        async Task Go()
        {
            try
            {
                var list = Path.Combine(dir, "list.txt");
                File.WriteAllLines(list, new[] { head, tailUp }.Select(p => "file '" + p.Replace('\\', '/') + "'"));
                var joined = Path.Combine(dir, "joined.mp4");
                await RunFfmpeg(FfmpegCommandBuilder.ConcatArgs(list, joined));
                await RunFfmpeg(FfmpegCommandBuilder.MuxArgs(joined, a.SystemWav, a.MicWav, final));
                Finalized?.Invoke(final);
                TryDeleteDir(dir);
            }
            catch (Exception ex) { FinalizeFailed?.Invoke(ex, dir); }
        }

        void OnDone(Guid id, string outputPath)
        {
            lock (gate) { if (mine is null) { early.Add(id); return; } if (id != mine) return; }
            queue.Completed -= OnDone; queue.Failed -= OnFail;
            _ = Go();
        }
        void OnFail(Guid id, Exception ex)
        {
            lock (gate) { if (mine is null || id != mine) return; }
            queue.Completed -= OnDone; queue.Failed -= OnFail;
            FinalizeFailed?.Invoke(ex, dir);
        }

        queue.Completed += OnDone; queue.Failed += OnFail;
        Guid job; bool already;
        lock (gate) { job = queue.Enqueue(tail, tailUp, _target); mine = job; already = early.Contains(job); }
        if (already) { queue.Completed -= OnDone; queue.Failed -= OnFail; _ = Go(); }
        return job;
    }

    async Task RunFfmpeg(string args)
    {
        var p = ffmpegFactory();
        p.Start(args);
        var code = await p.ExitAsync();
        if (code != 0) throw new InvalidOperationException($"ffmpeg exited with code {code}");
    }

    static void TryDeleteDir(string d)
    {
        try { Directory.Delete(d, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
