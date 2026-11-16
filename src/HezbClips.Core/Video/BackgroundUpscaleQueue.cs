using System.Threading.Channels;

namespace HezbClips.Core.Video;

public class BackgroundUpscaleQueue
{
    readonly IUpscaleRunner _runner;
    readonly Channel<UpscaleJob> _jobs = Channel.CreateUnbounded<UpscaleJob>();

    public event Action<Guid, double>? Progress;
    public event Action<Guid, string>? Completed;
    public event Action<Guid, Exception>? Failed;

    public BackgroundUpscaleQueue(IUpscaleRunner runner)
    {
        _runner = runner;
        _ = Task.Run(Worker);
    }

    public Guid Enqueue(string input, string output, (int W, int H) target)
    {
        var job = new UpscaleJob(Guid.NewGuid(), input, output, target);
        _jobs.Writer.TryWrite(job);
        return job.Id;
    }

    async Task Worker()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync())
        {
            try
            {
                await _runner.RunAsync(job, new Progress<double>(p => Progress?.Invoke(job.Id, p)), CancellationToken.None);
                Completed?.Invoke(job.Id, job.OutputPath);
            }
            catch (Exception ex)
            {
                Failed?.Invoke(job.Id, ex);
            }
        }
    }
}
