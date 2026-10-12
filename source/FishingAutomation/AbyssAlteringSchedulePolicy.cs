namespace FishingAutomation;

// A pure, read-only departure decision. The threshold is a short-wait
// efficiency filter, NOT a prediction of how long an Abyss dungeon will take.
// Once entered, the run continues until verified exit or safety cancellation.
internal static class AbyssAlteringSchedulePolicy
{
    internal const long MinimumUsefulWaitSeconds = 90;

    internal static bool CanRun(
        IReadOnlyList<AlteringWork> works,
        long minimumWaitSeconds = MinimumUsefulWaitSeconds)
    {
        if (minimumWaitSeconds < 1 || works is null || works.Count == 0)
            return false;
        var inProgress = works
            .Where(x => x.State == "InProgress" && !x.IsCompleted)
            .ToArray();
        if (inProgress.Length == 0) return false;

        // All active facility lanes must have a known, sufficiently long
        // wait. Real processing continues while the manager is away; always
        // re-query the server after verified outside return.
        return inProgress.All(x => x.RemainingSeconds >= minimumWaitSeconds);
    }
}
