using HezbClips.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HezbClips.Capture;

public class NAudioInputFactory : IAudioInputFactory
{
    public IAudioInput? CreateSystem()
    {
        try { return new NAudioInput(new WasapiLoopbackCapture(), keepLoopbackAlive: true); }
        catch (Exception) { return null; }
    }

    public IAudioInput? CreateMicrophone()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            if (!en.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return null;
            return new NAudioInput(new WasapiCapture(), keepLoopbackAlive: false);
        }
        catch (Exception) { return null; }
    }

    sealed class NAudioInput(IWaveIn capture, bool keepLoopbackAlive) : IAudioInput
    {
        readonly ManualResetEventSlim _stopped = new();
        WaveFileWriter? _writer;
        WasapiOut? _silence;
        volatile bool _paused;

        public void SetPaused(bool paused) => _paused = paused;
        public event Action<Exception?>? Stopped;

        public void Start(Stream wavOut)
        {
            _writer = new WaveFileWriter(wavOut, capture.WaveFormat);
            capture.DataAvailable += (_, e) => { try { if (!_paused) _writer?.Write(e.Buffer, 0, e.BytesRecorded); } catch (ObjectDisposedException) { } };
            capture.RecordingStopped += (_, e) =>
            {
                try { _writer?.Dispose(); } catch (IOException) { }
                _stopped.Set();
                Stopped?.Invoke(e.Exception);
            };
            if (keepLoopbackAlive)
            {
                // Loopback delivers no data while nothing plays, which would squash gaps out of the timeline.
                // Playing silence keeps packets flowing so audio stays in sync with video.
                try
                {
                    _silence = new WasapiOut();
                    _silence.Init(new SilenceProvider(capture.WaveFormat));
                    _silence.Play();
                }
                catch (Exception) { _silence?.Dispose(); _silence = null; }
            }
            capture.StartRecording();
        }

        public void Stop()
        {
            capture.StopRecording();
            _stopped.Wait(TimeSpan.FromSeconds(2));
            _silence?.Dispose();
            capture.Dispose();
        }
    }
}
