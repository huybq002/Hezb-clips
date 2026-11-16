using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class BackgroundUpscaleQueueTests
{
    class FakeRunner(List<string> log, int delayMs = 0) : IUpscaleRunner
    {
        public string? FailOn;
        public async Task RunAsync(UpscaleJob job, IProgress<double> progress, CancellationToken ct)
        {
            lock (log) log.Add("start " + Path.GetFileNameWithoutExtension(job.InputPath));
            await Task.Delay(delayMs, ct);
            if (job.InputPath == FailOn) throw new InvalidOperationException("boom");
            progress.Report(1);
            lock (log) log.Add("end " + Path.GetFileNameWithoutExtension(job.InputPath));
        }
    }

    [Fact] public async Task Jobs_run_one_at_a_time_in_order()
    {
        var log = new List<string>(); var runner = new FakeRunner(log, 50);
        var q = new BackgroundUpscaleQueue(runner);
        var done = new TaskCompletionSource();
        int n = 0;
        q.Completed += (_, _) => { if (++n == 2) done.SetResult(); };
        q.Enqueue("a.mp4", "a_up.mp4", (3840, 2160));
        q.Enqueue("b.mp4", "b_up.mp4", (3840, 2160));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "start a", "end a", "start b", "end b" }, log);
    }

    [Fact] public async Task Failure_raises_Failed_and_queue_continues()
    {
        var runner = new FakeRunner(new List<string>()) { FailOn = "a.mp4" };
        var q = new BackgroundUpscaleQueue(runner);
        var failed = new TaskCompletionSource<Guid>(); var ok = new TaskCompletionSource<Guid>();
        q.Failed += (id, _) => failed.SetResult(id);
        q.Completed += (id, _) => ok.SetResult(id);
        var a = q.Enqueue("a.mp4", "x", (3840, 2160));
        var b = q.Enqueue("b.mp4", "y", (3840, 2160));
        Assert.Equal(a, await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(b, await ok.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
