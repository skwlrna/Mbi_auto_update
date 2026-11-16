using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DungeonVisionBot;

internal sealed class OcrRecognizer
{
    private readonly OcrEngine _engine;
    // WinRT OCR engines do not accept overlapping RecognizeAsync calls.
    // Keep processing outside the gate; serialize only the engine invocation.
    private readonly SemaphoreSlim _engineGate = new(1, 1);

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
        // Only the Abyss clear prompt receives the expensive final fallback,
        // after normal compact OCR at 1x, 2x, 3x, and 4x has failed.
        return wanted == "화면을 터치해 주세요"
            ? await FindExactCompactAtScaleAsync(frame, roi, wanted, 2.8, true, ct)
            : DetectionResult.NotFound;
    }

    internal async Task<IReadOnlyList<DetectionResult>> FindAlteringFacilityTitlesAsync(Bitmap frame, string title, CancellationToken ct)
    {
        // Keep surrounding context for Korean OCR, but accept only the large title
        // inside the selected card. Repeated level-badge text is never clickable.
        var area = FishingAutomation.AlteringFacilityLayout.TitleArea(title);
        return await FindAlteringLabelsAsync(frame, new(20, 140, 760, 660), title, ct, acceptedBounds: area);
    }

    internal async Task<DetectionResult?> FindAlteringFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
    {
        // Facility views use a fixed large title at the top-left. Keep this ROI tight so
        // currency text and the help icon cannot make the exact line ambiguous.
        // Live V3.1.58 capture: the fixed title starts at x=20, not x=42.
        // Include the entire left edge; OCR is for FIRST admission only.
        var roi = new Rectangle(12, 24, 250, 80);
        var exact = await FindAlteringLabelsAsync(frame, roi, title, ct);
        if (exact.Count == 1) return exact[0];

        // Windows OCR occasionally misreads one Hangul syllable on the dark facility
        // header. A one-edit fallback is safe here because the ROI contains only the
        // already-selected facility title and no paid action is taken from this result.
        var fuzzy = await FindTextAsync(frame, roi, title, 1, retry2x: true, ct: ct);
        return fuzzy.Found ? fuzzy : null;
    }

    // Exact recipe matching must retain '+' and ingredient suffixes.
    internal async Task<IReadOnlyList<DetectionResult>> FindAlteringLabelsAsync(Bitmap frame, Rectangle roi, string wanted, CancellationToken ct, bool cardCandidate = false, Rectangle? acceptedBounds = null, bool dimText = false)
    {
        roi = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), roi);
        var found = new List<DetectionResult>();
        var modes = new[] { (Scale: 2, Threshold: 70), (Scale: 3, Threshold: 70), (Scale: 3, Threshold: 80), (Scale: 3, Threshold: 90), (Scale: 3, Threshold: 100), (Scale: 3, Threshold: 0) };
        if(dimText) modes = modes.Concat(new[]{(Scale:3, Threshold:30),(Scale:3, Threshold:40),(Scale:3, Threshold:50)}).ToArray();
        foreach (var mode in modes)
        {
            int scale = mode.Scale;
            ct.ThrowIfCancellationRequested();
            using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
            using var prepared = PrepareAlteringText(crop, scale, mode.Threshold);
            using var software = await ToSoftwareBitmapAsync(prepared);
            var result = await RecognizeEngineSafeAsync(software, ct);
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
                if (acceptedBounds is Rectangle allowed && !allowed.Contains(bounds)) continue;
                if (!found.Any(x => x.Bounds.IntersectsWith(bounds))) found.Add(new(true, bounds, 1, text));
            }
            // A facility has one known title region; stop on its unique exact title
            // so input does not rely on a frame aged by unnecessary OCR retries.
            if (acceptedBounds.HasValue && found.Count == 1) return found.ToArray();
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

    private async Task<DetectionResult> FindExactCompactAtScaleAsync(
        Bitmap frame, Rectangle roi, string wanted, double scale, CancellationToken ct)
        => await FindExactCompactAtScaleAsync(frame, roi, wanted, scale, false, ct);

    private async Task<DetectionResult> FindExactCompactAtScaleAsync(
        Bitmap frame, Rectangle roi, string wanted, double scale, bool lanczos, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
        using var prepared = lanczos
            ? ResizeLanczos3(crop, (int)Math.Round(crop.Width * scale), (int)Math.Round(crop.Height * scale))
            : scale == 1 ? (Bitmap)crop.Clone()
            : ResizeNearest(crop, (int)(crop.Width * scale), (int)(crop.Height * scale));
        using var software = await ToSoftwareBitmapAsync(prepared);
        var ocr = await RecognizeEngineSafeAsync(software, ct);
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

    // Production quest material names are rendered as small dim-gray text.
    // Normal ReadLinesAsync can still see the bright x/y counters while dropping
    // the name on the same row. Re-run only the narrow name ROI through the same
    // thresholded pipeline used by dim production labels.
    internal async Task<IReadOnlyList<DetectionResult>> ReadDimLinesAsync(
        Bitmap frame,
        Rectangle roi,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        roi = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), roi);
        if (roi.Width <= 0 || roi.Height <= 0)
            return Array.Empty<DetectionResult>();

        var found = new List<DetectionResult>();
        foreach (int threshold in new[] { 30, 40, 50, 70, 0 })
        {
            ct.ThrowIfCancellationRequested();
            using var crop = frame.Clone(roi, PixelFormat.Format24bppRgb);
            using var prepared = PrepareAlteringText(crop, 3, threshold);
            using var software = await ToSoftwareBitmapAsync(prepared);
            var result = await RecognizeEngineSafeAsync(software, ct);
            ct.ThrowIfCancellationRequested();

            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0)
                    continue;

                string text = string.Join(" ", line.Words.Select(w => w.Text)).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                double left = line.Words.Min(w => w.BoundingRect.X);
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                var bounds = Rectangle.FromLTRB(
                    roi.X + (int)Math.Round(left / 3.0),
                    roi.Y + (int)Math.Round(top / 3.0),
                    roi.X + (int)Math.Round(right / 3.0),
                    roi.Y + (int)Math.Round(bottom / 3.0));

                found.Add(new DetectionResult(true, bounds, 1.0, text));
            }
        }

        return found
            .OrderBy(x => x.Bounds.Top)
            .ThenBy(x => x.Bounds.Left)
            .ToArray();
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
        var ocr = await RecognizeEngineSafeAsync(software, ct);
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
        var ocr = await RecognizeEngineSafeAsync(software, ct);
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

    private async Task<OcrResult> RecognizeEngineSafeAsync(SoftwareBitmap bitmap, CancellationToken ct)
    {
        await _engineGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await _engine.RecognizeAsync(bitmap);
        }
        finally
        {
            _engineGate.Release();
        }
    }

    // Separable Lanczos-3 resampling. The only caller is the failed-Abyss-
    // touch-OCR fallback, so normal fast OCR paths are unaffected.
    private static Bitmap ResizeLanczos3(Bitmap src, int w, int h)
    {
        static double Sinc(double x) =>
            Math.Abs(x) < 1e-8 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
        static (int Index, double Weight)[][] Weights(int input, int output)
        {
            var all = new (int, double)[output][];
            for (int i = 0; i < output; i++)
            {
                double center = (i + 0.5) * input / output - 0.5;
                var taps = new List<(int Index, double Weight)>();
                double sum = 0;
                for (int j = (int)Math.Floor(center) - 2; j <= (int)Math.Ceiling(center) + 2; j++)
                {
                    double distance = center - j;
                    if (j < 0 || j >= input || Math.Abs(distance) >= 3) continue;
                    double weight = Sinc(distance) * Sinc(distance / 3);
                    taps.Add((j, weight)); sum += weight;
                }
                all[i] = taps.Select(t => (t.Index, t.Weight / sum)).ToArray();
            }
            return all;
        }
        var sourceData = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        byte[] sourceBytes; int sourceStride;
        try
        {
            sourceStride = Math.Abs(sourceData.Stride);
            sourceBytes = new byte[sourceStride * src.Height];
            System.Runtime.InteropServices.Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);
        }
        finally { src.UnlockBits(sourceData); }
        var xWeights = Weights(src.Width, w);
        var yWeights = Weights(src.Height, h);
        var intermediate = new double[src.Height * w * 3];
        for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < w; x++)
        for (int c = 0; c < 3; c++)
        {
            double value = 0;
            foreach (var tap in xWeights[x])
                value += sourceBytes[y * sourceStride + tap.Index * 3 + c] * tap.Weight;
            intermediate[(y * w + x) * 3 + c] = value;
        }
        var result = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var outputData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = Math.Abs(outputData.Stride);
            var pixels = new byte[stride * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            for (int c = 0; c < 3; c++)
            {
                double value = 0;
                foreach (var tap in yWeights[y])
                    value += intermediate[(tap.Index * w + x) * 3 + c] * tap.Weight;
                pixels[y * stride + x * 3 + c] =
                    (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, outputData.Scan0, pixels.Length);
        }
        finally { result.UnlockBits(outputData); }
        return result;
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
