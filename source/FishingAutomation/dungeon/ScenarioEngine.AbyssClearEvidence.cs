using System.Drawing;
namespace DungeonVisionBot;
internal sealed partial class ScenarioEngine
{
    // Exact text in the bottom prompt region only. The animated title is advisory.
    private async Task<(DetectionResult Title, DetectionResult Touch)> ReadAbyssClearEvidenceAsync(
        Bitmap frame, CancellationToken ct)
    {
        _abyssResultOcr ??= new OcrRecognizer();
        var area = ScaleAbyssResultRoi(AbyssClearTouchRecoverySafeRoi, frame.Size);
        var touch = await _abyssResultOcr.FindCompactLabelAsync(frame, area, "화면을 터치해 주세요", ct);
        if (!area.Contains(touch.Bounds)) touch = DetectionResult.NotFound;
        DiagnosticObserveDetection("abyss_clear_touch_resolved", touch);
        return (DetectionResult.NotFound, touch);
    }
}
