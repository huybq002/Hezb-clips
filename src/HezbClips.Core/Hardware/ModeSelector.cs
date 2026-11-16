using HezbClips.Core.Settings;

namespace HezbClips.Core.Hardware;

public static class ModeSelector
{
    public static RecordMode Choose(HardwareProfile p, ExportQuality q, VideoMode forced)
    {
        if (forced == VideoMode.Live) return RecordMode.Live;
        if (forced == VideoMode.Background) return RecordMode.Background;
        double need = q switch { ExportQuality.P1080 => 30, ExportQuality.P1440 => 45, _ => 60 };
        return p.HasHardwareEncoder && p.VulkanAvailable && p.BenchmarkFps >= need
            ? RecordMode.Live : RecordMode.Background;
    }
}
