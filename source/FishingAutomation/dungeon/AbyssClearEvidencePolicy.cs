namespace DungeonVisionBot;

// Title is advisory. Two consecutive bounded touch-prompt detections authorize input.
internal static class AbyssClearEvidencePolicy
{
    internal static int Next(int previous, bool clearTitle, bool touchPrompt)
        => touchPrompt ? Math.Min(2, Math.Max(0, previous) + 1) : 0;

    internal static bool Confirmed(int consecutive)
        => consecutive >= 2;
}
