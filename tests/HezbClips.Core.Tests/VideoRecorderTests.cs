using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Audio;
using HezbClips.Core.Capture;
using HezbClips.Core.Hardware;
using HezbClips.Core.Settings;
using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class VideoRecorderTests
{
    class FakeStream : MemoryStream
    {
        public volatile bool Slow;
        public override void Write(byte[] buffer, int offset, int count) { if (Slow) Thread.Sleep(70); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if (Slow) Thread.Sleep(70); base.Write(buffer); }
    }

    class FakeProcess(Action<FakeProcess> onStart) : IFfmpegProcess
    {
        public string Args = "";
        public FakeStream Stream { get; } = new();
        public Stream StdIn => Stream;
        public void Start(string args) { Args = args; onStart(this); }
        public Task<int> ExitAsync() => Task.FromResult(0);
    }

    class FakeFrames : IVideoFrameSource
    {
        public Action<ReadOnlyMemory<byte>>? Callback;
        public Func<FakeStream?> CurrentStream = () => null;
        public void Start(Rectangle r, int fps, Action<ReadOnlyMemory<byte>> cb) => Callback = cb;
        public void Stop() { }
        public void EmitWithDrops(int total, int dropped)
        {
            var frame = new byte[16];
            for (int i = 0; i < total; i++)
            {
                if (CurrentStream() is { } s) s.Slow = i < dropped;
                Callback!(frame);
            }
        }
    }

    class NoopRunner : IUpscaleRunner
    {
        public Task RunAsync(UpscaleJob job, IProgress<double> progress, CancellationToken ct) => Task.CompletedTask;
    }

    class NoopInput : IAudioInput
    {
        public event Action<Exception?>? Stopped { add { } remove { } }
        public void Start(Stream wavOut) { }
        public void Stop() { }
    }

    class AudioFactory(bool micMissing) : IAudioInputFactory
    {
        public IAudioInput? CreateSystem() => new NoopInput();
        public IAudioInput? CreateMicrophone() => micMissing ? null : new NoopInput();
    }

    class Harness
    {
        readonly List<FakeProcess> _procs = new();
        public FakeFrames Frames = new();
        public VideoRecorder Recorder;
        public Harness(bool micMissing = false)
        {
            var settings = new AppSettings { OutputDir = Directory.CreateTempSubdirectory().FullName };
            Frames.CurrentStream = () => _procs.LastOrDefault(p => p.Args.Contains("-f rawvideo"))?.Stream;
            Recorder = new VideoRecorder(Frames, new AudioRecorder(new AudioFactory(micMissing)),
                () => { var p = new FakeProcess(x => { lock (_procs) _procs.Add(x); }); return p; },
                new BackgroundUpscaleQueue(new NoopRunner()), VideoEncoder.Libx264, settings);
        }
        IEnumerable<FakeProcess> Video => _procs.Where(p => p.Args.Contains("-f rawvideo"));
        public string FirstVideoArgs => Video.First().Args;
        public string SecondVideoArgs => Video.Skip(1).First().Args;
        public int VideoProcessCount => Video.Count();
    }

    static RecordRequest Req(Rectangle region, ExportQuality q, RecordMode mode, AudioSources audio = AudioSources.None) =>
        new(region, 30, audio, q, mode);

    [Fact] public async Task Live_mode_passes_scale_to_ffmpeg()
    {
        var h = new Harness();
        h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P2160, RecordMode.Live));
        await h.Recorder.StopAsync();
        Assert.Contains("scale=3840:2160", h.FirstVideoArgs);
    }

    [Fact] public async Task Background_mode_records_native_and_enqueues_upscale()
    {
        var h = new Harness();
        h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P2160, RecordMode.Background));
        var r = await h.Recorder.StopAsync();
        Assert.DoesNotContain("flags=lanczos", h.FirstVideoArgs);
        Assert.NotNull(r.PendingUpscaleJob);
    }

    [Fact] public async Task Odd_region_is_cropped_to_even_before_encoding()
    {
        var h = new Harness();
        h.Recorder.Start(Req(new Rectangle(0, 0, 1001, 501), ExportQuality.P1080, RecordMode.Background));
        await h.Recorder.StopAsync();
        Assert.Contains("-s 1000x500", h.FirstVideoArgs);
    }

    [Fact] public async Task Sustained_drops_in_live_mode_start_unscaled_segment()
    {
        var h = new Harness();
        h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P1080, RecordMode.Live));
        h.Frames.EmitWithDrops(total: 60, dropped: 10);
        var r = await h.Recorder.StopAsync();
        Assert.Equal(2, h.VideoProcessCount);
        Assert.DoesNotContain("flags=lanczos", h.SecondVideoArgs);
        Assert.NotNull(r.PendingUpscaleJob);
    }

    [Fact] public async Task Audio_warning_is_returned_in_result()
    {
        var h = new Harness(micMissing: true);
        h.Recorder.Start(Req(new Rectangle(0, 0, 1280, 720), ExportQuality.P1080, RecordMode.Live, AudioSources.Microphone));
        Assert.NotEmpty((await h.Recorder.StopAsync()).Warnings);
    }
}
