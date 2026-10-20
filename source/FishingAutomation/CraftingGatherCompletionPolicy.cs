namespace FishingAutomation;

// Auto-travel and in-game auto-gather are one asynchronous operation.
// Idle is NOT evidence that the requested ingredient has been collected.
internal static class CraftingGatherCompletionPolicy
{
    internal static bool HasVerifiedTarget(
        string? ingredient, long inventory, long required, int stableFreshReads)
        => !string.IsNullOrWhiteSpace(ingredient) &&
           required > 0 && inventory >= required && stableFreshReads >= 2;

    internal static bool MustStopUnnamed(string? ingredient, int idlePolls, bool sawActivity)
        => string.IsNullOrWhiteSpace(ingredient) &&
           idlePolls >= (sawActivity ? 5 : 8);
}
