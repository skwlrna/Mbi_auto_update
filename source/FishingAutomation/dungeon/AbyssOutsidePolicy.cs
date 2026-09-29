namespace DungeonVisionBot;

internal static class AbyssOutsidePolicy
{
    internal static bool CanAccept(
        bool exitConfirmed,
        int matchedOutsideHudMarkers,
        bool clearScreenVisible,
        bool touchPromptVisible)
    {
        if (!exitConfirmed)
            return false;

        if (matchedOutsideHudMarkers < 3)
            return false;

        if (clearScreenVisible || touchPromptVisible)
            return false;

        return true;
    }
}
