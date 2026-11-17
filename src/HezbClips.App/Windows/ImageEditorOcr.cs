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
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WpfBitmapDecoder = System.Windows.Media.Imaging.BitmapDecoder;

namespace HezbClips.App.Windows;

/// <summary>Text recognition with the built-in Windows OCR: copy text out of the image, or auto-mask sensitive data.</summary>
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

    /// <summary>Run OCR on the current picture. Returns null (and tells the user) when it cannot.</summary>
    async Task<(OcrResult Result, double Scale)?> RunOcrAsync()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (engine is null) { _status.Text = "Windows chưa có gói nhận dạng chữ. Thêm ngôn ngữ trong Cài đặt Windows > Thời gian & ngôn ngữ."; return null; }

            BitmapSource src = _bmp;
            var scale = 1.0;
            var max = OcrEngine.MaxImageDimension;
            if (src.PixelWidth > max || src.PixelHeight > max)
            {
                scale = (double)max / Math.Max(src.PixelWidth, src.PixelHeight);
                var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale)); tb.Freeze(); src = tb;
            }
            var enc = new PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
            using var ms = new MemoryStream(); enc.Save(ms); ms.Position = 0;
            var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(soft);
            return (result, scale);
        }
        catch (Exception ex) { _status.Text = "Nhận dạng chữ lỗi: " + ex.Message; return null; }
    }

    async Task CopyTextAsync()
    {
        _status.Text = "Đang nhận dạng chữ…";
        if (await RunOcrAsync() is not { } r) return;
        var text = string.Join(Environment.NewLine, r.Result.Lines.Select(l => l.Text));
        if (string.IsNullOrWhiteSpace(text)) { _status.Text = "Không tìm thấy chữ trong ảnh."; return; }
        try { Clipboard.SetText(text); _status.Text = $"✓ Đã copy {text.Length} ký tự ({r.Result.Lines.Count} dòng)"; }
        catch (Exception) { _status.Text = "Clipboard đang bận, thử lại."; }
    }

    async Task RedactAsync()
    {
        _status.Text = "Đang tìm thông tin nhạy cảm…";
        if (await RunOcrAsync() is not { } r) return;
        var rects = new List<Rect>();
        foreach (var line in r.Result.Lines)
        {
            // line text with a record of where each word starts, so a regex hit maps back to word boxes
            var sb = new StringBuilder(); var spans = new List<(int Start, int End, OcrWord Word)>();
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
                var b = spans[i].Word.BoundingRect;
                rects.Add(new Rect(b.X / r.Scale, b.Y / r.Scale, b.Width / r.Scale, b.Height / r.Scale));
            }
        }
        if (rects.Count == 0) { _status.Text = "Không thấy email, số điện thoại hay số thẻ trong ảnh."; return; }
        foreach (var rc in rects)
        {
            var pad = 3;
            var box = new Rect(Math.Max(0, rc.X - pad), Math.Max(0, rc.Y - pad), rc.Width + pad * 2, rc.Height + pad * 2);
            var m = new Rectangle { Width = box.Width, Height = box.Height, Stroke = Brushes.Transparent, Fill = MosaicBrush(box) };
            RenderOptions.SetBitmapScalingMode(m, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(m, box.X); Canvas.SetTop(m, box.Y);
            _canvas.Children.Add(m); Commit(m);
        }
        _status.Text = $"✓ Đã che {rects.Count} vùng nhạy cảm. Ctrl+Z để hoàn tác từng vùng.";
    }
}
