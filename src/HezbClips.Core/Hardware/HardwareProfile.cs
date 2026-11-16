namespace HezbClips.Core.Hardware;

public record HardwareProfile(int CpuCores, bool HasHardwareEncoder, long GpuVramMb, bool VulkanAvailable, double BenchmarkFps);

public enum RecordMode { Live, Background }
