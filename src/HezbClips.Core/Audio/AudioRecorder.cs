namespace HezbClips.Core.Audio;

public class AudioRecorder(IAudioInputFactory factory)
{
    readonly List<string> _warnings = new();
    readonly List<(IAudioInput Input, Stream File)> _active = new();
    string? _sysPath, _micPath;

    public void Start(AudioSources which, string systemWavPath, string micWavPath)
    {
        _warnings.Clear(); _active.Clear(); _sysPath = _micPath = null;
        if (which.HasFlag(AudioSources.System))
            _sysPath = Begin(factory.CreateSystem(), systemWavPath, "System audio");
        if (which.HasFlag(AudioSources.Microphone))
            _micPath = Begin(factory.CreateMicrophone(), micWavPath, "Microphone");
    }

    string? Begin(IAudioInput? input, string path, string label)
    {
        if (input is null)
        {
            _warnings.Add($"{label} not available: {(label == "Microphone" ? "mic not found" : "no device")}.");
            return null;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var file = File.Create(path);
            input.Stopped += e =>
            {
                if (e is not null) lock (_warnings) _warnings.Add($"{label} stopped unexpectedly: {e.Message}");
            };
            _active.Add((input, file));
            input.Start(file);
            return path;
        }
        catch (Exception ex)
        {
            _warnings.Add($"{label} failed to start: {ex.Message}");
            return null;
        }
    }

    public AudioResult Stop()
    {
        foreach (var (input, file) in _active)
        {
            try { input.Stop(); } catch (Exception ex) { _warnings.Add($"Audio stop error: {ex.Message}"); }
            try { file.Dispose(); } catch (IOException) { }
        }
        _active.Clear();
        return new AudioResult(_sysPath, _micPath, _warnings.ToArray());
    }
}
