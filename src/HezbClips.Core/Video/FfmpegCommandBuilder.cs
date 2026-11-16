namespace HezbClips.Core.Video;

public static class FfmpegCommandBuilder
{
    public static string EncoderArgs(VideoEncoder enc) => enc switch
    {
        VideoEncoder.H264Nvenc => "-c:v h264_nvenc -preset p5 -cq 19",
        VideoEncoder.H264Qsv => "-c:v h264_qsv -global_quality 19",
        VideoEncoder.H264Amf => "-c:v h264_amf -quality quality -rc cqp -qp_i 19 -qp_p 19",
        _ => "-c:v libx264 -preset veryfast -crf 18",
    };

    public static string VideoPipeArgs(int srcW, int srcH, int fps, (int W, int H)? scaleTo, VideoEncoder enc, string outPath)
    {
        if (srcW % 2 != 0 || srcH % 2 != 0)
            throw new ArgumentException($"source size must be even, got {srcW}x{srcH}");
        if (scaleTo is { } t && (t.W % 2 != 0 || t.H % 2 != 0))
            throw new ArgumentException($"target size must be even, got {t.W}x{t.H}");
        var vf = scaleTo is { } s ? $"-vf scale={s.W}:{s.H}:flags=lanczos " : "";
        return $"-y -f rawvideo -pix_fmt bgra -s {srcW}x{srcH} -r {fps} -i - {vf}{EncoderArgs(enc)} -pix_fmt yuv420p -movflags +faststart \"{outPath}\"";
    }

    public static string MuxArgs(string videoPath, string? systemWav, string? micWav, string outPath)
    {
        var wavs = new[] { systemWav, micWav }.Where(w => w is not null).Cast<string>().ToList();
        var inputs = $"-i \"{videoPath}\" " + string.Concat(wavs.Select(w => $"-i \"{w}\" "));
        return wavs.Count switch
        {
            0 => $"-y {inputs}-c copy \"{outPath}\"",
            1 => $"-y {inputs}-map 0:v -map 1:a -c:v copy -c:a aac \"{outPath}\"",
            _ => $"-y {inputs}-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[a]\" -map 0:v -map \"[a]\" -c:v copy -c:a aac \"{outPath}\"",
        };
    }

    public static string ConcatArgs(string listFilePath, string outPath) =>
        $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outPath}\"";
}
