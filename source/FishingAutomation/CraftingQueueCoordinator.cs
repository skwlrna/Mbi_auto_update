namespace FishingAutomation;

// The callback must return only after the recipe's completion and output
// have been verified. Any error/cancellation stops the whole queue.
// Only confirmed material acquisition failures may skip a queued recipe.
// Unknown UI/input/identity faults must still abort the full queue.
internal sealed class CraftingMaterialAcquisitionException : InvalidOperationException
{
    internal string MaterialName { get; }

    internal CraftingMaterialAcquisitionException(
        string? materialName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        MaterialName = string.IsNullOrWhiteSpace(materialName) ? "미확인 재료" : materialName;
    }
}

internal sealed record CraftingQueueRunResult(int Completed, int Skipped);

internal static class CraftingQueueCoordinator
{
    internal static async Task<CraftingQueueRunResult> RunAsync(
        IReadOnlyList<CraftingPlan> plans,
        Func<CraftingPlan, int, CancellationToken, Task> runVerifiedAsync,
        CancellationToken ct,
        Func<CraftingPlan, int, CraftingMaterialAcquisitionException, CancellationToken, Task>?
            onMaterialSkippedAsync = null)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runVerifiedAsync);
        if (plans.Count == 0)
            throw new InvalidOperationException("순차 제작 대기 목록이 비어 있습니다.");

        int completed = 0;
        int skipped = 0;
        for (int index = 0; index < plans.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var plan = plans[index];
            plan.Validate();
            try
            {
                // Never start another item before a verified completion or
                // confirmed material-failure skip with safe screen recovery.
                await runVerifiedAsync(plan, index, ct);
                completed++;
            }
            catch (CraftingMaterialAcquisitionException ex) when (!ct.IsCancellationRequested)
            {
                skipped++;
                if (onMaterialSkippedAsync is not null)
                    await onMaterialSkippedAsync(plan, index, ex, ct);
            }
        }
        return new CraftingQueueRunResult(completed, skipped);
    }
}
