namespace FishingAutomation.Telemetry;

// Normalized from the EXISTING vetted CLI parsers, not raw socket bytes.
// Only controlled status and counts are retained; no item, character,
// facility or recipe identifiers escape this diagnostic result.
internal sealed record CliDiagnosticProjection(
    string Status, CommonObservationDomain? Domain = null,
    CommonObservationState? State = null,
    int Count = 0, int InProgress = 0, int Completed = 0,
    bool AuthorizesGameplay = false);

internal static class ReadOnlyCliObservationProjection
{
    // The production CLI maps timeouts and unavailability to its own
    // "disconnected" state. These are NOT authenticated disconnect events.
    internal static CliDiagnosticProjection Connection(
        string command, bool success, string state, string? error)
    {
        if (command != "status")
            return new("command_not_accepted");
        if (success && state == "connected" && error is null)
            return new("state_observed", CommonObservationDomain.Connection,
                CommonObservationState.Connected);
        if (!success && state == "disconnected" && error == "disconnected")
            return new("state_observed", CommonObservationDomain.Connection,
                CommonObservationState.Disconnected);
        return new("not_verified");
    }

    // The full production GetActivity parsing is performed by
    // GatheringQueries.ParseActivity before the following booleans are
    // supplied. Missing/invalid fields never become "Idle"/"Completed".
    internal static CliDiagnosticProjection Activity(
        bool autoTraveling, bool inCombat, bool autoPlaying,
        bool isGathering, bool isFishing)
    {
        if (autoTraveling || inCombat || autoPlaying)
            return new("activity_observed", CommonObservationDomain.Activity,
                CommonObservationState.Active);
        if (isGathering)
            return new("gathering_observed", CommonObservationDomain.Gathering,
                CommonObservationState.InProgress);
        if (isFishing)
            return new("fishing_seen_unmapped");
        return new("activity_not_classified");
    }

    // Accepts only counts produced by AlteringQueries.ParseWorks (which
    // validates the schema, per-work state and completedCount first).
    // A Completed row means a CLI work row is marked complete, NOT a claimed
    // item receipt, a ready game input or a finished multi-altering batch.
    internal static CliDiagnosticProjection AlteringWorks(
        int total, int running, int completed, int notStarted)
    {
        if (total < 0 || total > 1024 || running < 0 || completed < 0 ||
            notStarted < 0 || (long)running + completed + notStarted != total)
            return new("invalid_work_counts");
        if (total == 0)
            return new("empty_work_list_not_completion");
        if (running > 0)
            return new("work_running", CommonObservationDomain.Altering,
                CommonObservationState.InProgress, total, running, completed);
        if (completed == total)
            return new("all_work_rows_completed_not_receipted",
                CommonObservationDomain.Altering, CommonObservationState.Completed,
                total, 0, completed);
        return new("mixed_or_pending_work_not_completion",
            Count: total, InProgress: running, Completed: completed);
    }

    internal static CliDiagnosticProjection InventoryShape(
        bool querySucceeded, bool validItemList, int itemRows)
    {
        if (!querySucceeded || !validItemList || itemRows < 0 || itemRows > 100000)
            return new("inventory_unavailable");
        // One snapshot cannot establish quantity change or absence of
        // materials; the existing InventoryLootTracker stays authoritative.
        return new("inventory_snapshot_only", Count: itemRows);
    }
}

/// <summary>
/// Offline comparison of known local stage and validated read-only work rows.
/// This reports discrepancies, never issues a retry, travel, receipt or input.
/// In particular, no work rows != completion and timer 0 != receipt.
/// </summary>
internal static class MultiAlteringObservationAudit
{
    internal sealed record Result(
        string Status, bool NeedsReview, bool AuthorizesGameplay = false);

    internal static Result Compare(
        CommonObservationState? localStage,
        CliDiagnosticProjection works)
    {
        if (works.Status is "invalid_work_counts" or "empty_work_list_not_completion")
            return new("unknown_server_work_state", true);
        if (!works.Domain.HasValue || works.Domain != CommonObservationDomain.Altering ||
            !works.State.HasValue || localStage is null)
            return new("insufficient_cross_evidence", true);
        if (localStage == CommonObservationState.Completed &&
            works.State == CommonObservationState.InProgress)
            return new("local_complete_but_work_running", true);
        if (localStage == CommonObservationState.InProgress &&
            works.State == CommonObservationState.Completed)
            return new("server_rows_completed_receipt_unknown", true);
        if (localStage == works.State)
            return new("same_stage_diagnostic_only", false);
        return new("unclassified_stage_difference", true);
    }
}
