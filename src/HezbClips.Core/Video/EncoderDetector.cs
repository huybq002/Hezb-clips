namespace HezbClips.Core.Video;

public static class EncoderDetector
{
    public static VideoEncoder Pick(string ffmpegEncodersOutput)
    {
        if (ffmpegEncodersOutput.Contains("h264_nvenc")) return VideoEncoder.H264Nvenc;
        if (ffmpegEncodersOutput.Contains("h264_qsv")) return VideoEncoder.H264Qsv;
        if (ffmpegEncodersOutput.Contains("h264_amf")) return VideoEncoder.H264Amf;
        return VideoEncoder.Libx264;
    }
}
