using System.Text;
using System.Text.Json;

namespace FishingAutomation;

// Constructed ONLY at a known read-only pre-input boundary. Never construct
// around gathering, screen clicks, receipts, registration or unknown callbacks.
internal sealed class SafePreInputReadException : Exception
{
    internal string Operation { get; }
    internal SafePreInputReadException(string operation)
        : base("Read-only F9 preflight was not confirmed; game input prohibited")
    {
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 40 ||
            operation.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new ArgumentException("Invalid safe-read operation", nameof(operation));
        Operation = operation;
    }
}

internal sealed record MultiAlteringReadQuarantineSnapshot(
    string Facility, string Recipe, string Operation,
    int ConsecutiveFailures, bool Repeated, bool VerifiedRecovered,
    DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc,
    DateTimeOffset? NextReadUtc);

// A read exception after a click is NOT eligible. This is diagnostic-only
// and scoped to one coordinator run. Never deserialize it into authorization.
internal sealed class MultiAlteringReadQuarantineTracker
{
    private sealed record Entry(
        string Recipe, string Operation, int Failures,
        DateTimeOffset First, DateTimeOffset Last, DateTimeOffset? Next,
        bool Recovered);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    internal MultiAlteringReadQuarantineTracker(Func<DateTimeOffset>? now = null)
        => _now = now ?? (() => DateTimeOffset.UtcNow);

    internal MultiAlteringReadQuarantineSnapshot NoteFailure(
        AlteringPlan plan, string operation, TimeSpan backoff)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        if (backoff <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(backoff));
        _ = new SafePreInputReadException(operation);
        var now = _now();
        if (!_entries.TryGetValue(plan.FacilityName, out var old) ||
            old.Recipe != MultiAlteringBatchStore.Key(plan) ||
            old.Operation != operation || old.Recovered)
            old = new Entry(MultiAlteringBatchStore.Key(plan), operation, 0,
                now, now, null, false);
        var update = old with
        {
            Failures = Math.Min(1000000, old.Failures + 1),
            Last = now, Next = now.Add(backoff), Recovered = false
        };
        _entries[plan.FacilityName] = update;
        return ToSnapshot(plan.FacilityName, plan.DisplayName, update);
    }

    internal bool IsBlocked(string facility, DateTimeOffset now)
        => _entries.TryGetValue(facility, out var x) &&
           !x.Recovered && x.Next.HasValue && now < x.Next.Value;

    internal bool HasUnverified(string facility)
        => _entries.TryGetValue(facility, out var x) && !x.Recovered;

    internal MultiAlteringReadQuarantineSnapshot NoteRecovered(AlteringPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_entries.TryGetValue(plan.FacilityName, out var found) ||
            found.Recovered || found.Recipe != MultiAlteringBatchStore.Key(plan))
            throw new InvalidOperationException("Read quarantine recovery identity mismatch");
        var update = found with { Last = _now(), Next = null, Recovered = true };
        _entries[plan.FacilityName] = update;
        return ToSnapshot(plan.FacilityName, plan.DisplayName, update);
    }

    private static MultiAlteringReadQuarantineSnapshot ToSnapshot(
        string facility, string display, Entry entry)
        => new(facility, display, entry.Operation, entry.Failures,
            entry.Failures >= 2, entry.Recovered, entry.First,
            entry.Last, entry.Next);

    internal IReadOnlyList<MultiAlteringReadQuarantineSnapshot> Snapshot(
        IReadOnlyList<AlteringPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        return plans.Where(x => _entries.ContainsKey(x.FacilityName) &&
                           MultiAlteringBatchStore.Key(x) == _entries[x.FacilityName].Recipe)
            .Select(x => ToSnapshot(x.FacilityName, x.DisplayName,
                _entries[x.FacilityName]))
            .GroupBy(x => x.Facility, StringComparer.Ordinal)
            .Select(x => x.First()).Take(8).ToArray();
    }
}

// A bounded, sanitized local incident for the existing AI-assisted *review*
// pipeline. It NEVER requests Codex, merges patches or replays game input.
internal static class MultiAlteringReadQuarantineAudit
{
    internal static string Write(
        string directory, MultiAlteringReadQuarantineSnapshot record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(record);
        if (!AlteringPlan.Facilities.Contains(record.Facility, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(record.Recipe) || record.Recipe.Length > 120 ||
            record.Operation.Length > 40 || record.ConsecutiveFailures <= 0)
            throw new InvalidDataException("Invalid quarantine diagnostic");
        Directory.CreateDirectory(directory);
        string content = JsonSerializer.Serialize(new
        {
            schema = 1,
            mode = "multi-altering",
            type = "safe_pre_input_read_quarantine",
            facility = record.Facility, recipe = record.Recipe,
            operation = record.Operation,
            category = "transient_read_exhausted",
            consecutiveFailures = record.ConsecutiveFailures,
            repeated = record.Repeated,
            verifiedReadRecovered = record.VerifiedRecovered,
            firstSeenUtc = record.FirstSeenUtc, lastSeenUtc = record.LastSeenUtc,
            nextReadUtc = record.NextReadUtc,
            inputPermittedByThisEvidence = false,
            codeFixApplied = false, gameValidated = false,
            autoFixReviewOnly = true
        }, new JsonSerializerOptions { WriteIndented = true });
        string path = Path.Combine(directory,
            "quarantine-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        string temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content, Encoding.UTF8);
            File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        try
        {
            foreach (var old in Directory.EnumerateFiles(directory, "quarantine-*.json")
                .OrderByDescending(x => x, StringComparer.Ordinal).Skip(20))
                File.Delete(old);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return path;
    }
}
