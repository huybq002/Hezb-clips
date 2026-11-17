namespace HezbClips.Core.Audio;

public interface IAudioInput
{
    void Start(Stream wavOut);
    void Stop();
    event Action<Exception?> Stopped;
    /// <summary>Drop incoming audio while paused (default: ignored).</summary>
    void SetPaused(bool paused) { }
}

public interface IAudioInputFactory
{
    IAudioInput? CreateSystem();
    IAudioInput? CreateMicrophone();
}

public record AudioResult(string? SystemWav, string? MicWav, IReadOnlyList<string> Warnings);
