namespace DungeonVisionBot;

// Read-only recognition. Never derives permission to click from an icon or a
// near spelling alone; the caller still requires two fresh stable observations.
internal static class LifeSkillRowRecognition
{
    internal static async Task<DetectionResult?> FindAsync(
        OcrRecognizer ocr, Bitmap frame, Rectangle roi, string target,
        CancellationToken ct, Action<string>? log)
    {
        var matches = (await ocr.FindAlteringLabelsAsync(frame, roi, target, ct,
            acceptedBounds: roi, dimText: true)).ToList();
        // The 2026-11-05 capture reproduces zero matches in thresholded OCR,
        // while native-color 2x reads the exact bottom name. Do not expand ROI.
        if (matches.Count == 0)
        {
            var retriedRows = new List<int>();
            foreach (int scale in new[] { 2, 3 })
            {
                var lines = await ocr.ReadLinesAsync(frame, roi, scale, ct);
                foreach (var line in lines.Where(x => x.Bounds.Left >= 250 &&
                    x.Bounds.Right <= 540 && x.Bounds.Height is >= 8 and <= 40))
                {
                    log?.Invoke($"[대량 채집][OCR 행] scale={scale} text='{line.ReadText}' bounds={line.Bounds}");
                    // Approximation only chooses a crop to read again. It is
                    // never accepted as an exact match or permission to click.
                    if (!FuzzyText.ContainsApprox(line.ReadText ?? "", target, 2)) continue;
                    if (retriedRows.Any(y => Math.Abs(y - line.Center.Y) <= 12)) continue;
                    retriedRows.Add(line.Center.Y);
                    var rowRoi = Rectangle.Intersect(roi,
                        new Rectangle(250, line.Center.Y - 28, 290, 56));
                    // Re-read each observed row, including misrecognized names;
                    // a guessed/fixed bottom Y never becomes a click coordinate.
                    var exact = await ocr.FindAlteringLabelsAsync(frame, rowRoi,
                        target, ct, acceptedBounds: rowRoi, dimText: true);
                    log?.Invoke($"[대량 채집][OCR 재검증] roi={rowRoi} exact={exact.Count}");
                    foreach (var hit in exact)
                        if (!matches.Any(x => x.Bounds.IntersectsWith(hit.Bounds))) matches.Add(hit);
                }
            }
        }
        log?.Invoke($"[대량 채집][OCR 판정] target='{target}' roi={roi} exact={matches.Count} " +
            $"bounds={string.Join(";", matches.Select(x => x.Bounds))} result={(matches.Count == 1 ? "unique" : "reject")}");
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static bool HasIcon(Bitmap frame, Rectangle text, Action<string>? log = null)
    {
        // Keep the sampling inside the icon cell, clear of the panel/card edges
        // and adjacent rows. The live herb icons occupy x195..242.
        var cell = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size),
            new Rectangle(text.Left - 75, text.Top - 14, 54, text.Height + 28));
        if (cell.Width < 40 || cell.Height < 30) return false;
        int min = 255, max = 0, saturated = 0, sampled = 0;
        for (int y = cell.Top; y < cell.Bottom; y += 2)
        for (int x = cell.Left; x < cell.Right; x += 2)
        {
            var p = frame.GetPixel(x, y);
            int bright = (p.R + p.G + p.B) / 3;
            min = Math.Min(min, bright); max = Math.Max(max, bright);
            if (Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) >= 28) saturated++;
            sampled++;
        }
        bool ok = sampled > 0 && max - min >= 32 && saturated * 100 >= sampled * 3;
        log?.Invoke($"[대량 채집][아이콘 판정] text={text} cell={cell} contrast={max-min} colored={saturated}/{sampled} sameRow={ok}");
        return ok;
    }
}
