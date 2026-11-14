using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // V3.3.24: share SAME-FRAME image + bounded OCR evidence. In particular,
    // a second image-only HUD check must not discard an OCR-confirmed overlay.
    // This method sends no input and does not advance the two-frame policy.
    private async Task<(DetectionResult Title, DetectionResult Touch)> ReadAbyssClearEvidenceAsync(
        Bitmap frame, CancellationToken ct)
    {
        var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
        DiagnosticObserveDetection("abyss_dungeon_clear_visual", clearTitle);
        var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
        DiagnosticObserveDetection("abyss_touch_screen", touch);

        // V3.3.22: an animating clear screen may temporarily lose the title
        // or prompt template (the 22:53 screenshot shows both words).
        // OCR is bounded to the actual title/prompt regions, and NEVER
        // authorizes a click without the same two-frame pair policy.
        var clearTitleArea = ScaleAbyssResultRoi(AbyssClearTitleRecoverySafeRoi, frame.Size);
        var clearTouchArea = ScaleAbyssResultRoi(AbyssClearTouchRecoverySafeRoi, frame.Size);
        if (!clearTitle.Found || !clearTitleArea.Contains(clearTitle.Bounds) ||
            !touch.Found || !clearTouchArea.Contains(touch.Bounds))
        {
            _abyssResultOcr ??= new OcrRecognizer();
            if (!clearTitle.Found || !clearTitleArea.Contains(clearTitle.Bounds))
            {
                var ocrTitle = await _abyssResultOcr.FindCompactLabelAsync(
                    frame, clearTitleArea, "던전 클리어", ct);
                if (ocrTitle.Found && clearTitleArea.Contains(ocrTitle.Bounds))
                    clearTitle = ocrTitle;
            }
            if (!touch.Found || !clearTouchArea.Contains(touch.Bounds))
            {
                var ocrTouch = await _abyssResultOcr.FindCompactLabelAsync(
                    frame, clearTouchArea, "화면을 터치해 주세요", ct);
                if (ocrTouch.Found && clearTouchArea.Contains(ocrTouch.Bounds))
                    touch = ocrTouch;
            }
        }

        if (!clearTitleArea.Contains(clearTitle.Bounds)) clearTitle = DetectionResult.NotFound;
        if (!clearTouchArea.Contains(touch.Bounds)) touch = DetectionResult.NotFound;
        DiagnosticObserveDetection("abyss_clear_title_resolved", clearTitle);
        DiagnosticObserveDetection("abyss_clear_touch_resolved", touch);
        return (clearTitle, touch);
    }
}
