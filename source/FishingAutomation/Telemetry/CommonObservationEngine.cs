namespace FishingAutomation.Telemetry;

// These enums name our *diagnostic categories*, not decoded packet events,
// third-party API fields, or permission to control the game.
internal enum CommonObservationSource
{
    ReadOnlyCli = 1,
    VerifiedScreen = 2,
    NetworkMetadata = 3,
    MobilayOfflineExport = 4,
    LocalScheduler = 5
}

internal enum CommonObservationDomain
{
    Connection = 1,
    Activity = 2,
    Inventory = 3,
    Gathering = 4,
    Altering = 5,
    Crafting = 6,
    Fishing = 7,
    Combat = 8,
    Dungeon = 9,
    Scheduler = 10
}

internal enum CommonObservationState
{
    Connected = 1,
    Disconnected = 2,
    Active = 3,
    Idle = 4,
    InProgress = 5,
    Completed = 6,
    Changed = 7,
    Stable = 8,
    TrafficObserved = 9,
    NoFrames = 10,
    HistoricalSnapshotPresent = 11,
    Failed = 12
}

internal enum CommonEvidenceLevel
{
    TransportMetadataOnly = 1,
    HistoricalOfflineSummary = 2,
    ValidatedReadOnlyCli = 3,
    VerifiedScreen = 4,
    LocalRuntimeDiagnostic = 5
}

internal sealed record CommonObservation(
    CommonObservationDomain Domain,
    CommonObservationSource Source,
    CommonObservationState State,
    CommonEvidenceLevel Evidence,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed record CommonObservationAdmission(bool Accepted, string Status);

// This is diagnostic output only. No data or code path granting input authority
// exists here. All fields are bounded enums/counts, not user-provided strings.
internal sealed record CommonObservationAssessment(
    string Status, CommonObservationState? State,
    int SemanticSources, int SupportingSources,
    bool CanReplaceExistingAuthority = false,
    bool AuthorizesGameplay = false);

/// <summary>
/// Bounded, process-local read-only evidence reconciliation. Caller must
/// explicitly opt in and allowlist sources; no game interface, filesystem,
/// network socket, packet parser, OCR or input driver is opened or invoked.
/// Adapters have NOT been connected to this diagnostic gate.
/// </summary>
internal sealed class CommonObservationEngine
{
    private static readonly TimeSpan FutureSkew = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LiveAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LiveValidity = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExportAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan ExportValidity = TimeSpan.FromDays(14);

    private readonly object _gate = new();
    private readonly HashSet<CommonObservationSource> _allowedSources;
    private readonly Dictionary<(CommonObservationDomain, CommonObservationSource),
        CommonObservation> _latest = new();
    // Ordering persists after signal expiry until explicit Clear.
    private readonly Dictionary<(CommonObservationDomain, CommonObservationSource),
        long> _lastSequence = new();
    private readonly Dictionary<(CommonObservationDomain, CommonObservationSource),
        DateTimeOffset> _lastObserved = new();

    internal bool DiagnosticsEnabled { get; }

    internal CommonObservationEngine(
        bool diagnosticsEnabled = false,
        IEnumerable<CommonObservationSource>? allowedSources = null)
    {
        DiagnosticsEnabled = diagnosticsEnabled;
        _allowedSources = allowedSources is null ?
            new HashSet<CommonObservationSource>() :
            new HashSet<CommonObservationSource>(
                allowedSources.Where(Enum.IsDefined));
    }

    private static CommonEvidenceLevel ExpectedEvidence(CommonObservationSource source) =>
        source switch
        {
            CommonObservationSource.ReadOnlyCli => CommonEvidenceLevel.ValidatedReadOnlyCli,
            CommonObservationSource.VerifiedScreen => CommonEvidenceLevel.VerifiedScreen,
            CommonObservationSource.NetworkMetadata => CommonEvidenceLevel.TransportMetadataOnly,
            CommonObservationSource.MobilayOfflineExport => CommonEvidenceLevel.HistoricalOfflineSummary,
            CommonObservationSource.LocalScheduler => CommonEvidenceLevel.LocalRuntimeDiagnostic,
            _ => (CommonEvidenceLevel)0
        };

    private static bool IsWorkflow(CommonObservationDomain domain) =>
        domain is CommonObservationDomain.Gathering or
            CommonObservationDomain.Altering or
            CommonObservationDomain.Crafting or
            CommonObservationDomain.Fishing or
            CommonObservationDomain.Dungeon;

    /// <summary>
    /// Explicit source/domain/state matrix. For example, "no frames" can
    /// NEVER turn into "disconnected", and a historical export can NEVER
    /// assert an active or completed encounter.
    /// </summary>
    internal static bool IsSupported(CommonObservation observation)
    {
        if (!Enum.IsDefined(observation.Domain) ||
            !Enum.IsDefined(observation.Source) ||
            !Enum.IsDefined(observation.State) ||
            !Enum.IsDefined(observation.Evidence) ||
            observation.Evidence != ExpectedEvidence(observation.Source))
            return false;

        var domain = observation.Domain;
        var state = observation.State;
        return observation.Source switch
        {
            CommonObservationSource.NetworkMetadata =>
                domain == CommonObservationDomain.Connection &&
                state is CommonObservationState.TrafficObserved or
                    CommonObservationState.NoFrames,
            CommonObservationSource.MobilayOfflineExport =>
                domain == CommonObservationDomain.Combat &&
                state == CommonObservationState.HistoricalSnapshotPresent,
            CommonObservationSource.LocalScheduler =>
                domain == CommonObservationDomain.Scheduler &&
                state is CommonObservationState.InProgress or
                    CommonObservationState.Completed or
                    CommonObservationState.Failed,
            CommonObservationSource.ReadOnlyCli =>
                domain switch
                {
                    CommonObservationDomain.Connection =>
                        state is CommonObservationState.Connected or
                            CommonObservationState.Disconnected,
                    CommonObservationDomain.Activity =>
                        state is CommonObservationState.Active or
                            CommonObservationState.Idle,
                    CommonObservationDomain.Inventory =>
                        state is CommonObservationState.Changed or
                            CommonObservationState.Stable,
                    _ => IsWorkflow(domain) &&
                        domain != CommonObservationDomain.Fishing &&
                        domain != CommonObservationDomain.Dungeon &&
                        state is CommonObservationState.InProgress or
                            CommonObservationState.Completed
                },
            CommonObservationSource.VerifiedScreen =>
                domain switch
                {
                    CommonObservationDomain.Connection =>
                        state is CommonObservationState.Connected or
                            CommonObservationState.Disconnected,
                    CommonObservationDomain.Activity or
                    CommonObservationDomain.Combat =>
                        state is CommonObservationState.Active or
                            CommonObservationState.Idle,
                    CommonObservationDomain.Inventory =>
                        state is CommonObservationState.Changed or
                            CommonObservationState.Stable,
                    _ => IsWorkflow(domain) &&
                        state is CommonObservationState.InProgress or
                            CommonObservationState.Completed
                },
            _ => false
        };
    }

    internal CommonObservationAdmission TryObserve(
        CommonObservation? observation, DateTimeOffset nowUtc)
    {
        if (!DiagnosticsEnabled)
            return new(false, "disabled");
        if (observation is null)
            return new(false, "empty");
        if (!_allowedSources.Contains(observation.Source))
            return new(false, "source_not_allowed");
        if (!IsSupported(observation) || observation.Sequence <= 0)
            return new(false, "invalid_evidence");

        var observed = observation.ObservedAtUtc.ToUniversalTime();
        var expires = observation.ExpiresAtUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();
        bool historical = observation.Source == CommonObservationSource.MobilayOfflineExport;
        var maximumAge = historical ? ExportAge : LiveAge;
        var maximumValidity = historical ? ExportValidity : LiveValidity;

        if (expires <= observed || expires - observed > maximumValidity)
            return new(false, "invalid_validity");
        if (observed - now > FutureSkew)
            return new(false, "future");
        if (now - observed > maximumAge || expires <= now)
            return new(false, "stale");

        var key = (observation.Domain, observation.Source);
        lock (_gate)
        {
            if (_lastSequence.TryGetValue(key, out long prior) &&
                observation.Sequence <= prior)
                return new(false, "duplicate_or_reordered");
            if (_lastObserved.TryGetValue(key, out DateTimeOffset priorTime) &&
                observed < priorTime)
                return new(false, "older_timestamp");
            _latest[key] = observation;
            _lastSequence[key] = observation.Sequence;
            _lastObserved[key] = observed;
            return new(true, "accepted");
        }
    }

    /// <summary>
    /// For all domains, even confirmed agreement means "diagnostic only".
    /// Historical exports, raw frame counts and socket-owner candidates are
    /// explicitly excluded from semantic source votes.
    /// </summary>
    internal CommonObservationAssessment Evaluate(
        CommonObservationDomain domain, DateTimeOffset nowUtc)
    {
        if (!DiagnosticsEnabled)
            return new("disabled", null, 0, 0);
        if (!Enum.IsDefined(domain))
            return new("unknown_domain", null, 0, 0);

        lock (_gate)
        {
            var now = nowUtc.ToUniversalTime();
            var rows = _latest.Values
                .Where(x => x.Domain == domain &&
                    x.ExpiresAtUtc.ToUniversalTime() > now &&
                    (x.Source == CommonObservationSource.MobilayOfflineExport
                        ? now - x.ObservedAtUtc.ToUniversalTime() <= ExportAge
                        : now - x.ObservedAtUtc.ToUniversalTime() <= LiveAge))
                .ToArray();

            // Nonsemantic metadata remains visible as supporting evidence
            // but can never corroborate an actual game state.
            int supporting = rows.Count(x => x.Source is
                CommonObservationSource.NetworkMetadata or
                CommonObservationSource.MobilayOfflineExport);
            var live = rows.Where(x => x.Source is
                CommonObservationSource.ReadOnlyCli or
                CommonObservationSource.VerifiedScreen or
                CommonObservationSource.LocalScheduler).ToArray();

            if (live.Length == 0)
                return new(rows.Length == 0 ? "no_fresh_observation" :
                    "nonsemantic_only", null, 0, supporting);
            if (live.Select(x => x.State).Distinct().Skip(1).Any())
                return new("conflicting_sources", null, live.Length, supporting);
            return new(live.Length > 1 ? "independent_sources_agree" :
                "single_source_only", live[0].State, live.Length, supporting);
        }
    }

    /// <summary>Clears diagnostics, never game controls or automation status.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _latest.Clear();
            _lastSequence.Clear();
            _lastObserved.Clear();
        }
    }
}
