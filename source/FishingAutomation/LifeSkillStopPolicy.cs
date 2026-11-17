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
    // A refused/no-input boundary probe does not consume a retry.
    // Exactly two actually transmitted Space taps are allowed in one stop fence.
    internal static bool CanAttemptBoundedStop(
        int sentStopInputs, int stableVerifiedStopFrames, bool cooldownElapsed)
        => sentStopInputs is >= 0 and < 2 &&
           stableVerifiedStopFrames >= 2 && cooldownElapsed;

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
