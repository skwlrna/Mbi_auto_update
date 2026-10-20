namespace FishingAutomation;

// The callback must return only after the recipe's completion and output
// have been verified. Any error/cancellation stops the whole queue.
internal static class CraftingQueueCoordinator
{
    internal static async Task RunAsync(
        IReadOnlyList<CraftingPlan> plans,
        Func<CraftingPlan, int, CancellationToken, Task> runVerifiedAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runVerifiedAsync);
        if (plans.Count == 0)
            throw new InvalidOperationException("순차 제작 대기 목록이 비어 있습니다.");

        for (int index = 0; index < plans.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var plan = plans[index];
            plan.Validate();
            // Sequential await is deliberate: never start another item
            // before this one's verified completion.
            await runVerifiedAsync(plan, index, ct);
        }
    }
}
