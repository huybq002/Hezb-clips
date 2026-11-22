using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HezbClips.Core.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace HezbClips.App.Windows;

/// <summary>Tesseract (LSTM) with Vietnamese + English data. One instance is created lazily and reused; it is not thread-safe, so calls are serialised.</summary>
sealed class TesseractOcrEngine : IOcrEngine
{
    readonly Tesseract.TesseractEngine _engine;
    readonly object _gate = new();

    public TesseractOcrEngine(string tessdataDir) =>
        _engine = new Tesseract.TesseractEngine(tessdataDir, OcrData.Languages, Tesseract.EngineMode.LstmOnly);

    public string Name => "Tesseract";
    public int MaxImageDimension => 8000;

    public Task<IReadOnlyList<OcrLineInfo>> RecognizeAsync(byte[] png, CancellationToken ct = default) => Task.Run(() =>
    {
        lock (_gate)
        {
            using var pix = Tesseract.Pix.LoadFromMemory(png);
            using var page = _engine.Process(pix, Tesseract.PageSegMode.Auto);
            var lines = new List<OcrLineInfo>(); var words = new List<OcrWordInfo>();
            using var it = page.GetIterator();
            it.Begin();
            do
            {
                ct.ThrowIfCancellationRequested();
                var text = it.GetText(Tesseract.PageIteratorLevel.Word)?.Trim();
                if (!string.IsNullOrEmpty(text) && it.TryGetBoundingBox(Tesseract.PageIteratorLevel.Word, out var r))
                    words.Add(new OcrWordInfo(text, new OcrBox(r.X1, r.Y1, r.Width, r.Height), it.GetConfidence(Tesseract.PageIteratorLevel.Word)));
                if (it.IsAtFinalOf(Tesseract.PageIteratorLevel.TextLine, Tesseract.PageIteratorLevel.Word) && words.Count > 0)
                { lines.Add(new OcrLineInfo(words)); words = new List<OcrWordInfo>(); }
            } while (it.Next(Tesseract.PageIteratorLevel.Word));
            if (words.Count > 0) lines.Add(new OcrLineInfo(words));
            return (IReadOnlyList<OcrLineInfo>)lines;
        }
    }, ct);

    public void Dispose() { lock (_gate) _engine.Dispose(); }
}

/// <summary>Built-in Windows OCR: fallback when the Tesseract data are missing or Tesseract cannot start.</summary>
sealed class WindowsOcrEngine : IOcrEngine
{
    readonly OcrEngine _engine;
    WindowsOcrEngine(OcrEngine e) => _engine = e;

    public string Name => "Windows OCR";
    public int MaxImageDimension => (int)OcrEngine.MaxImageDimension;

    /// <summary>Best engine: Vietnamese when installed, else the user's languages, else English; null when Windows has none.</summary>
    public static WindowsOcrEngine? TryCreate()
    {
        var vi = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith("vi", StringComparison.OrdinalIgnoreCase));
        var e = (vi is not null ? OcrEngine.TryCreateFromLanguage(vi) : null)
            ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        return e is null ? null : new WindowsOcrEngine(e);
    }

    public async Task<IReadOnlyList<OcrLineInfo>> RecognizeAsync(byte[] png, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(png);
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(soft);
        return result.Lines.Select(l => new OcrLineInfo(l.Words.Select(w =>
            new OcrWordInfo(w.Text, new OcrBox(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))).ToList())).ToList();
    }

    public void Dispose() { }
}

/// <summary>Shared OCR engines for all editor windows: created once, disposed when the app exits.</summary>
static class OcrEngines
{
    static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    static readonly SemaphoreSlim Gate = new(1, 1);
    static TesseractOcrEngine? _tess;
    static WindowsOcrEngine? _win;

    /// <summary>Tesseract when its data are present (downloading them on first use, reporting 0..1 progress); otherwise Windows OCR.
    /// <paramref name="notice"/> gets a message when it had to fall back. Returns null when nothing works.</summary>
    public static async Task<IOcrEngine?> GetAsync(IProgress<double>? progress, Action<string> notice)
    {
        await Gate.WaitAsync();
        try
        {
            if (_tess is not null) return _tess;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = OcrData.FindDir(OcrData.Candidates(AppContext.BaseDirectory, local));
            string? why = null;
            if (dir is null)
            {
                try { dir = OcrData.UserDir(local); await OcrData.EnsureAsync(Http, dir, progress); }
                catch (Exception ex) { dir = null; why = ex is System.Net.Http.HttpRequestException or TaskCanceledException
                    ? "Không tải được dữ liệu OCR tiếng Việt (không có mạng?)" : "Dữ liệu OCR tiếng Việt lỗi: " + ex.Message; }
            }
            if (dir is not null)
            {
                try { return _tess = await Task.Run(() => new TesseractOcrEngine(dir)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { why = "Không khởi động được Tesseract: " + ex.Message; }
            }
            _win ??= WindowsOcrEngine.TryCreate();
            if (_win is not null) notice(why + ". Dùng OCR của Windows, chữ có dấu có thể bị sai.");
            return _win;
        }
        finally { Gate.Release(); }
    }

    public static void Dispose() { _tess?.Dispose(); _tess = null; _win?.Dispose(); _win = null; }
}
