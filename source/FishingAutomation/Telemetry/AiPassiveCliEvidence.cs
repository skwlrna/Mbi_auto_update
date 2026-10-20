namespace FishingAutomation.Telemetry;

/// <summary>
/// Opt-in, process-local, no-I/O bridge from ALREADY COMPLETED CLI calls to
/// an error-time AI evidence snippet. Never reads packets, JSON body, item
/// metadata, accounts, character names, IPs, PIDs or private error text.
/// Enabled only after the user's existing AI watcher process starts.
/// </summary>
internal static class AiPassiveCliEvidence
{
    private static readonly object Gate = new();
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(5);
    private static bool _enabled;
    private static AlteringWorkCountSnapshot? _lastVerifiedAltering;
    private sealed record ScreenEntry(VerifiedScreenIncidentSignal Signal,
        DateTimeOffset ObservedUtc);
    // Independent from the CLI rows: already-verified, post-gate screen decisions
    // only. Each signal retains two times so late callbacks cannot erase
    // evidence from before the error. No OCR text or frame is retained.
    private static readonly Dictionary<VerifiedScreenIncidentSignal,
        List<ScreenEntry>> ScreenRecent = new();

    private sealed record Entry(string Query, string Outcome,
        DateTimeOffset ObservedUtc);

    // Exactly five known read-only commands; action, filtered args,
    // capabilities and identity queries are deliberately excluded.
    private static readonly string[] Order =
    {
        "status", "get_activity", "get_altering_works",
        "get_items", "get_alterable_items"
    };

    private static readonly Dictionary<string, List<Entry>> Recent =
        new(StringComparer.Ordinal);

    internal static bool Enabled
    {
        get { lock (Gate) return _enabled; }
    }

    internal static void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            // Reset on each watcher lifecycle transition: never reuse
            // evidence from an earlier session or after watcher exit.
            _enabled = enabled;
            Recent.Clear();
            _lastVerifiedAltering = null;
            ScreenRecent.Clear();
        }
    }

    /// <summary>
    /// Called AFTER the existing MabinogiMobileCli result is finalized.
    /// Success is the CLI wrapper's validity check, NOT game-state or
    /// work-completion proof. Failure labels come only from exact
    /// allowlisted machine-generated classifications.
    /// </summary>
    internal static void Observe(
        string command, bool success, string state, string? error,
        DateTimeOffset observedAtUtc, string? validatedDiagnostic = null,
        CliDiagnosticProjection? verifiedProjection = null)
    {
        // Never interpolate an untrusted input into a result.
        if (!Order.Contains(command, StringComparer.Ordinal))
            return;

        string outcome;
        if (success)
        {
            // These labels are emitted only by the existing vetted CLI
            // parsers. None of them authorizes game actions, item receipt
            // or a completed multi-altering batch.
            outcome = (command, validatedDiagnostic) switch
            {
                ("get_activity", "gathering_observed") =>
                    "cli_gathering_in_progress",
                ("get_activity", "activity_observed") =>
                    "cli_activity_observed",
                ("get_altering_works", "work_running") =>
                    "cli_altering_work_running",
                ("get_altering_works", "all_work_rows_completed_not_receipted") =>
                    "cli_work_rows_completed_receipt_unknown",
                ("get_altering_works", "empty_work_list_not_completion") =>
                    "cli_no_work_rows_completion_unknown",
                ("get_altering_works", "mixed_or_pending_work_not_completion") =>
                    "cli_mixed_or_pending_works",
                ("get_items", "inventory_snapshot_only") =>
                    "cli_inventory_shape_only_no_delta",
                ("get_alterable_items", "recipe_catalog_snapshot_only") =>
                    "cli_recipe_catalog_shape_only",
                (_, "cli_schema_unverified") => "cli_parser_schema_unverified",
                _ when command == "status" && state == "connected" =>
                    "cli_connected",
                _ => "cli_response_accepted_not_game_event"
            };
        }
        else
        {
            outcome = error switch
            {
                "timeout" => "cli_timeout_state_unknown",
                "cli_unavailable" => "cli_unavailable_state_unknown",
                "game_off" => "cli_reports_game_off",
                "option_off" => "cli_reports_option_off",
                "disconnected" => "cli_explicit_disconnect",
                "invalid_json" => "cli_invalid_json",
                "unexpected_response" => "cli_unexpected_schema",
                "cli_rejected" => "cli_rejected",
                _ => "cli_failed_unknown"
            };
        }

        lock (Gate)
        {
            if (!_enabled)
                return;
            var observed = observedAtUtc.ToUniversalTime();
            if (observed == default || observed.Year < 2000)
                return;
            if (!Recent.TryGetValue(command, out var history))
            {
                history = new List<Entry>(2);
                Recent.Add(command, history);
            }
            if (history.Count > 0 && observed < history[^1].ObservedUtc)
                return; // late async completion must not override fresh evidence

            // The existing strict CLI parser has already run. Keep only
            // bounded, anonymous counts; a matching change never confirms
            // cancellation, item delivery or a particular recipe ID.
            if (command == "get_altering_works")
            {
                if (success && verifiedProjection is not null &&
                    AlteringWorkDeltaDiagnostic.TryProjection(
                        verifiedProjection, observed, out var current) &&
                    current is not null)
                {
                    AlteringWorkDeltaAssessment delta =
                        AlteringWorkDeltaDiagnostic.Compare(
                            _lastVerifiedAltering, current);
                    _lastVerifiedAltering = current;
                    if (delta.Status == "possible_work_removal_not_completion")
                        outcome = "cli_altering_possible_removed_not_completed";
                    else if (delta.Status ==
                        "possible_progress_to_completed_receipt_unknown")
                        outcome = "cli_altering_possible_finished_receipt_unknown";
                }
                else
                {
                    _lastVerifiedAltering = null;
                }
            }

            if (history.Count > 0 && observed == history[^1].ObservedUtc)
                history[^1] = new(command, outcome, observed);
            else
            {
                history.Add(new(command, outcome, observed));
                if (history.Count > 2)
                    history.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Produce one cross-feature hypothesis from ALREADY COMPLETED validly
    /// timed query classifications. No new CLI requests or raw game data.
    /// Even if no source is available, explicitly documents coverage gaps.
    /// </summary>
    internal static string? SnapshotForIncident(
        string? mode, DateTimeOffset errorAtUtc)
    {
        lock (Gate)
        {
            if (!_enabled)
                return null;
            var at = errorAtUtc.ToUniversalTime();
            var facts = new List<CommonIncidentFact>(Order.Length);
            foreach (string command in Order)
            {
                if (!Recent.TryGetValue(command, out var history))
                    continue;
                Entry? entry = null;
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    if (history[i].ObservedUtc <= at)
                    {
                        entry = history[i];
                        break;
                    }
                }
                if (entry is null)
                    continue;
                var age = at - entry.ObservedUtc;
                if (age < TimeSpan.Zero || age > MaximumAge)
                    continue;
                facts.Add(new(command, entry.Outcome));
            }

            return CommonIncidentTriage.Evaluate(mode, facts).ToEvidenceBlock();
        }
    }

    /// <summary>
    /// Called only AFTER existing production screen guards have passed.
    /// This is a cache write: it performs no capture, OCR, CLI or input.
    /// Unknown signal values and out-of-order notifications are discarded.
    /// </summary>
    internal static void ObserveVerifiedScreen(
        VerifiedScreenIncidentSignal signal, DateTimeOffset observedAtUtc)
    {
        if (VerifiedScreenIncidentDiagnostic.AreaOf(signal) ==
            CommonIncidentArea.Unknown)
            return;
        var observed = observedAtUtc.ToUniversalTime();
        if (observed == default || observed.Year < 2000)
            return;
        lock (Gate)
        {
            if (!_enabled)
                return;
            if (!ScreenRecent.TryGetValue(signal, out var history))
            {
                history = new List<ScreenEntry>(2);
                ScreenRecent.Add(signal, history);
            }
            if (history.Count > 0 && observed < history[^1].ObservedUtc)
                return;
            if (history.Count > 0 && observed == history[^1].ObservedUtc)
                history[^1] = new(signal, observed);
            else
            {
                history.Add(new(signal, observed));
                if (history.Count > 2)
                    history.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Build a separate Stage19 hint from an existing visual check and
    /// optional previously completed validated activity CLI fact. Never
    /// infer item receipt, server clear, cause, or safe corrective input.
    /// </summary>
    internal static string? SnapshotForScreenIncident(
        string? mode, DateTimeOffset errorAtUtc)
    {
        lock (Gate)
        {
            if (!_enabled)
                return null;
            var at = errorAtUtc.ToUniversalTime();
            ScreenEntry? latest = null;
            foreach (var history in ScreenRecent.Values)
            {
                foreach (var entry in history)
                {
                    if (entry.ObservedUtc > at ||
                        VerifiedScreenIncidentDiagnostic.AreaOf(entry.Signal) !=
                            CommonIncidentTriage.MapMode(mode))
                        continue;
                    if (latest is null || entry.ObservedUtc > latest.ObservedUtc)
                        latest = entry;
                }
            }
            if (latest is null)
                return null;

            string? activity = null;
            if (Recent.TryGetValue("get_activity", out var entries))
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var item = entries[i];
                    if (item.ObservedUtc <= at &&
                        at - item.ObservedUtc <= MaximumAge)
                    {
                        activity = item.Outcome;
                        break;
                    }
                }
            }
            return VerifiedScreenIncidentDiagnostic.Render(
                mode, latest.Signal, latest.ObservedUtc, at, activity);
        }
    }

    /// <summary>
    /// Returns fixed machine labels only, bounded to five lines and 5 seconds.
    /// CLI observations finishing AFTER the event are never included.
    /// The text is diagnostic context, not instructions or game authority.
    /// No string originated in a packet, CLI stdout or error is echoed.
    /// </summary>
    internal static string? SnapshotForError(DateTimeOffset errorAtUtc)
    {
        lock (Gate)
        {
            if (!_enabled)
                return null;

            var at = errorAtUtc.ToUniversalTime();
            var lines = new List<string>(Order.Length);
            foreach (string command in Order)
            {
                if (!Recent.TryGetValue(command, out var entries))
                    continue;
                // Keep at most two recent finishes so a callback AFTER an
                // error cannot erase the immediately preceding observation.
                Entry? entry = null;
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].ObservedUtc <= at)
                    {
                        entry = entries[i];
                        break;
                    }
                }
                if (entry is null)
                    continue;
                TimeSpan elapsed = at - entry.ObservedUtc;
                if (elapsed < TimeSpan.Zero || elapsed > MaximumAge)
                    continue;
                long ageMilliseconds = Math.Clamp(
                    (long)elapsed.TotalMilliseconds, 0L, 5000L);
                lines.Add(command + "=" + entry.Outcome +
                    ";age_ms=" + ageMilliseconds);
            }
            if (lines.Count == 0)
                return null;

            return "BEGIN READ-ONLY CLI DIAGNOSTIC (observed before error; " +
                   "not decoded packet data or permission to act)\n" +
                   string.Join("\n", lines) + "\n" +
                   "END READ-ONLY CLI DIAGNOSTIC; never infer receipt, " +
                   "completion, or safe automation from this evidence";
        }
    }
}
