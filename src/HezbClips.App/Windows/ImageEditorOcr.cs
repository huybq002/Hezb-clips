using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HezbClips.Core.Imaging;

namespace HezbClips.App.Windows;

/// <summary>Text recognition with Tesseract (Vietnamese + English), falling back to the built-in Windows OCR: copy text out of the image, or auto-mask sensitive data.</summary>
public sealed partial class ImageEditorWindow
{
    // email, VN/intl phone, card-like groups, long digit runs (account / ID numbers)
    static readonly Regex[] Sensitive =
    {
        new(@"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+", RegexOptions.Compiled),
        new(@"(?<!\d)(\+?84|0)[\s.\-]?\d{2,3}[\s.\-]?\d{3}[\s.\-]?\d{3,4}(?!\d)", RegexOptions.Compiled),
        new(@"(?<!\d)\d{4}[\s\-]\d{4}[\s\-]\d{4}([\s\-]\d{1,7})?(?!\d)", RegexOptions.Compiled),
        new(@"(?<!\d)\d{9,}(?!\d)", RegexOptions.Compiled),
    };

    /// <summary>Run OCR on the current picture, or only on <paramref name="region"/> (picture pixels). Result positions map back with
    /// picture = result / Scale + Offset. Returns null (and tells the user) when it cannot.</summary>
    async Task<(IReadOnlyList<OcrLineInfo> Lines, double Scale, Point Offset)?> RunOcrAsync(Rect? region = null)
    {
        try
        {
            var progress = new Progress<double>(v => { if (v - _ocrShown >= 0.1 || v >= 1) { _ocrShown = v; Hud("", $"Đang tải dữ liệu OCR tiếng Việt… {(int)(v * 100)}%"); } });
            _ocrShown = -1;
            var engine = await OcrEngines.GetAsync(progress, msg => Hud("", msg, ok: false));
            if (engine is null) { Hud("", "Windows chưa có gói nhận dạng chữ. Thêm ngôn ngữ trong Cài đặt Windows > Thời gian & ngôn ngữ.", ok: false); return null; }

            BitmapSource src = _bmp;
            var offset = new Point(0, 0);
            if (region is { } rg)
            {
                var ir = new Int32Rect((int)Math.Floor(rg.X), (int)Math.Floor(rg.Y), (int)Math.Ceiling(rg.Width), (int)Math.Ceiling(rg.Height));
                ir.Width = Math.Min(ir.Width, _bmp.PixelWidth - ir.X); ir.Height = Math.Min(ir.Height, _bmp.PixelHeight - ir.Y);
                var cb = new CroppedBitmap(src, ir); cb.Freeze(); src = cb; offset = new Point(ir.X, ir.Y);
            }
            // small text reads far better when enlarged first, big text does not: engines that report confidence try 1x, 2x, 3x
            // only while unsure; the others use one fixed scale
            IReadOnlyList<OcrLineInfo>? best = null; var bestScale = 1.0; var bestConf = -1.0;
            var plan = OcrScaling.Candidates(src.PixelWidth, src.PixelHeight, engine.MaxImageDimension);
            var fixedScale = OcrScaling.Fixed(src.PixelWidth, src.PixelHeight, engine.MaxImageDimension);
            foreach (var k in plan)
            {
                var scale = k;
                var lines = await engine.RecognizeAsync(EncodePng(src, scale));
                var conf = OcrScaling.MeanConfidence(lines);
                if (conf is null) // no confidence from this engine: one fixed scale
                {
                    if (Math.Abs(fixedScale - scale) > 0.001) lines = await engine.RecognizeAsync(EncodePng(src, scale = fixedScale));
                    return (lines, scale, offset);
                }
                if (conf > bestConf) { best = lines; bestScale = scale; bestConf = conf.Value; }
                if (conf >= OcrScaling.GoodEnough) break;
            }
            return (best ?? Array.Empty<OcrLineInfo>(), bestScale, offset);
        }
        catch (Exception ex) { Hud("", "Nhận dạng chữ lỗi: " + ex.Message, ok: false); return null; }
    }

    double _ocrShown;

    /// <summary>PNG bytes of the picture enlarged by <paramref name="scale"/> (1 = as is).</summary>
    static byte[] EncodePng(BitmapSource src, double scale)
    {
        if (Math.Abs(scale - 1) > 0.001)
        {
            var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale)); tb.Freeze(); src = tb;
        }
        var enc = new PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var ms = new MemoryStream(); enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Copy the text of <paramref name="region"/> (picture pixels; null = whole picture) to the clipboard.</summary>
    async Task CopyTextAsync(Button? b, Rect? region)
    {
        Hud("", region is null ? "Đang nhận dạng chữ cả ảnh…" : "Đang nhận dạng chữ trong vùng đã chọn…");
        if (await RunOcrAsync(region) is not { } r) return;
        // only letters/digits and normal punctuation: border marks, icons and stray symbols are dropped
        var text = OcrTextCleaner.Clean(r.Lines.Select(l => l.Words.Select(w => w.Text)), Environment.NewLine);
        if (string.IsNullOrWhiteSpace(text)) { Hud("", region is null ? "Không tìm thấy chữ trong ảnh" : "Không tìm thấy chữ trong vùng đã chọn", ok: false); return; }
        var lines = text.Split('\n').Length;
        var where = region is null ? "cả ảnh" : "vùng chọn";
        try { Clipboard.SetText(text); Done(b, "Đã copy chữ", $"Đã copy {text.Length} ký tự ({lines} dòng, {where}) vào clipboard"); }
        catch (Exception) { Hud("", "Clipboard đang bận, thử lại", ok: false); }
    }

    async Task RedactAsync(Button? b)
    {
        Hud("", "Đang tìm thông tin nhạy cảm…");
        if (await RunOcrAsync() is not { } r) return;
        var rects = new List<Rect>();
        foreach (var line in r.Lines)
        {
            // line text with a record of where each word starts, so a regex hit maps back to word boxes
            var sb = new StringBuilder(); var spans = new List<(int Start, int End, OcrWordInfo Word)>();
            foreach (var w in line.Words)
            {
                if (sb.Length > 0) sb.Append(' ');
                spans.Add((sb.Length, sb.Length + w.Text.Length, w)); sb.Append(w.Text);
            }
            var text = sb.ToString();
            var hit = new HashSet<int>();
            foreach (var re in Sensitive)
                foreach (Match m in re.Matches(text))
                    for (int i = 0; i < spans.Count; i++)
                        if (spans[i].Start < m.Index + m.Length && spans[i].End > m.Index) hit.Add(i);
            foreach (var i in hit)
            {
                var bb = spans[i].Word.Box;
                rects.Add(new Rect(bb.X / r.Scale + r.Offset.X, bb.Y / r.Scale + r.Offset.Y, bb.Width / r.Scale, bb.Height / r.Scale));
            }
        }
        if (rects.Count == 0) { Hud("", "Không thấy email, số điện thoại hay số thẻ trong ảnh", ok: false); return; }

        // all masks of one pass undo together
        var cmds = new List<IEditCmd>();
        foreach (var rc in rects)
        {
            const int pad = 3;
            var box = new Rect(Math.Max(0, rc.X - pad), Math.Max(0, rc.Y - pad), rc.Width + pad * 2, rc.Height + pad * 2);
            var m = new Rectangle { Width = box.Width, Height = box.Height, Stroke = Brushes.Transparent, Fill = MosaicBrush(box), Tag = "mosaic" };
            RenderOptions.SetBitmapScalingMode(m, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(m, box.X); Canvas.SetTop(m, box.Y);
            cmds.Add(new AddCmd(this, m, "làm mờ"));
        }
        Run(new GroupCmd("che thông tin nhạy cảm", cmds));
        Done(b, $"Đã che {rects.Count}", $"Đã che {rects.Count} vùng nhạy cảm (Ctrl+Z hoàn tác tất cả)");
    }
}
