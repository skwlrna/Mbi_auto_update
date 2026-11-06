namespace FishingAutomation;

// A visible Stop control is not permission to press Space by itself.
// A transient CLI/UI disagreement must be observed, not treated as a failed stop.
internal enum LifeSkillStopInputDecision
{
    NoVisibleStop,
    AwaitCliStop,
    VerifiedStop
}

internal static class LifeSkillStopPolicy
{
    internal static LifeSkillStopInputDecision DecideStopInput(
        bool stopButtonVisible, string? mainButtonState)
        => !stopButtonVisible
            ? LifeSkillStopInputDecision.NoVisibleStop
            : mainButtonState == "Stop"
                ? LifeSkillStopInputDecision.VerifiedStop
                : LifeSkillStopInputDecision.AwaitCliStop;

    // The CLI can retain a stale Stop indication even after an action really ends.
    // The opposite also happens: IsGathering becomes false while a visible
    // Stop control still owns the active 100-action task. Require BOTH sources.
    // Hidden main controls can also make IsGathering false during autoplay.
    internal static bool IsStoppedAfterSpace(GatheringActivity activity, bool stopButtonVisible)
        => !activity.IsAutoPlaying && IsStoppedAfterSpace(
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
