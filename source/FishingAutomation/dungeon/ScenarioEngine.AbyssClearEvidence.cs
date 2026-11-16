using System.Drawing;
namespace DungeonVisionBot;
internal sealed partial class ScenarioEngine
{
    private async Task<(DetectionResult Title, DetectionResult Touch)> ReadAbyssClearEvidenceAsync(
        Bitmap frame, CancellationToken ct)
    {
        _abyssResultOcr ??= new OcrRecognizer();
        var area = ScaleAbyssResultRoi(AbyssClearTouchRecoverySafeRoi, frame.Size);
        var touch = await _abyssResultOcr.FindCompactLabelAsync(frame, area, "화면을 터치해 주세요", ct);
        if (!area.Contains(touch.Bounds)) touch = DetectionResult.NotFound;
        DiagnosticObserveDetection("abyss_clear_touch_resolved", touch);
        if (touch.Found) return (DetectionResult.NotFound, touch);
        var title = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
        var safeTitle = ScaleAbyssResultRoi(new Rectangle(120, 120, 560, 300), frame.Size);
        if (!AbyssClearEvidencePolicy.StrongTitle(title, safeTitle))
            title = DetectionResult.NotFound;
        DiagnosticObserveDetection("abyss_clear_title_fallback", title);
        return (title, DetectionResult.NotFound);
    }
}
