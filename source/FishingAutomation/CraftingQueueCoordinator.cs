namespace FishingAutomation;

// Only confirmed material failures may terminate/skip a recipe.
// A screen recognition fault is deferrable ONLY after the crafting engine
// has verified a neutral screen, safe CLI idle, and zero completed crafts.
// Unknown UI/input/identity faults must still stop the queue.
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

// This exception is NEVER emitted after a craft click or any confirmed craft
// progress. Scheduler must not invent output credits or skip partial batches.
internal sealed class CraftingRetryableScreenRecognitionException : InvalidOperationException
{
    internal CraftingRetryableScreenRecognitionException(
        string message, Exception innerException) : base(message, innerException) { }
}

internal sealed record CraftingQueueRunResult(int Completed, int Skipped);

internal static class CraftingQueueCoordinator
{
    internal static async Task<CraftingQueueRunResult> RunAsync(
        IReadOnlyList<CraftingPlan> plans,
        Func<CraftingPlan, int, CancellationToken, Task> runVerifiedAsync,
        CancellationToken ct,
        Func<CraftingPlan, int, CraftingMaterialAcquisitionException, CancellationToken, Task>?
            onMaterialSkippedAsync = null,
        Func<CraftingPlan, int, CraftingRetryableScreenRecognitionException, CancellationToken, Task>?
            onScreenDeferredAsync = null,
        Func<TimeSpan, CancellationToken, Task>? waitForRetryAsync = null)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runVerifiedAsync);
        if (plans.Count == 0)
            throw new InvalidOperationException("순차 제작 대기 목록이 비어 있습니다.");

        var pendingRecognition = new Queue<int>();
        int completed = 0;
        int skipped = 0;

        async Task<bool> TryRecipeAsync(int index)
        {
            ct.ThrowIfCancellationRequested();
            var plan = plans[index];
            plan.Validate();
            try
            {
                // This callback must return ONLY after verified crafting output.
                await runVerifiedAsync(plan, index, ct);
                completed++;
                return true;
            }
            catch (CraftingMaterialAcquisitionException ex) when (!ct.IsCancellationRequested)
            {
                skipped++;
                if (onMaterialSkippedAsync is not null)
                    await onMaterialSkippedAsync(plan, index, ex, ct);
            }
            catch (CraftingRetryableScreenRecognitionException ex) when (!ct.IsCancellationRequested)
            {
                // Exact plan identity, original target and its index are retained.
                // Only the verified no-craft recovery path can raise this type.
                pendingRecognition.Enqueue(index);
                if (onScreenDeferredAsync is not null)
                    await onScreenDeferredAsync(plan, index, ex, ct);
            }
            return false;
        }

        // One normally scheduled recipe, then ONE deferred screen-failure
        // recipe after each confirmed normal recipe. Other recipes are not
        // held hostage by a single unreliable background/OCR region.
        for (int index = 0; index < plans.Count; index++)
        {
            bool succeeded = await TryRecipeAsync(index);
            if (succeeded && pendingRecognition.Count > 0)
                await TryRecipeAsync(pendingRecognition.Dequeue());
        }

        // All regular plans were visited, but failed ones are NOT complete.
        // Retry them round-robin with bounded time between attempts. F10
        // remains authoritative; no fixed retry-count silently drops a plan.
        while (pendingRecognition.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var delay = TimeSpan.FromSeconds(30);
            if (waitForRetryAsync is not null)
                await waitForRetryAsync(delay, ct);
            else
                await Task.Delay(delay, ct);
            await TryRecipeAsync(pendingRecognition.Dequeue());
        }

        return new CraftingQueueRunResult(completed, skipped);
    }
}
