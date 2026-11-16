using HezbClips.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HezbClips.Capture;

public class NAudioInputFactory : IAudioInputFactory
{
    public IAudioInput? CreateSystem()
    {
        try { return new NAudioInput(new WasapiLoopbackCapture()); }
        catch (Exception) { return null; }
    }

    public IAudioInput? CreateMicrophone()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            if (!en.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return null;
            return new NAudioInput(new WasapiCapture());
        }
        catch (Exception) { return null; }
    }

    sealed class NAudioInput(IWaveIn capture) : IAudioInput
    {
        WaveFileWriter? _writer;
        public event Action<Exception?>? Stopped;

        public void Start(Stream wavOut)
        {
            _writer = new WaveFileWriter(wavOut, capture.WaveFormat);
            capture.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            capture.RecordingStopped += (_, e) =>
            {
                _writer?.Flush();
                Stopped?.Invoke(e.Exception);
            };
            capture.StartRecording();
        }

        public void Stop()
        {
            capture.StopRecording();
            _writer?.Dispose();
            capture.Dispose();
        }
    }
}
