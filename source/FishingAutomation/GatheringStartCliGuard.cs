namespace FishingAutomation;

// Only already-supported, read-only CLI get_activity observations may reach here.
// This is NOT packet decoding and cannot identify the selected screen row or target.
// Two successive observations must agree before new non-idempotent screen input.
internal static class GatheringStartCliGuard
{
    internal static bool IsStableSafeToStart(
        GatheringActivity first,
        GatheringActivity second,
        bool staleStopVisuallyAbsent)
    {
        return IsSafe(first, staleStopVisuallyAbsent) &&
               IsSafe(second, staleStopVisuallyAbsent) &&
               first.MainButtonState == second.MainButtonState &&
               first.IsAutoPlaying == second.IsAutoPlaying &&
               first.LastRunningInteractionType == second.LastRunningInteractionType &&
               first.DungeonState == second.DungeonState;
    }

    private static bool IsSafe(GatheringActivity activity, bool staleStopVisuallyAbsent)
        => GatheringSafetyPolicy.IsSafeField(activity) &&
           !activity.IsInCombat &&
           !activity.IsAutoTraveling &&
           !activity.IsGathering &&
           !activity.IsFishing &&
           // A lingering CLI Stop is tolerable only after the existing
           // independent screen probe proved that no Stop control remains.
           (activity.MainButtonState != "Stop" || staleStopVisuallyAbsent);
}
