using HezbClips.Core.Audio;

namespace HezbClips.Core.Tests;

public class AudioRecorderTests
{
    class FakeInput : IAudioInput
    {
        public bool Started;
        public event Action<Exception?>? Stopped;
        public void Start(Stream wavOut) => Started = true;
        public void Stop() { }
        public void RaiseStopped(Exception? e) => Stopped?.Invoke(e);
    }

    class FakeFactory(IAudioInput? system, IAudioInput? mic) : IAudioInputFactory
    {
        public IAudioInput? CreateSystem() => system;
        public IAudioInput? CreateMicrophone() => mic;
    }

    static (string Sys, string Mic) Paths()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        return (Path.Combine(d, "s.wav"), Path.Combine(d, "m.wav"));
    }

    [Fact] public void Missing_mic_warns_and_does_not_throw()
    {
        var (s, m) = Paths();
        var rec = new AudioRecorder(new FakeFactory(new FakeInput(), null));
        rec.Start(AudioSources.System | AudioSources.Microphone, s, m);
        var r = rec.Stop();
        Assert.Null(r.MicWav);
        Assert.NotNull(r.SystemWav);
        Assert.Contains(r.Warnings, w => w.Contains("mic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void Mic_unplugged_midway_keeps_file_and_warns()
    {
        var (s, m) = Paths();
        var mic = new FakeInput();
        var rec = new AudioRecorder(new FakeFactory(null, mic));
        rec.Start(AudioSources.Microphone, s, m);
        mic.RaiseStopped(new InvalidOperationException("device removed"));
        var r = rec.Stop();
        Assert.Equal(m, r.MicWav);
        Assert.Single(r.Warnings);
    }

    [Fact] public void Only_requested_sources_are_started()
    {
        var (s, m) = Paths();
        var sys = new FakeInput(); var mic = new FakeInput();
        new AudioRecorder(new FakeFactory(sys, mic)).Start(AudioSources.System, s, m);
        Assert.True(sys.Started);
        Assert.False(mic.Started);
    }
}
