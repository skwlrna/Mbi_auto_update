namespace FishingAutomation.Telemetry;

/// <summary>
/// Normalized, read-only observations. These names are NOT game packet IDs or fields.
/// A future authorized adapter must independently establish their meaning.
/// </summary>
internal enum PacketObservationKind
{
    CombatActivityHint = 1,
    TargetHealthFractionHint = 2,
    EncounterEndedHint = 3
}

internal sealed record PacketObservation(
    PacketObservationKind Kind,
    string AdapterId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    double? Value = null);

internal sealed record PacketObservationAdmission(bool Accepted, string Reason);

/// <summary>
/// A bounded, in-memory diagnostic inbox for already-normalized signals.
/// Does not capture packets, open sockets, read credentials, log raw data,
/// press keys, or authorize ANY gameplay / clear / input decision.
/// Disabled by default; approved adapter IDs are supplied by application code,
/// never inferred from untrusted event content.
/// </summary>
internal sealed class PacketObservationInbox
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumValidity = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly HashSet<string> _allowedAdapters;
    private readonly Dictionary<(string, PacketObservationKind), long> _sequences = new();
    private readonly Dictionary<PacketObservationKind, PacketObservation> _latest = new();
    // Retain accepted ordering watermarks after a hint expires or is read.
    private readonly Dictionary<PacketObservationKind, DateTimeOffset> _lastObserved = new();

    internal bool DiagnosticsEnabled { get; }

    internal PacketObservationInbox(
        bool diagnosticsEnabled = false,
        IEnumerable<string>? allowedAdapters = null)
    {
        DiagnosticsEnabled = diagnosticsEnabled;
        _allowedAdapters = allowedAdapters is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(allowedAdapters, StringComparer.Ordinal);
        _allowedAdapters.RemoveWhere(id => string.IsNullOrWhiteSpace(id) || id.Length > 64);
    }

    internal PacketObservationAdmission TryObserve(
        PacketObservation? observation,
        DateTimeOffset nowUtc)
    {
        if (!DiagnosticsEnabled)
            return new(false, "disabled");
        if (observation is null)
            return new(false, "empty");
        if (string.IsNullOrWhiteSpace(observation.AdapterId) ||
            !_allowedAdapters.Contains(observation.AdapterId))
            return new(false, "unapproved_adapter");
        if (!Enum.IsDefined(observation.Kind) || observation.Sequence <= 0)
            return new(false, "invalid_metadata");
        if (observation.Kind == PacketObservationKind.TargetHealthFractionHint)
        {
            if (!observation.Value.HasValue ||
                !double.IsFinite(observation.Value.Value) ||
                observation.Value.Value < 0 || observation.Value.Value > 1)
                return new(false, "invalid_value");
        }
        else if (observation.Value.HasValue)
        {
            return new(false, "unexpected_value");
        }

        var observed = observation.ObservedAtUtc.ToUniversalTime();
        var expires = observation.ExpiresAtUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();
        if (expires <= observed || expires - observed > MaximumValidity)
            return new(false, "invalid_validity");
        if (observed - now > MaximumFutureSkew)
            return new(false, "future");
        if (now - observed > MaximumAge || expires <= now)
            return new(false, "stale");

        lock (_gate)
        {
            var key = (observation.AdapterId, observation.Kind);
            if (_sequences.TryGetValue(key, out long prior) &&
                observation.Sequence <= prior)
                return new(false, "duplicate_or_reordered");
            if (_lastObserved.TryGetValue(observation.Kind, out var lastObserved) &&
                observed < lastObserved)
                return new(false, "older_observation");

            _sequences[key] = observation.Sequence;
            _lastObserved[observation.Kind] = observed;
            _latest[observation.Kind] = observation;
            return new(true, "accepted");
        }
    }

    /// <summary>Diagnostic retrieval only; consumers MUST NOT treat this as action authority.</summary>
    internal PacketObservation? ReadFresh(PacketObservationKind kind, DateTimeOffset nowUtc)
    {
        if (!DiagnosticsEnabled || !Enum.IsDefined(kind))
            return null;
        var now = nowUtc.ToUniversalTime();
        lock (_gate)
        {
            if (!_latest.TryGetValue(kind, out var observation))
                return null;
            if (observation.ExpiresAtUtc.ToUniversalTime() <= now ||
                now - observation.ObservedAtUtc.ToUniversalTime() > MaximumAge)
            {
                _latest.Remove(kind);
                return null;
            }
            return observation;
        }
    }

    /// <summary>Call when a diagnostic session ends. Never changes game state.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _sequences.Clear();
            _lastObserved.Clear();
            _latest.Clear();
        }
    }
}
