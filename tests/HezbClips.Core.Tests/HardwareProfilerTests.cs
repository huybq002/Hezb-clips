using HezbClips.Core.Hardware;

namespace HezbClips.Core.Tests;

public class HardwareProfilerTests
{
    [Fact] public void Hardware_encoder_detected()
    {
        var p = new HardwareProfiler(() => " V....D h264_nvenc x\n", () => true, () => 80).Detect();
        Assert.True(p.HasHardwareEncoder);
        Assert.Equal(80, p.BenchmarkFps);
    }

    [Fact] public void Probe_failures_give_safe_weak_profile()
    {
        var p = new HardwareProfiler(() => " V....D libx264 x\n",
            () => throw new Exception(), () => throw new Exception()).Detect();
        Assert.False(p.HasHardwareEncoder);
        Assert.False(p.VulkanAvailable);
        Assert.Equal(0, p.BenchmarkFps);
    }

    [Fact] public void Profile_store_roundtrips_and_missing_gives_null()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "hardware.json");
        var store = new HardwareProfileStore(path);
        Assert.Null(store.Load());
        store.Save(new HardwareProfile(8, true, 0, true, 72.5));
        Assert.Equal(new HardwareProfile(8, true, 0, true, 72.5), store.Load());
    }
}
