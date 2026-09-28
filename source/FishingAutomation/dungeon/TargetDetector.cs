namespace DungeonVisionBot;

internal sealed class TargetDetector
{
    private readonly Dictionary<string, TargetDefinition> _targets;
    private readonly OcrRecognizer _ocr;
    private readonly TemplateMatcher _template;

    public TargetDetector(IEnumerable<TargetDefinition> targets, string baseDir)
    {
        _targets = targets.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var t in _targets.Values)
        {
            if (string.IsNullOrWhiteSpace(t.TemplatePath)) continue;

            string resolved = Path.IsPathRooted(t.TemplatePath)
                ? t.TemplatePath
                : Path.Combine(baseDir, t.TemplatePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    $"던전 템플릿 파일을 찾을 수 없습니다. target={t.Id}, path={resolved}",
                    resolved);
        }

        _ocr = new OcrRecognizer();
        _template = new TemplateMatcher(baseDir);
    }

    public TargetDefinition Get(string id) => _targets.TryGetValue(id, out var t)
        ? t : throw new KeyNotFoundException($"targets.json에 '{id}' 타깃이 없습니다.");

    public async Task<DetectionResult> DetectAsync(string id, Bitmap frame, CancellationToken ct)
    {
        var t = Get(id);
        var roi = WindowCapture.ClampRoi(t.Roi.ToRectangle(), frame.Size);
        if (roi.Width < 2 || roi.Height < 2) return DetectionResult.NotFound;

        if (t.Kind.Equals("ocr", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) return DetectionResult.NotFound;

            bool exactDungeonSlot =
                id.Equals("route_d1_1", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("route_d2_1", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("route_regular_1_1", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("route_regular_2_1", StringComparison.OrdinalIgnoreCase);

            bool exactClickableEntry =
                id.Equals("enter_bottom", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("enter_confirm", StringComparison.OrdinalIgnoreCase);

            bool exactDungeonEntryState =
                id.Equals("selected_ocr_strict", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("challenge_confirm_strict", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("retry_ocr_strict", StringComparison.OrdinalIgnoreCase);

            if (exactDungeonSlot || exactClickableEntry || exactDungeonEntryState)
                return await _ocr.FindCompactLabelAsync(frame, roi, t.Text, ct);

            return await _ocr.FindTextAsync(frame, roi, t.Text, t.MaxEditDistance, t.OcrRetryAt2x, ct);
        }

        if (t.Kind.Equals("template", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(t.TemplatePath)) return DetectionResult.NotFound;
            return _template.FindMultiScale(
                frame, roi, t.TemplatePath, t.Threshold,
                t.TemplateScaleMin, t.TemplateScaleMax, t.TemplateScaleStep);
        }

        if (t.Kind.Equals("slot-hybrid", StringComparison.OrdinalIgnoreCase))
        {
            DetectionResult visual = DetectionResult.NotFound;
            if (!string.IsNullOrWhiteSpace(t.TemplatePath))
            {
                visual = _template.FindBrightGlyphMultiScale(
                    frame, roi, t.TemplatePath, t.Threshold,
                    t.TemplateScaleMin, t.TemplateScaleMax, t.TemplateScaleStep);
            }

            // An exact OCR hit is always accepted. When the binary visual score is
            // already strong (>= threshold), OCR is only a confirmation attempt:
            // failure to OCR tiny pixel text does not discard the strong visual hit.
            if (!string.IsNullOrWhiteSpace(t.Text))
            {
                Rectangle ocrRoi = roi;
                if (visual.Bounds.Width > 0 && visual.Bounds.Height > 0)
                {
                    var expanded = Rectangle.Inflate(visual.Bounds, 26, 22);
                    ocrRoi = WindowCapture.ClampRoi(expanded, frame.Size);
                }

                var exactOcr = await _ocr.FindCompactLabelAsync(frame, ocrRoi, t.Text, ct);
                if (exactOcr.Found)
                {
                    if (visual.Found)
                        return new DetectionResult(true, visual.Bounds, visual.Score, exactOcr.ReadText);
                    return exactOcr;
                }
            }

            if (visual.Found)
                return visual;

            return visual;
        }

        if (t.Kind.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(t.TemplatePath))
            {
                var visual = _template.FindMultiScale(
                    frame, roi, t.TemplatePath, t.Threshold,
                    t.TemplateScaleMin, t.TemplateScaleMax, t.TemplateScaleStep);
                if (visual.Found) return visual;
            }

            if (!string.IsNullOrWhiteSpace(t.Text))
            {
                return await _ocr.FindTextAsync(
                    frame, roi, t.Text, t.MaxEditDistance, t.OcrRetryAt2x, ct);
            }

            return DetectionResult.NotFound;
        }

        throw new NotSupportedException($"알 수 없는 타깃 종류: {t.Kind}");
    }
}
