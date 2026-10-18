namespace FishingAutomation.Telemetry;

/// <summary>
/// Two verified get_altering_works snapshots, count-only and process-local.
/// A work ID is NOT present: a matching count pattern never proves which
/// recipe was cancelled, completed, received or removed.
/// </summary>
internal sealed record AlteringWorkCountSnapshot(
    int Total, int Running, int Completed, int Pending, DateTimeOffset AtUtc);

internal sealed record AlteringWorkDeltaAssessment(
    string Status, int TotalChange = 0, int RunningChange = 0,
    int CompletedChange = 0, int PendingChange = 0,
    bool EvidenceOfReceipt = false, bool CancellationProven = false,
    bool AuthorizesGameplay = false);

internal static class AlteringWorkDeltaDiagnostic
{
    internal static bool TrySnapshot(
        CliDiagnosticReadResult? read, DateTimeOffset atUtc,
        out AlteringWorkCountSnapshot? snapshot)
    {
        snapshot = null;
        if (read is null ||
            read.Status != read.Projection.Status ||
            read.Projection.Status is not
                ("work_running" or "all_work_rows_completed_not_receipted" or
                 "mixed_or_pending_work_not_completion" or
                 "empty_work_list_not_completion"))
            return false;

        if (read.Projection.State is not null &&
            read.Admission?.Accepted != true)
            return false;
        if (read.Projection.State is null && read.Admission is not null)
            return false;
        return TryProjection(read.Projection, atUtc, out snapshot);
    }

    // Reuse only after the existing strict AlteringQueries.ParseWorks
    // parser succeeded. AI passive reads have no separate semantic
    // admission because they observe already-completed production CLI reads.
    internal static bool TryProjection(
        CliDiagnosticProjection p, DateTimeOffset atUtc,
        out AlteringWorkCountSnapshot? snapshot)
    {
        snapshot = null;
        if (p.Status is not
            ("work_running" or "all_work_rows_completed_not_receipted" or
             "mixed_or_pending_work_not_completion" or
             "empty_work_list_not_completion"))
            return false;
        // Source/type consistency: even an internal synthetic projection
        // cannot impersonate a validated altering domain or a work state.
        bool semanticRunning = p.Status == "work_running";
        bool semanticCompleted = p.Status == "all_work_rows_completed_not_receipted";
        if ((semanticRunning && (p.Domain != CommonObservationDomain.Altering ||
                p.State != CommonObservationState.InProgress)) ||
            (semanticCompleted && (p.Domain != CommonObservationDomain.Altering ||
                p.State != CommonObservationState.Completed)) ||
            (!semanticRunning && !semanticCompleted &&
                (p.Domain is not null || p.State is not null)))
            return false;
        int pending = p.Count - p.InProgress - p.Completed;
        if (p.Count is < 0 or > 1024 ||
            p.InProgress < 0 || p.Completed < 0 || pending < 0 ||
            (long)p.InProgress + p.Completed + pending != p.Count ||
            (p.Status == "work_running" && p.InProgress <= 0) ||
            (p.Status == "all_work_rows_completed_not_receipted" &&
                (p.Count == 0 || p.InProgress != 0 || p.Completed != p.Count)) ||
            (p.Status == "empty_work_list_not_completion" && p.Count != 0) ||
            (p.Status == "mixed_or_pending_work_not_completion" &&
                (p.InProgress != 0 || p.Count == 0 || p.Completed == p.Count)))
            return false;


        var at = atUtc.ToUniversalTime();
        if (at == default || at.Year < 2000)
            return false;

        snapshot = new(p.Count, p.InProgress, p.Completed, pending, at);
        return true;
    }

    internal static AlteringWorkDeltaAssessment Compare(
        AlteringWorkCountSnapshot? before, AlteringWorkCountSnapshot? after)
    {
        if (before is null || after is null)
            return new("missing_verified_pair");
        TimeSpan gap = after.AtUtc - before.AtUtc;
        if (gap <= TimeSpan.Zero)
            return new("out_of_order_pair");
        if (gap > TimeSpan.FromMinutes(15))
            return new("pair_too_old");

        int total = after.Total - before.Total;
        int running = after.Running - before.Running;
        int completed = after.Completed - before.Completed;
        int pending = after.Pending - before.Pending;
        if (total == 0 && running == 0 && completed == 0 && pending == 0)
            return new("unchanged_counts_identity_unknown");
        if (total < 0 && running == total && completed == 0 && pending == 0)
            return new("possible_work_removal_not_completion",
                total, running, completed, pending);
        if (total == 0 && running < 0 && completed == -running && pending == 0)
            return new("possible_progress_to_completed_receipt_unknown",
                total, running, completed, pending);
        if (total > 0 && running >= 0 && completed == 0 && pending >= 0)
            return new("possible_work_added",
                total, running, completed, pending);
        return new("work_counts_changed_identity_unknown",
            total, running, completed, pending);
    }
}

/// <summary>
/// One bounded 2-snapshot session. Invalid CLI data breaks continuity.
/// No polling, persistence, automation authority or packet subscription.
/// </summary>
internal sealed class AlteringWorkDeltaSession
{
    private AlteringWorkCountSnapshot? _last;
    internal AlteringWorkDeltaAssessment Observe(
        CliDiagnosticReadResult? read, DateTimeOffset capturedUtc)
    {
        if (!AlteringWorkDeltaDiagnostic.TrySnapshot(read, capturedUtc,
                out var current) || current is null)
        {
            _last = null;
            return new("unverified_snapshot_reset");
        }
        if (_last is null)
        {
            _last = current;
            return new("baseline_recorded");
        }
        var assessed = AlteringWorkDeltaDiagnostic.Compare(_last, current);
        _last = current;
        return assessed;
    }

    internal void Clear() => _last = null;
}
