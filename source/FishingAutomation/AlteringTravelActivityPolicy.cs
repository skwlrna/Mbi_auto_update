namespace FishingAutomation;

internal enum AlteringTravelActivityVerdict
{
    SafeToObserve,
    AwaitPossibleTravelDialog,
    Unsafe
}

// F9 post-click observation does not send input. In particular, IsReviving can
// remain stale in the CLI even when the character is back in a normal field;
// use the SAME base predicate as the pre-click GatheringSafetyPolicy guard.
// A genuine combat/death/dungeon is never treated as a transient popup.
internal static class AlteringTravelActivityPolicy
{
    internal const int MaxAmbiguousFrames = 3;

    // An F9 session can be started while a PREVIOUS, positively reported
    // auto-travel is still running. Only observe it without inputs, for a
    // bounded interval; a safe idle is freshly confirmed twice afterwards.
    internal static readonly TimeSpan ManagedPreInputTravelWaitLimit = TimeSpan.FromSeconds(30);
    internal const int RequiredFreshManagedIdleReads = 2;

    internal static bool IsManagedSafeIdle(GatheringActivity activity)
        => GatheringSafetyPolicy.IsSafeField(activity) &&
           !activity.IsAutoTraveling &&
           !activity.IsGathering && !activity.IsFishing &&
           !activity.IsInCombat;

    internal static bool CanObserveExistingTravelBeforeManagedInput(GatheringActivity activity)
        => activity.IsAutoTraveling &&
           GatheringSafetyPolicy.IsSafeField(activity) &&
           !activity.IsGathering && !activity.IsFishing &&
           !activity.IsInCombat;

    internal static bool IsWithinManagedPreInputTravelWait(TimeSpan elapsed)
        => elapsed >= TimeSpan.Zero && elapsed < ManagedPreInputTravelWaitLimit;

    internal static AlteringTravelActivityVerdict AssessAfterMove(GatheringActivity activity)
    {
        if (GatheringSafetyPolicy.IsSafeField(activity) && !activity.IsInCombat)
            return AlteringTravelActivityVerdict.SafeToObserve;

        // An NPC/dialogue flag after the click might be the travel confirmation
        // that has not yet become visually detectable. Wait a bounded number
        // of frames with NO input; never use the CLI flag to authorize Space.
        if ((activity.IsDialoguePlaying || activity.IsWaitingForSelection) &&
            CanConfirmVerifiedTravelDialog(activity))
            return AlteringTravelActivityVerdict.AwaitPossibleTravelDialog;

        return AlteringTravelActivityVerdict.Unsafe;
    }

    // Use ONLY after the correct move click and in combination with two
    // independent images of the exact managed travel-dialog visual, and the
    // existing bounded confirmation policy. Generic dialogue is NOT proof.
    internal static bool CanConfirmVerifiedTravelDialog(GatheringActivity activity)
        => !activity.IsDead &&
           !activity.IsInCombat &&
           !activity.IsAutoTraveling &&
           !activity.IsGathering &&
           !activity.IsFishing &&
           activity.DungeonState == "NotInDungeon" &&
           !activity.IsPlayingTutorial &&
           !activity.IsInScenario &&
           !activity.IsPlayingPerformance &&
           !activity.IsPlayingMiniGame &&
           !activity.IsHousingEditMode;

    internal static string Describe(GatheringActivity activity)
        => "Dead=" + activity.IsDead +
           ", Reviving=" + activity.IsReviving +
           ", Combat=" + activity.IsInCombat +
           ", AutoTraveling=" + activity.IsAutoTraveling +
           ", Dialogue=" + activity.IsDialoguePlaying +
           ", WaitingSelection=" + activity.IsWaitingForSelection +
           ", Dungeon=" + activity.DungeonState +
           ", Tutorial=" + activity.IsPlayingTutorial +
           ", Scenario=" + activity.IsInScenario +
           ", Performance=" + activity.IsPlayingPerformance +
           ", MiniGame=" + activity.IsPlayingMiniGame +
           ", Housing=" + activity.IsHousingEditMode +
           ", Gathering=" + activity.IsGathering +
           ", Fishing=" + activity.IsFishing;
}
