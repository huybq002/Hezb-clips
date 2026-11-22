using HezbClips.Core.Video;

namespace HezbClips.Core.Tests;

public class FfmpegCommandBuilderTests
{
    [Fact] public void Picks_nvenc_first() => Assert.Equal(VideoEncoder.H264Nvenc,
        EncoderDetector.Pick(" V....D h264_amf  AMD\n V....D h264_nvenc NVIDIA\n"));

    [Fact] public void Falls_back_to_libx264() => Assert.Equal(VideoEncoder.Libx264,
        EncoderDetector.Pick(" V....D libx264 x264\n"));

    [Fact] public void Odd_size_throws() => Assert.Throws<ArgumentException>(() =>
        FfmpegCommandBuilder.VideoPipeArgs(1001, 501, 30, null, VideoEncoder.Libx264, "o.mp4"));

    [Fact] public void Live_args_scale_with_lanczos()
    {
        var a = FfmpegCommandBuilder.VideoPipeArgs(1920, 1080, 30, (3840, 2160), VideoEncoder.H264Nvenc, "o.mp4");
        Assert.Contains("-f rawvideo", a);
        Assert.Contains("-pix_fmt bgra", a);
        Assert.Contains("scale=3840:2160:flags=lanczos", a);
        Assert.Contains("h264_nvenc", a);
    }

    [Fact] public void Native_args_have_no_scale() => Assert.DoesNotContain("scale=",
        FfmpegCommandBuilder.VideoPipeArgs(1920, 1080, 30, null, VideoEncoder.Libx264, "o.mp4"));

    [Fact] public void Mux_both_audio_uses_amix()
    {
        var a = FfmpegCommandBuilder.MuxArgs("v.mp4", "s.wav", "m.wav", "o.mp4");
        Assert.Contains("amix=inputs=2", a);
        Assert.Contains("-c:v copy", a);
    }

    [Fact] public void Mux_without_audio_just_copies() =>
        Assert.DoesNotContain("amix", FfmpegCommandBuilder.MuxArgs("v.mp4", null, null, "o.mp4"));

    [Fact] public void Mux_trims_audio_that_started_before_the_first_frame()
    {
        var a = FfmpegCommandBuilder.MuxArgs("v.mp4", "s.wav", "m.wav", "o.mp4", TimeSpan.FromMilliseconds(367));
        Assert.Contains("-ss 0.367 -i \"s.wav\"", a);
        Assert.Contains("-ss 0.367 -i \"m.wav\"", a);
        Assert.DoesNotContain("-ss 0.367 -i \"v.mp4\"", a);
    }

    [Fact] public void Mux_delays_audio_that_started_after_the_first_frame() =>
        Assert.Contains("-itsoffset 0.25 -i \"s.wav\"", FfmpegCommandBuilder.MuxArgs("v.mp4", "s.wav", null, "o.mp4", TimeSpan.FromMilliseconds(-250)));

    [Fact] public void Mux_without_offset_has_no_shift() =>
        Assert.DoesNotContain("-ss", FfmpegCommandBuilder.MuxArgs("v.mp4", "s.wav", null, "o.mp4"));
}
