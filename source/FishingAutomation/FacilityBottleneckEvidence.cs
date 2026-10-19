namespace FishingAutomation;

// Read-only facility incident categories used by F9 diagnostics.
// This data is never input permission or proof of a missing receipt.
internal enum FacilityBottleneckCause
{
    QueueFullMasked,
    RecipeUnavailable,
    StalledWork,
    UncertainBatch
}

internal sealed record FacilityBottleneckSnapshot(
    string Facility, long QueueFullEpisodes, long RecipeUnavailableEpisodes,
    long StalledWorkEvents, long UncertainBatchAttempts, bool CurrentlyBlocked,
    long? BlockedSeconds, int LastKnownQueueDepth, int LastKnownCompleted,
    bool RepeatedIssue);
