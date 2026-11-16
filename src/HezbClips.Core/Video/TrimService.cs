namespace HezbClips.Core.Video;

public static class TrimService
{
    const string Fmt = @"hh\:mm\:ss\.fff";

    public static string BuildArgs(string input, string output, TimeSpan start, TimeSpan end, VideoEncoder enc) =>
        $"-y -i \"{input}\" -ss {start.ToString(Fmt)} -to {end.ToString(Fmt)} " +
        $"{FfmpegCommandBuilder.EncoderArgs(enc)} -pix_fmt yuv420p -c:a aac \"{output}\"";
}
