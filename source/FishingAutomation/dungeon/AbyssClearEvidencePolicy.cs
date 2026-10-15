namespace DungeonVisionBot;

// A clear-looking title can also appear during live combat. Only a genuinely
// detected touch prompt together with the title may advance this state.
internal static class AbyssClearEvidencePolicy
{
    internal static int Next(int previous, bool clearTitle, bool touchPrompt)
        => clearTitle && touchPrompt ? Math.Min(2, Math.Max(0, previous) + 1) : 0;

    internal static bool Confirmed(int consecutive)
        => consecutive >= 2;
}
