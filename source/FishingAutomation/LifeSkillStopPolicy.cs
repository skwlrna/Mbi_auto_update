namespace FishingAutomation;

internal static class LifeSkillStopPolicy
{
    internal static bool IsStoppedAfterSpace(GatheringActivity activity)
        => IsStoppedAfterSpace(
            activity.IsGathering,
            activity.IsAutoTraveling,
            activity.IsFishing);

    internal static bool IsStoppedAfterSpace(
        bool isGathering,
        bool isAutoTraveling,
        bool isFishing)
        => !isGathering && !isAutoTraveling && !isFishing;
}
