using System.Drawing;
namespace DungeonVisionBot;

// Keep the primary OCR proof; fallback requires high score and two stationary title frames.
internal static class AbyssClearEvidencePolicy
{
    internal static int Next(int previous, bool clearTitle, bool touchPrompt)
        => touchPrompt ? Math.Min(2, Math.Max(0, previous) + 1) : 0;
    internal static bool Confirmed(int consecutive) => consecutive >= 2;
    internal static bool StrongTitle(DetectionResult title, Rectangle safeArea)
        => title.Found && title.Score >= 0.78 && title.Bounds.Width >= 20 &&
           title.Bounds.Height >= 10 && safeArea.Contains(title.Center);
    internal static bool SameTitle(DetectionResult a, DetectionResult b)
        => a.Found && b.Found && Math.Abs(a.Center.X - b.Center.X) <= 30 &&
           Math.Abs(a.Center.Y - b.Center.Y) <= 25;
    internal static DetectionResult TitleTouchPoint(Size size)
    {
        if (size.Width < 400 || size.Height < 600 ||
            size.Width / (double)size.Height is < 0.74 or > 0.92)
            return DetectionResult.NotFound;
        return new DetectionResult(true, new Rectangle(
            (int)Math.Round(375 * size.Width / 800.0),
            (int)Math.Round(920 * size.Height / 1000.0),
            Math.Max(1, (int)Math.Round(50 * size.Width / 800.0)),
            Math.Max(1, (int)Math.Round(45 * size.Height / 1000.0))),
            1.0, "clear_title_visual_fallback");
    }
}
