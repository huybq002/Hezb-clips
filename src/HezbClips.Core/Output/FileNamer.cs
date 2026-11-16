namespace HezbClips.Core.Output;

public static class FileNamer
{
    public static string Next(string dir, string prefix, string ext, DateTime now, Func<string, bool> exists)
    {
        var stem = $"{prefix}_{now:yyyyMMdd_HHmmss}";
        var path = Path.Combine(dir, $"{stem}.{ext}");
        for (int i = 1; exists(path); i++) path = Path.Combine(dir, $"{stem}_{i}.{ext}");
        return path;
    }
}
