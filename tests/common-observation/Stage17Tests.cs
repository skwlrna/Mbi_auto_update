using FishingAutomation.Telemetry;

internal static class Stage17Tests
{
    internal static int Run()
    {
        int checkedCount = 0;
        void Assert(bool pass, string name)
        {
            if (!pass)
                throw new InvalidOperationException("Stage17: " + name);
            checkedCount++;
        }

        DateTimeOffset now = new(2026, 10, 18, 11, 50, 0, TimeSpan.Zero);

        CliDiagnosticReadResult Read(int total, int running,
            int completed, int pending)
        {
            var p = ReadOnlyCliObservationProjection.AlteringWorks(
                total, running, completed, pending);
            return new(p.Status, p,
                p.State is null ? null : new CommonObservationAdmission(true, "accepted"),
                null);
        }

        var before = Read(32, 3, 25, 4);
        var after = Read(31, 2, 25, 4);
        var session = new AlteringWorkDeltaSession();
        Assert(session.Observe(before, now).Status == "baseline_recorded",
            "first read must only set reference");
        var removed = session.Observe(after, now.AddSeconds(15));
        Assert(removed is {
            Status: "possible_work_removal_not_completion",
            TotalChange: -1, RunningChange: -1, CompletedChange: 0,
            PendingChange: 0, CancellationProven: false,
            EvidenceOfReceipt: false, AuthorizesGameplay: false
        }, "32-to-31, running 3-to-2, completed 25 unchanged");
        string rendered = CommonObservationDiagnosticReport.RenderWorkDelta(removed);
        Assert(rendered.Contains("취소·제거 가능성", StringComparison.Ordinal) &&
            rendered.Contains("전체 -1", StringComparison.Ordinal) &&
            rendered.Contains("진행 -1", StringComparison.Ordinal) &&
            rendered.Contains("완료표시 0", StringComparison.Ordinal) &&
            rendered.Contains("미확정", StringComparison.Ordinal),
            "diagnostic UI must show exact deltas with no proven cancellation");
        Assert(!rendered.Contains("아이템 수령 완료", StringComparison.Ordinal) &&
            !rendered.Contains("취소 확정", StringComparison.Ordinal),
            "UI overclaims a final game state");
        Assert(session.Observe(after, now.AddSeconds(16)).Status ==
            "unchanged_counts_identity_unknown",
            "unchanged aggregate is not an item identity claim");

        var finishedSession = new AlteringWorkDeltaSession();
        finishedSession.Observe(before, now);
        var finished = finishedSession.Observe(Read(32, 2, 26, 4),
            now.AddSeconds(25));
        Assert(finished is {
            Status: "possible_progress_to_completed_receipt_unknown",
            TotalChange: 0, RunningChange: -1, CompletedChange: 1,
            EvidenceOfReceipt: false, AuthorizesGameplay: false
        }, "in-progress-to-completed rows cannot prove item receipt");

        var addedSession = new AlteringWorkDeltaSession();
        addedSession.Observe(after, now);
        Assert(addedSession.Observe(before, now.AddSeconds(5)) is {
            Status: "possible_work_added", TotalChange: 1,
            RunningChange: 1, CompletedChange: 0,
            AuthorizesGameplay: false
        }, "work creation is only a possible addition");
        var mixedSession = new AlteringWorkDeltaSession();
        mixedSession.Observe(before, now);
        Assert(mixedSession.Observe(Read(31, 2, 24, 5),
            now.AddSeconds(3)).Status == "work_counts_changed_identity_unknown",
            "concurrent changes may not be labelled cancellation");

        // Valid zero-count state has no semantic source vote, but is
        // still an explicit bounded parser result; it must never complete
        // a multi-altering batch.
        var empty = Read(0, 0, 0, 0);
        Assert(AlteringWorkDeltaDiagnostic.TrySnapshot(empty, now, out var e) &&
            e is { Total: 0, Running: 0, Completed: 0, Pending: 0 },
            "empty works must be count snapshot only");
        Assert(session.Observe(empty, now.AddSeconds(18)).Status ==
            "work_counts_changed_identity_unknown",
            "zero work count cannot prove batch completed");
        Assert(AlteringWorkDeltaDiagnostic.Compare(null, e).Status ==
            "missing_verified_pair", "missing comparison baseline fabricated");

        var failSession = new AlteringWorkDeltaSession();
        failSession.Observe(before, now);
        var noCli = new CliDiagnosticReadResult(
            "cli_query_unavailable", new CliDiagnosticProjection("cli_query_unavailable"),
            null, null);
        Assert(failSession.Observe(noCli, now.AddSeconds(2)).Status ==
            "unverified_snapshot_reset", "query failure must reset pair");
        Assert(failSession.Observe(after, now.AddSeconds(3)).Status ==
            "baseline_recorded", "post-failure read cannot compare to old count");

        var rejected = new CliDiagnosticReadResult(
            "stale", after.Projection,
            new CommonObservationAdmission(false, "stale"), null);
        Assert(!AlteringWorkDeltaDiagnostic.TrySnapshot(rejected,
            now.AddSeconds(3), out _), "stale engine rejection promoted to delta");
        var malformed = new CliDiagnosticReadResult(
            "work_running", new CliDiagnosticProjection("work_running",
                CommonObservationDomain.Altering, CommonObservationState.InProgress,
                Count: 32, InProgress: 3, Completed: 30),
            new CommonObservationAdmission(true, "accepted"), null);
        Assert(!AlteringWorkDeltaDiagnostic.TrySnapshot(malformed, now, out _),
            "invalid negative pending count accepted");
        var spoofed = new CliDiagnosticProjection("work_running",
            CommonObservationDomain.Altering, CommonObservationState.InProgress,
            Count: 32, InProgress: 0, Completed: 25);
        Assert(!AlteringWorkDeltaDiagnostic.TryProjection(spoofed, now, out _),
            "zero-running status was improperly trusted");
        var future = new AlteringWorkCountSnapshot(32, 3, 25, 4, now.AddSeconds(10));
        Assert(AlteringWorkDeltaDiagnostic.Compare(future,
            new(31, 2, 25, 4, now)).Status == "out_of_order_pair",
            "out-of-order pair used for cancellation inference");
        Assert(AlteringWorkDeltaDiagnostic.Compare(
            new(32, 3, 25, 4, now),
            new(31, 2, 25, 4, now.AddMinutes(16))).Status == "pair_too_old",
            "stale 16-minute pair used for delta");
        var longSession = new AlteringWorkDeltaSession();
        longSession.Observe(before, now);
        Assert(longSession.Observe(after, now.AddMinutes(16)).Status ==
            "pair_too_old", "old snapshot should be replaced safely");
        Assert(longSession.Observe(Read(31, 2, 25, 4),
            now.AddMinutes(16).AddSeconds(1)).Status ==
            "unchanged_counts_identity_unknown",
            "new baseline not established after gap");

        // AI receives only fixed classification values from already
        // completed CLI queries when watcher opt-in is active.
        AiPassiveCliEvidence.SetEnabled(false);
        AiPassiveCliEvidence.Observe(
            "get_altering_works", true, "connected", null, now,
            before.Projection.Status, before.Projection);
        Assert(AiPassiveCliEvidence.SnapshotForError(now) is null,
            "disabled AI watcher retained new stage17 evidence");
        AiPassiveCliEvidence.SetEnabled(true);
        AiPassiveCliEvidence.Observe(
            "get_altering_works", true, "connected", null, now,
            before.Projection.Status, before.Projection);
        AiPassiveCliEvidence.Observe(
            "get_altering_works", true, "connected", null,
            now.AddMilliseconds(100),
            after.Projection.Status, after.Projection);
        string? evidence = AiPassiveCliEvidence.SnapshotForError(
            now.AddMilliseconds(150));
        Assert(evidence is not null && evidence.Contains(
            "get_altering_works=cli_altering_possible_removed_not_completed;age_ms=50",
            StringComparison.Ordinal), "AI does not report a bounded possible-removal hint");
        Assert(!evidence!.Contains("fabric", StringComparison.OrdinalIgnoreCase) &&
            !evidence.Contains("recipe", StringComparison.OrdinalIgnoreCase) &&
            !evidence.Contains("cancelled=true", StringComparison.OrdinalIgnoreCase),
            "AI evidence leaks work identity or claims verified cancellation");
        Assert(AiPassiveCliEvidence.SnapshotForError(now.AddSeconds(7)) is null,
            "AI retained stage17 evidence past 5 seconds");
        AiPassiveCliEvidence.SetEnabled(false);
        Assert(AiPassiveCliEvidence.SnapshotForError(now.AddMilliseconds(150)) is null,
            "watcher shutdown did not purge comparison state");
        AiPassiveCliEvidence.SetEnabled(true);
        AiPassiveCliEvidence.Observe(
            "get_altering_works", true, "connected", null, now,
            after.Projection.Status, after.Projection);
        Assert(!AiPassiveCliEvidence.SnapshotForError(now)!.Contains(
            "cli_altering_possible_removed_not_completed", StringComparison.Ordinal),
            "new AI session reused removed-work comparison from old watcher");
        AiPassiveCliEvidence.SetEnabled(false);

        Console.WriteLine("PASS " + checkedCount +
            " stage17 synthetic altering count change + AI fail-closed regressions");
        return checkedCount;
    }
}
