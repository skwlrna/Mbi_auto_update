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

    private sealed record Entry(string Query, string Outcome,
        DateTimeOffset ObservedUtc);

    // Exactly five known read-only commands; action, filtered args,
    // capabilities and identity queries are deliberately excluded.
    private static readonly string[] Order =
    {
        "status", "get_activity", "get_altering_works",
        "get_items", "get_alterable_items"
    };

    private static readonly Dictionary<string, Entry> Recent =
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
        DateTimeOffset observedAtUtc)
    {
        // Never interpolate an untrusted input into a result.
        if (!Order.Contains(command, StringComparer.Ordinal))
            return;

        string outcome;
        if (success)
        {
            outcome = command == "status" && state == "connected"
                ? "cli_connected" : "cli_response_accepted_not_game_event";
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
            if (Recent.TryGetValue(command, out var prior) &&
                observed < prior.ObservedUtc)
                return; // late async completion must not replace a newer call
            Recent[command] = new(command, outcome, observed);
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
                if (!Recent.TryGetValue(command, out var entry))
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
