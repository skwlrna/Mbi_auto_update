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

    // Exact recipe matching must retain '+' and ingredient suffixes.
    internal async Task<IReadOnlyList<DetectionResult>> FindAlteringLabelsAsync(Bitmap frame, Rectangle roi, string wanted, CancellationToken ct, bool cardCandidate = false)
    {
        roi = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), roi);
        var found = new List<DetectionResult>();
        foreach (var mode in new[] { (Scale: 2, Threshold: 70), (Scale: 3, Threshold: 70), (Scale: 3, Threshold: 80), (Scale: 3, Threshold: 90), (Scale: 3, Threshold: 100), (Scale: 3, Threshold: 0) })
        {
            int scale = mode.Scale;
            ct.ThrowIfCancellationRequested();
            using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
            using var prepared = PrepareAlteringText(crop, scale, mode.Threshold);
            using var software = await ToSoftwareBitmapAsync(prepared);
            var result = await _engine.RecognizeAsync(software);
            ct.ThrowIfCancellationRequested();
            string normalized = FishingAutomation.AlteringText.Normalize(wanted);
            foreach (var line in result.Lines)
            for (int start = 0; start < line.Words.Count; start++)
            for (int count = 1; count <= 12 && start + count <= line.Words.Count; count++)
            {
                var words = line.Words.Skip(start).Take(count).ToArray();
                string text = string.Concat(words.Select(w => w.Text));
                if (cardCandidate ? !FishingAutomation.AlteringText.IsCardCandidate(text, wanted) :
                    !FishingAutomation.AlteringText.Normalize(text).Equals(normalized, StringComparison.OrdinalIgnoreCase)) continue;
                if (wanted != "5" && start > 0 && line.Words[start - 1].Text.Any(char.IsLetter) &&
                    words[0].BoundingRect.X - line.Words[start - 1].BoundingRect.Right < 20 * scale)
                    continue;
                if (wanted != "5" && start + count < line.Words.Count && line.Words[start + count].Text.Any(char.IsLetter) &&
                    line.Words[start + count].BoundingRect.X - words[^1].BoundingRect.Right < 20 * scale)
                    continue;
                // A trailing '+' can be a separate OCR word: do not match the base item.
                if (start + count < line.Words.Count && line.Words[start + count].Text.Trim() == "+" &&
                    line.Words[start + count].BoundingRect.X - words[^1].BoundingRect.Right < 30 * scale)
                    continue;
                var bounds = Rectangle.FromLTRB(roi.X + (int)(words.Min(w => w.BoundingRect.X) / scale),
                    roi.Y + (int)(words.Min(w => w.BoundingRect.Y) / scale),
                    roi.X + (int)(words.Max(w => w.BoundingRect.Right) / scale),
                    roi.Y + (int)(words.Max(w => w.BoundingRect.Bottom) / scale));
                if (!found.Any(x => x.Bounds.IntersectsWith(bounds))) found.Add(new(true, bounds, 1, text));
            }
        }
        return found.OrderBy(x => x.Bounds.Top).ThenBy(x => x.Bounds.Left).ToArray();
    }

    private static Bitmap PrepareAlteringText(Bitmap crop, int scale, int threshold)
    {
        var result = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, new Rectangle(0, 0, result.Width, result.Height));
        }
        if (threshold == 0) return result;
        var rect = new Rectangle(0, 0, result.Width, result.Height);
        var data = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int length = Math.Abs(data.Stride) * result.Height;
            var pixels = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, length);
            for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                int index = y * data.Stride + x * 3;
                int brightness = (pixels[index] + pixels[index + 1] + pixels[index + 2]) / 3;
                byte value = brightness >= threshold ? (byte)0 : (byte)255;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = value;
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, length);
        }
        finally { result.UnlockBits(data); }
        return result;
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
