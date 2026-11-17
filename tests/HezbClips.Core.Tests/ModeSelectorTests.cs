using HezbClips.Core.Hardware;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Tests;

public class ModeSelectorTests
{
    static HardwareProfile P(double fps, bool enc = true, bool vk = true) => new(8, enc, 4096, vk, fps);

    [Fact] public void Auto_strong_machine_is_live() =>
        Assert.Equal(RecordMode.Live, ModeSelector.Choose(P(70), ExportQuality.P2160, VideoMode.Auto));
    [Fact] public void Auto_below_threshold_is_background() =>
        Assert.Equal(RecordMode.Background, ModeSelector.Choose(P(50), ExportQuality.P2160, VideoMode.Auto));
    [Fact] public void Auto_without_hw_encoder_is_background() =>
        Assert.Equal(RecordMode.Background, ModeSelector.Choose(P(200, enc: false), ExportQuality.P1080, VideoMode.Auto));
    [Fact] public void Auto_fast_machine_without_vulkan_is_still_live() =>
        Assert.Equal(RecordMode.Live, ModeSelector.Choose(P(70, vk: false), ExportQuality.P2160, VideoMode.Auto));
    [Fact] public void Forced_mode_wins() =>
        Assert.Equal(RecordMode.Live, ModeSelector.Choose(P(1, false, false), ExportQuality.P2160, VideoMode.Live));

    [Fact] public void Monitor_downgrades_after_window_with_enough_drops()
    {
        var m = new FrameDropMonitor();
        for (int i = 0; i < 60; i++) m.Report(i < 6);
        Assert.True(m.ShouldDowngrade);
    }

    [Fact] public void Monitor_waits_for_full_window()
    {
        var m = new FrameDropMonitor();
        for (int i = 0; i < 10; i++) m.Report(true);
        Assert.False(m.ShouldDowngrade);
    }
}
