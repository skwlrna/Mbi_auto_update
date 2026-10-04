namespace FishingAutomation;


/// <summary>
/// Interprets activity flags for free field gathering.
/// Reviving is intentionally ignored in gathering because the live CLI can leave
/// that flag stale after the character is already back in the field.
/// Combat is a temporary wait state, not a failure state; callers must wait for
/// combat to end before sending any gathering UI input.
/// </summary>
internal static class GatheringSafetyPolicy
{
    internal static bool IsSafeField(GatheringActivity activity)
        => !activity.IsDead &&
           !activity.IsDialoguePlaying &&
           !activity.IsWaitingForSelection &&
           activity.DungeonState == "NotInDungeon" &&
           !activity.IsPlayingTutorial &&
           !activity.IsInScenario &&
           !activity.IsPlayingPerformance &&
           !activity.IsPlayingMiniGame &&
           !activity.IsHousingEditMode;

    internal static bool ShouldWaitForCombat(GatheringActivity activity)
        => activity.IsInCombat;
}
