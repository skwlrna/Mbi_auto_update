namespace FishingAutomation;

// A pure, read-only departure decision. The threshold is a short-wait
// efficiency filter, NOT a prediction of how long an Abyss dungeon will take.
// Once entered, the run continues until verified exit or safety cancellation.
internal static class AbyssAlteringSchedulePolicy
{
    internal const long MinimumUsefulWaitSeconds = 90;

    // The facility manager collects a facility's WHOLE current batch, not a
    // single finished slot. Only its read-only queue state is inspected here.
    // The manager remains the only owner of receipts, registration and travel.
    internal static bool NeedsProcessingAfterRound(
        IReadOnlyCollection<string> selectedFacilities,
        IReadOnlyList<AlteringWork> works)
    {
        ArgumentNullException.ThrowIfNull(selectedFacilities);
        ArgumentNullException.ThrowIfNull(works);
        if (selectedFacilities.Count == 0 ||
            selectedFacilities.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("어비스 병행 가공 시설 목록이 비어 있거나 올바르지 않습니다.");

        foreach (string facility in selectedFacilities.Distinct(StringComparer.Ordinal))
        {
            var batch = works.Where(w => w.FacilityName == facility).ToArray();
            // An empty selected lane may have next work to register, or a
            // consumed batch that must be reconciled by the coordinator.
            if (batch.Length == 0) return true;

            if (batch.Any(w =>
                w.State is not ("InProgress" or "Completed" or "NotStarted") ||
                w.IsCompleted != (w.State == "Completed") ||
                w.RemainingSeconds < 0))
                throw new InvalidDataException("CLI 가공 작업 상태 불일치 · 무조건 다시 하기 금지");

            if (batch.Any(w => w.State == "NotStarted") ||
                batch.All(w => w.IsCompleted))
                return true;

            // Partial completion with other active slots is not yet a
            // collectable whole-facility batch under the manager's policy.
            if (!batch.Any(w => w.State == "InProgress" && !w.IsCompleted))
                throw new InvalidDataException("진행 중인 가공이 없어 상태 재확인 필요");
        }
        return false;
    }

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
