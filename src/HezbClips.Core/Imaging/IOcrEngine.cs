namespace HezbClips.Core.Imaging;

/// <summary>Word box in pixels of the image that was recognised.</summary>
public readonly record struct OcrBox(double X, double Y, double Width, double Height);

/// <summary>Recognised word; <paramref name="Confidence"/> is 0..100 when the engine reports one.</summary>
public sealed record OcrWordInfo(string Text, OcrBox Box, double? Confidence = null);

/// <summary>One line of recognised text: its words from left to right.</summary>
public sealed record OcrLineInfo(IReadOnlyList<OcrWordInfo> Words);

/// <summary>Text recognition behind a small seam, so Core logic and tests stay free of Windows APIs and native libraries.</summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>Short name for messages ("Tesseract", "Windows OCR").</summary>
    string Name { get; }

    /// <summary>Largest width or height the engine accepts; callers enlarge small text only up to this.</summary>
    int MaxImageDimension { get; }

    /// <summary>Recognise a PNG image. Runs off the caller's thread.</summary>
    Task<IReadOnlyList<OcrLineInfo>> RecognizeAsync(byte[] png, CancellationToken ct = default);
}
