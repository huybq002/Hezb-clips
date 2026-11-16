using System.Text.Json;
using HezbClips.Core.Video;

namespace HezbClips.Core.Hardware;

public class HardwareProfiler(Func<string> runFfmpegEncoders, Func<bool> vulkanProbe, Func<double> benchmarkFps)
{
    public HardwareProfile Detect()
    {
        string encoders;
        try { encoders = runFfmpegEncoders(); } catch (Exception) { encoders = ""; }
        bool vulkan;
        try { vulkan = vulkanProbe(); } catch (Exception) { vulkan = false; }
        double fps;
        try { fps = benchmarkFps(); } catch (Exception) { fps = 0; }
        return new HardwareProfile(Environment.ProcessorCount,
            EncoderDetector.Pick(encoders) != VideoEncoder.Libx264, 0, vulkan, fps);
    }
}

public class HardwareProfileStore(string path)
{
    public HardwareProfile? Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<HardwareProfile>(File.ReadAllText(path)) : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public void Save(HardwareProfile p)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(p));
    }

    public void Clear() { try { File.Delete(path); } catch (IOException) { } }
}
