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
        // RGB -> YUV with the BT.709 matrix and TV range, and tag the stream the same way: untagged video converted with the
        // default BT.601 matrix is shown with shifted colours by players, which assume BT.709 for HD sizes
        var size = scaleTo is { } s ? $"{s.W}:{s.H}:flags=lanczos:" : "";
        var vf = $"-vf scale={size}out_color_matrix=bt709:out_range=tv ";
        return $"-y -f rawvideo -pix_fmt bgra -s {srcW}x{srcH} -r {fps} -i - {vf}{EncoderArgs(enc)} -pix_fmt yuv420p " +
               $"-colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv -movflags +faststart \"{outPath}\"";
    }

    /// <param name="audioLead">How much earlier the audio capture began than the first video frame (negative: later).
    /// The audio is trimmed (or delayed) by that much so picture and sound start together.</param>
    public static string MuxArgs(string videoPath, string? systemWav, string? micWav, string outPath, TimeSpan audioLead = default)
    {
        var wavs = new[] { systemWav, micWav }.Where(w => w is not null).Cast<string>().ToList();
        var shift = audioLead.TotalSeconds switch
        {
            >= 0.005 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"-ss {audioLead.TotalSeconds:0.###} "),
            <= -0.005 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"-itsoffset {-audioLead.TotalSeconds:0.###} "),
            _ => "",
        };
        var inputs = $"-i \"{videoPath}\" " + string.Concat(wavs.Select(w => $"{shift}-i \"{w}\" "));
        return wavs.Count switch
        {
            0 => $"-y {inputs}-c copy \"{outPath}\"",
            1 => $"-y {inputs}-map 0:v -map 1:a -c:v copy -c:a aac \"{outPath}\"",
            _ => $"-y {inputs}-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[a]\" -map 0:v -map \"[a]\" -c:v copy -c:a aac \"{outPath}\"",
        };
    }

    public static string ConcatArgs(string listFilePath, string outPath) =>
        $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outPath}\"";

    /// <summary>Longest GIF recording allowed.</summary>
    public static readonly TimeSpan GifMaxDuration = TimeSpan.FromSeconds(60);

    /// <summary>Convert a recorded clip to an optimized GIF (shared palette, lanczos downscale to maxWidth).</summary>
    public static string GifArgs(string inputPath, string outPath, int fps = 15, int maxWidth = 960) =>
        $"-y -i \"{inputPath}\" -an -vf \"fps={fps},scale='min({(maxWidth <= 0 ? 100000 : maxWidth)},iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4\" -loop 0 \"{outPath}\"";
}
