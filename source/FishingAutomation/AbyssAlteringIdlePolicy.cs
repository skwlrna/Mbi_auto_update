namespace FishingAutomation;

/// <summary>
/// Advisory gate only: never authorizes a UI action, receipt, or registration.
/// The existing multi-altering coordinator remains the sole owner of those actions.
/// </summary>
internal static class AbyssAlteringIdlePolicy
{
    // Do not enter an Abyss round when a processing lane needs attention soon.
    internal const long MinimumRemainingSeconds = 240;

    internal static bool CanYieldToAbyss(
        IReadOnlyList<AlteringWork> works,
        IReadOnlyList<AlteringPlan> plans)
    {
        if (works is null || plans is null || plans.Count == 0)
            return false;

        var facilities = plans.Select(p => p.FacilityName)
            .ToHashSet(StringComparer.Ordinal);
        var lanes = works.Where(w => facilities.Contains(w.FacilityName))
            .GroupBy(w => w.FacilityName, StringComparer.Ordinal)
            .ToArray();

        if (lanes.Length == 0)
            return false;

        // Entirely completed lanes should be collected by the original manager
        // before we consider leaving the field for another dungeon round.
        if (lanes.Any(lane => lane.Count() > 7 || lane.All(work => work.IsCompleted)))
            return false;

        var pending = lanes.SelectMany(lane => lane)
            .Where(work => !work.IsCompleted)
            .ToArray();

        // Unknown queue states are never considered a safe waiting period.
        return pending.Length > 0 &&
            pending.All(work =>
                work.State == "InProgress" &&
                work.RemainingSeconds >= MinimumRemainingSeconds);
    }
}
