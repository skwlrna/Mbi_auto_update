namespace FishingAutomation;

/// <summary>
/// Interprets activity flags for free field gathering without weakening the
/// existing hard hazard gates. The live V3.1.1 CLI can leave IsReviving=true
/// after the character is already back in a normal field state, so that single
/// flag is treated as stale only when independent field/action signals prove a
/// normal state.
/// </summary>
internal static class GatheringSafetyPolicy
{
    internal static bool IsSafeField(GatheringActivity activity)
    {
        if (activity.IsDead ||
            activity.IsInCombat ||
            activity.IsDialoguePlaying ||
            activity.IsWaitingForSelection ||
            activity.DungeonState != "NotInDungeon" ||
            activity.IsPlayingTutorial ||
            activity.IsInScenario ||
            activity.IsPlayingPerformance ||
            activity.IsPlayingMiniGame ||
            activity.IsHousingEditMode)
            return false;

        if (!activity.IsReviving)
            return true;

        return IsClearlyStaleReviving(activity);
    }

    internal static bool IsClearlyStaleReviving(GatheringActivity activity)
    {
        if (!activity.IsReviving || activity.IsDead)
            return false;

        bool idleField =
            !activity.IsAutoTraveling &&
            !activity.IsFishing &&
            activity.MainButtonState == "Compass" &&
            !activity.HasTarget &&
            activity.AvailableInteractionType == "None" &&
            activity.LastRunningInteractionType is "None" or "Talk";

        bool ownedGathering =
            !activity.IsFishing &&
            activity.MainButtonState == "Stop" &&
            activity.LastRunningInteractionType == "Gathering";

        return idleField || ownedGathering;
    }
}
