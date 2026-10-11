namespace FishingAutomation;

// A pure, read-only eligibility decision. Never grants UI/input authority by itself.
internal static class AbyssAlteringSchedulePolicy
{
    // A short queue should finish normally, without an expensive dungeon detour.
    internal const long MinimumUsefulWaitSeconds = 90;

    internal static bool CanRun(IReadOnlyList<AlteringWork> works)
    {
        if (works is null || works.Count == 0) return false;
        var inProgress = works.Where(x => x.State == "InProgress" && !x.IsCompleted).ToArray();
        if (inProgress.Length == 0) return false;

        // Every active facility batch must have enough estimated time remaining.
        // Unknown/negative timers mean no safe handoff. Completed slots are
        // harmless here: normal facility-wide receipt remains deferred.
        return inProgress.All(x => x.RemainingSeconds >= MinimumUsefulWaitSeconds);
    }
}
