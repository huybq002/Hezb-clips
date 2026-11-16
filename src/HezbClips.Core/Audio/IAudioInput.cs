namespace HezbClips.Core.Audio;

public interface IAudioInput
{
    void Start(Stream wavOut);
    void Stop();
    event Action<Exception?> Stopped;
}

public interface IAudioInputFactory
{
    IAudioInput? CreateSystem();
    IAudioInput? CreateMicrophone();
}

public record AudioResult(string? SystemWav, string? MicWav, IReadOnlyList<string> Warnings);
