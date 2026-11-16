namespace HezbClips.Core.Video;

public record UpscaleJob(Guid Id, string InputPath, string OutputPath, (int W, int H) Target);

public interface IUpscaleRunner
{
    Task RunAsync(UpscaleJob job, IProgress<double> progress, CancellationToken ct);
}
