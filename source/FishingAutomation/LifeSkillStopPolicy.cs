namespace FishingAutomation;

internal static class LifeSkillStopPolicy
{
    // The CLI can retain a stale Stop indication even after an action really ends.
    // The opposite also happens: IsGathering becomes false while a visible
    // Stop control still owns the active 100-action task. Require BOTH sources.
    internal static bool IsStoppedAfterSpace(GatheringActivity activity, bool stopButtonVisible)
        => IsStoppedAfterSpace(
            activity.IsGathering,
            activity.IsAutoTraveling,
            activity.IsFishing,
            stopButtonVisible);

    internal static bool IsStoppedAfterSpace(
        bool isGathering,
        bool isAutoTraveling,
        bool isFishing,
        bool stopButtonVisible)
        => !isGathering && !isAutoTraveling && !isFishing && !stopButtonVisible;
}
