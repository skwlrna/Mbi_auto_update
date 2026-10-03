namespace FishingAutomation;

internal static class LifeSkillStopPolicy
{
    internal static bool IsStoppedAfterSpace(GatheringActivity activity)
        => !activity.IsGathering &&
           !activity.IsAutoTraveling &&
           !activity.IsFishing;
}
