using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DungeonVisionBot;

internal sealed class OcrRecognizer
{
    private readonly OcrEngine _engine;

    public OcrRecognizer()
    {
        _engine = OcrEngine.TryCreateFromLanguage(new Language("ko-KR"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? throw new InvalidOperationException("Windows 한국어 OCR 엔진을 만들 수 없습니다. Windows 설정에서 한국어 언어/OCR 기능을 설치해 주세요.");
    }

    public async Task<DetectionResult> FindTextAsync(Bitmap frame, Rectangle roi, string wanted, int maxEditDistance, bool retry2x, CancellationToken ct)
    {
        var result = await FindAtScaleAsync(frame, roi, wanted, maxEditDistance, 1, ct);
        if (result.Found || !retry2x) return result;
        return await FindAtScaleAsync(frame, roi, wanted, maxEditDistance, 2, ct);
    }

    public async Task<DetectionResult> FindCompactLabelAsync(Bitmap frame, Rectangle roi, string wanted, CancellationToken ct)
    {
        foreach (int scale in new[] { 1, 2, 3, 4 })
        {
            var result = await FindExactCompactAtScaleAsync(frame, roi, wanted, scale, ct);
            if (result.Found) return result;
        }
        return DetectionResult.NotFound;
    }

    private async Task<DetectionResult> FindExactCompactAtScaleAsync(Bitmap frame, Rectangle roi, string wanted, int scale, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
        using var prepared = scale == 1 ? (Bitmap)crop.Clone() : ResizeNearest(crop, crop.Width * scale, crop.Height * scale);
        using var software = await ToSoftwareBitmapAsync(prepared);
        var ocr = await _engine.RecognizeAsync(software);
        ct.ThrowIfCancellationRequested();

        string wantedNorm = FuzzyText.Normalize(wanted);
        foreach (var line in ocr.Lines)
        {
            var words = line.Words;
            for (int start = 0; start < words.Count; start++)
            {
                for (int count = 1; count <= 3 && start + count <= words.Count; count++)
                {
                    var selected = words.Skip(start).Take(count).ToArray();
                    string candidate = string.Concat(selected.Select(w => w.Text));
                    if (!FuzzyText.Normalize(candidate).Equals(wantedNorm, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double left = selected.Min(w => w.BoundingRect.X);
                    double top = selected.Min(w => w.BoundingRect.Y);
                    double right = selected.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                    double bottom = selected.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                    var bounds = Rectangle.FromLTRB(
                        roi.X + (int)Math.Round(left / scale),
                        roi.Y + (int)Math.Round(top / scale),
                        roi.X + (int)Math.Round(right / scale),
                        roi.Y + (int)Math.Round(bottom / scale));
                    return new DetectionResult(true, bounds, 1.0, candidate);
                }
            }
        }
        return DetectionResult.NotFound;
    }

    // V0168_ABYSS_LOOT_OCR_LINES
    public async Task<IReadOnlyList<DetectionResult>> ReadLinesAsync(
        Bitmap frame,
        Rectangle roi,
        int scale,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        roi = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), roi);
        if (roi.Width <= 0 || roi.Height <= 0)
            return Array.Empty<DetectionResult>();

        using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
        using var prepared = scale == 1
            ? (Bitmap)crop.Clone()
            : ResizeNearest(crop, crop.Width * scale, crop.Height * scale);
        using var software = await ToSoftwareBitmapAsync(prepared);
        var ocr = await _engine.RecognizeAsync(software);
        ct.ThrowIfCancellationRequested();

        var lines = new List<DetectionResult>();
        foreach (var line in ocr.Lines)
        {
            if (line.Words.Count == 0)
                continue;

            string lineText = string.Join(" ", line.Words.Select(w => w.Text));
            double left = line.Words.Min(w => w.BoundingRect.X);
            double top = line.Words.Min(w => w.BoundingRect.Y);
            double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

            var bounds = Rectangle.FromLTRB(
                roi.X + (int)Math.Round(left / scale),
                roi.Y + (int)Math.Round(top / scale),
                roi.X + (int)Math.Round(right / scale),
                roi.Y + (int)Math.Round(bottom / scale));

            lines.Add(new DetectionResult(true, bounds, 1.0, lineText));
        }

        return lines;
    }

    private async Task<DetectionResult> FindAtScaleAsync(Bitmap frame, Rectangle roi, string wanted, int maxEditDistance, int scale, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
        using var prepared = scale == 1 ? (Bitmap)crop.Clone() : ResizeNearest(crop, crop.Width * scale, crop.Height * scale);
        using var software = await ToSoftwareBitmapAsync(prepared);
        var ocr = await _engine.RecognizeAsync(software);
        ct.ThrowIfCancellationRequested();

        foreach (var line in ocr.Lines)
        {
            string lineText = string.Join(" ", line.Words.Select(w => w.Text));
            if (!FuzzyText.ContainsApprox(lineText, wanted, maxEditDistance)) continue;
            if (line.Words.Count == 0) continue;

            double left = line.Words.Min(w => w.BoundingRect.X);
            double top = line.Words.Min(w => w.BoundingRect.Y);
            double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

            var bounds = Rectangle.FromLTRB(
                roi.X + (int)Math.Round(left / scale),
                roi.Y + (int)Math.Round(top / scale),
                roi.X + (int)Math.Round(right / scale),
                roi.Y + (int)Math.Round(bottom / scale));
            return new DetectionResult(true, bounds, 1.0, lineText);
        }
        return DetectionResult.NotFound;
    }

    private static Bitmap ResizeNearest(Bitmap src, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        // Do not use System.Runtime.WindowsRuntime / AsStreamForWrite here.
        // .NET 5+ consumes Windows Runtime APIs through C#/WinRT projections.
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        byte[] bytes = png.ToArray();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
