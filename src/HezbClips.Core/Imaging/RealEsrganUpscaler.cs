using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HezbClips.Core.Imaging;

public class RealEsrganUpscaler(string exePath) : IImageUpscaler
{
    public Image<Rgba32> Upscale(Image<Rgba32> src, int targetW, int targetH)
    {
        var dir = Directory.CreateTempSubdirectory("hezb-esrgan").FullName;
        try
        {
            var inp = Path.Combine(dir, "in.png"); var outp = Path.Combine(dir, "out.png");
            src.SaveAsPng(inp);
            var psi = new ProcessStartInfo(exePath, $"-i \"{inp}\" -o \"{outp}\" -s 4")
            { CreateNoWindow = true, UseShellExecute = false };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("realesrgan failed to start");
            proc.WaitForExit();
            if (proc.ExitCode != 0 || !File.Exists(outp))
                throw new InvalidOperationException($"realesrgan exit {proc.ExitCode}");
            using var big = Image.Load<Rgba32>(outp);
            return new LanczosUpscaler().Upscale(big, targetW, targetH);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
