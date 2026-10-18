using System.Text;
using FishingAutomation.Telemetry;

int checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new InvalidOperationException(description);
    checks++;
}

var now = new DateTimeOffset(2026, 10, 18, 7, 30, 0, TimeSpan.Zero);
CommonObservation Sample(
    CommonObservationSource source = CommonObservationSource.ReadOnlyCli,
    CommonObservationDomain domain = CommonObservationDomain.Connection,
    CommonObservationState state = CommonObservationState.Connected,
    long sequence = 1, DateTimeOffset? observed = null,
    DateTimeOffset? expires = null, CommonEvidenceLevel? evidence = null)
{
    var at = observed ?? now;
    return new(domain, source, state,
        evidence ?? (source switch
        {
            CommonObservationSource.ReadOnlyCli => CommonEvidenceLevel.ValidatedReadOnlyCli,
            CommonObservationSource.VerifiedScreen => CommonEvidenceLevel.VerifiedScreen,
            CommonObservationSource.NetworkMetadata => CommonEvidenceLevel.TransportMetadataOnly,
            CommonObservationSource.MobilayOfflineExport => CommonEvidenceLevel.HistoricalOfflineSummary,
            CommonObservationSource.LocalScheduler => CommonEvidenceLevel.LocalRuntimeDiagnostic,
            _ => (CommonEvidenceLevel)999
        }),
        sequence, at, expires ?? at.AddSeconds(4));
}

var disabled = new CommonObservationEngine(allowedSources:
    new[] { CommonObservationSource.ReadOnlyCli });
Check(!disabled.DiagnosticsEnabled, "default enabled");
Check(disabled.TryObserve(Sample(), now).Status == "disabled",
    "default engine accepted a state");
Check(disabled.Evaluate(CommonObservationDomain.Connection, now).Status == "disabled",
    "default engine returned a live state");

var noSources = new CommonObservationEngine(true);
Check(noSources.TryObserve(Sample(), now).Status == "source_not_allowed",
    "engine ran without source allowlist");

var engine = new CommonObservationEngine(true,
    Enum.GetValues<CommonObservationSource>());
var admitted = engine.TryObserve(Sample(), now);
Check(admitted.Accepted && admitted.Status == "accepted",
    "valid read-only CLI state not admitted");
var onlyCli = engine.Evaluate(CommonObservationDomain.Connection, now);
Check(onlyCli is { Status: "single_source_only",
    State: CommonObservationState.Connected,
    SemanticSources: 1, SupportingSources: 0,
    CanReplaceExistingAuthority: false, AuthorizesGameplay: false },
    "single CLI report granted gameplay authority");

var networkNoFrames = Sample(CommonObservationSource.NetworkMetadata,
    state: CommonObservationState.NoFrames);
Check(engine.TryObserve(networkNoFrames, now).Accepted,
    "network metadata should be a nonsemantic source");
var cliPlusNoFrames = engine.Evaluate(CommonObservationDomain.Connection, now);
Check(cliPlusNoFrames is { Status: "single_source_only",
    State: CommonObservationState.Connected,
    SemanticSources: 1, SupportingSources: 1, AuthorizesGameplay: false },
    "no packet frames wrongly negated read-only CLI");
Check(!CommonObservationEngine.IsSupported(
    Sample(CommonObservationSource.NetworkMetadata,
        state: CommonObservationState.Disconnected)),
    "packet count was allowed to impersonate disconnection");

Check(engine.TryObserve(Sample(CommonObservationSource.VerifiedScreen,
    state: CommonObservationState.Connected), now).Accepted,
    "verified screen state rejected");
var agreement = engine.Evaluate(CommonObservationDomain.Connection, now);
Check(agreement is { Status: "independent_sources_agree",
    State: CommonObservationState.Connected, SemanticSources: 2,
    SupportingSources: 1, CanReplaceExistingAuthority: false,
    AuthorizesGameplay: false }, "diagnostic agreement authorized gameplay");
Check(engine.TryObserve(Sample(CommonObservationSource.VerifiedScreen,
    state: CommonObservationState.Disconnected, sequence: 2,
    observed: now.AddMilliseconds(100)), now).Accepted,
    "contradictory screen transition rejected");
var conflict = engine.Evaluate(CommonObservationDomain.Connection, now);
Check(conflict is { Status: "conflicting_sources", State: null,
    SemanticSources: 2, SupportingSources: 1,
    CanReplaceExistingAuthority: false, AuthorizesGameplay: false },
    "conflicting independent source reports were silently reconciled");

Check(engine.TryObserve(Sample(), now).Status == "duplicate_or_reordered",
    "replay sequence accepted");
Check(engine.TryObserve(Sample(sequence: 0), now).Status == "invalid_evidence",
    "zero sequence accepted");
Check(engine.TryObserve(Sample(sequence: 2,
    observed: now.AddSeconds(2)), now).Status == "future",
    "future state admitted");
Check(engine.TryObserve(Sample(sequence: 2,
    observed: now.AddSeconds(-6), expires: now.AddSeconds(2)), now).Status ==
    "stale", "old CLI state admitted");
Check(engine.TryObserve(Sample(sequence: 2, expires: now), now).Status ==
    "invalid_validity", "invalid expiry admitted");
Check(engine.TryObserve(Sample(sequence: 2,
    expires: now.AddSeconds(11)), now).Status == "invalid_validity",
    "overlong live TTL accepted");
Check(engine.TryObserve(Sample(sequence: 2,
    evidence: CommonEvidenceLevel.VerifiedScreen), now).Status == "invalid_evidence",
    "CLI claimed verified screen authority");
Check(engine.TryObserve(Sample(
    source: (CommonObservationSource)900), now).Status == "source_not_allowed",
    "unsupported source bypassed allowlist");
Check(engine.TryObserve(null, now).Status == "empty", "null observation accepted");
Check(engine.TryObserve(Sample(sequence: 2, observed: now.AddMilliseconds(-1)),
    now).Status == "older_timestamp", "older timestamp replaced current state");

var gone = engine.Evaluate(CommonObservationDomain.Connection,
    now.AddSeconds(6));
Check(gone is { Status: "no_fresh_observation", State: null,
    SemanticSources: 0, SupportingSources: 0 },
    "expired states persisted as current");
Check(engine.TryObserve(Sample(sequence: 1), now).Status == "duplicate_or_reordered",
    "expired state lost its replay watermark");
engine.Clear();
Check(engine.TryObserve(Sample(), now).Accepted &&
    engine.Evaluate(CommonObservationDomain.Connection, now).Status ==
        "single_source_only",
    "clear did not reset diagnostics and replay watermarks");

// The offline export's SavedAt can be historical. Even a plausible export
// is never a LIVE combat/room/loot/character-state vote.
var historicAt = now.AddDays(-1);
var snapshot = Sample(CommonObservationSource.MobilayOfflineExport,
    CommonObservationDomain.Combat,
    CommonObservationState.HistoricalSnapshotPresent,
    observed: historicAt, expires: historicAt.AddDays(5));
Check(engine.TryObserve(snapshot, now).Accepted,
    "legitimate older offline historical summary rejected");
var offlineOnly = engine.Evaluate(CommonObservationDomain.Combat, now);
Check(offlineOnly is { Status: "nonsemantic_only", State: null,
    SemanticSources: 0, SupportingSources: 1, AuthorizesGameplay: false },
    "historical export masqueraded as current combat status");
Check(!CommonObservationEngine.IsSupported(Sample(
    CommonObservationSource.MobilayOfflineExport,
    CommonObservationDomain.Dungeon, CommonObservationState.Completed)),
    "offline export was allowed to verify dungeon completion");
Check(!CommonObservationEngine.IsSupported(Sample(
    CommonObservationSource.ReadOnlyCli,
    CommonObservationDomain.Combat, CommonObservationState.Active)),
    "existing CLI incorrectly claims an undocumented combat query");
Check(!CommonObservationEngine.IsSupported(Sample(
    CommonObservationSource.ReadOnlyCli,
    CommonObservationDomain.Fishing, CommonObservationState.InProgress)),
    "existing CLI incorrectly claims a fishing query");
Check(!CommonObservationEngine.IsSupported(Sample(
    CommonObservationSource.VerifiedScreen,
    CommonObservationDomain.Connection, CommonObservationState.TrafficObserved)),
    "screen claimed network traffic authority");
Check(engine.Evaluate((CommonObservationDomain)999, now).Status ==
    "unknown_domain", "unknown domain produced a state");

var gathering = Sample(CommonObservationSource.ReadOnlyCli,
    CommonObservationDomain.Gathering, CommonObservationState.InProgress);
Check(engine.TryObserve(gathering, now).Accepted &&
    engine.Evaluate(CommonObservationDomain.Gathering, now)
        is { Status: "single_source_only", AuthorizesGameplay: false },
    "gathering diagnostic incorrectly granted an action");
Check(engine.TryObserve(Sample(CommonObservationSource.VerifiedScreen,
    CommonObservationDomain.Gathering, CommonObservationState.Completed), now)
    .Accepted &&
    engine.Evaluate(CommonObservationDomain.Gathering, now)
        is { Status: "conflicting_sources", State: null },
    "gathering conflict caused automatic completion");
Check(engine.TryObserve(Sample(CommonObservationSource.LocalScheduler,
    CommonObservationDomain.Scheduler, CommonObservationState.Failed), now)
    .Accepted && engine.Evaluate(CommonObservationDomain.Scheduler, now)
        is { Status: "single_source_only", State: CommonObservationState.Failed,
            AuthorizesGameplay: false },
    "local failure diagnosis authorized automatic retry");

string ValidDps = """
{"Version":4,"SavedAt":"2026-10-18T17:33:26+09:00","BattleSeconds":48.749,
"TotalDamage":27394216,"EventCount":824,"RetainedEventCount":824,
"DroppedEventCount":0,"IsEventHistoryTrimmed":false,
"UploaderUserId":12345678,"UploaderUserName":"PRIVATE",
"MapMembersText":"PRIVATE_PARTY",
"CompactData":{"st":"2026-10-18T17:32:13+09:00","kt":["Secret skill"],
"bt":["Secret buff"],"s":[{"s":0},{"s":1},{"s":47}]},
"Events":[]}
""";
MobilayDpsSummaryValidator.Result Inspect(string json) =>
    MobilayDpsSummaryValidator.Inspect(Encoding.UTF8.GetBytes(json));
var valid = Inspect(ValidDps);
Check(valid is { Status: "offline_summary_shape_only",
    FormatVersion: 4, EventCount: 824, RetainedCount: 824,
    DroppedCount: 0, SecondBuckets: 3,
    IsRealtime: false, SemanticsVerified: false, GameplayAuthorized: false },
    "observed DPS v4 summary was mistaken for replayable live events");
foreach (string secret in new[] { "PRIVATE", "PRIVATE_PARTY", "12345678",
    "Secret skill", "Secret buff", "UploaderUserId" })
    Check(!valid.ToString().Contains(secret, StringComparison.Ordinal),
        "offline summary leaked private content");
Check(Inspect(ValidDps.Replace("\"Version\":4", "\"Version\":5"))
    .Status == "unsupported_version", "unknown export version accepted");
Check(Inspect(ValidDps.Replace("\"EventCount\":824",
    "\"EventCount\":-1")).Status == "invalid_summary",
    "negative event count accepted");
Check(Inspect(ValidDps.Replace("\"BattleSeconds\":48.749",
    "\"BattleSeconds\":-1")).Status == "invalid_summary",
    "negative elapsed seconds accepted");
Check(Inspect(ValidDps.Replace("\"TotalDamage\":27394216",
    "\"TotalDamage\":-1")).Status == "invalid_summary",
    "negative damage accepted");
Check(Inspect(ValidDps.Replace("\"s\":[{\"s\":0},{\"s\":1},{\"s\":47}]",
    "\"s\":[{\"s\":0},{\"s\":0}]")).Status == "invalid_summary",
    "nonincreasing DPS second buckets accepted");
Check(Inspect(ValidDps.Replace("\"Version\":4", "\"Version\":4,\"Version\":4"))
    .Status == "invalid_summary", "duplicate root field accepted");
Check(Inspect(ValidDps.Replace("\"st\":", "\"s\":[],\"st\":"))
    .Status == "invalid_summary", "duplicate CompactData key accepted");
Check(Inspect(ValidDps.Replace("\"SavedAt\":\"2026-10-18T17:33:26+09:00\"",
    "\"SavedAt\":\"not-a-date\"")).Status == "invalid_summary",
    "invalid historical timestamp accepted");
Check(Inspect("{}").Status == "invalid_summary",
    "empty JSON accepted as v4 export");
Check(Inspect("{bad").Status == "invalid_json",
    "malformed JSON accepted");
Check(MobilayDpsSummaryValidator.Inspect(Array.Empty<byte>()).Status == "empty",
    "empty export accepted");
Check(MobilayDpsSummaryValidator.Inspect(
    new byte[MobilayDpsSummaryValidator.MaximumBytes + 1]).Status == "too_large",
    "oversized export accepted");
Check(Inspect("622417 00 00 50 3B").Status == "invalid_json",
    "hex CharacterInfo record was treated as a decoded export");

Console.WriteLine($"PASS {checks} synthetic common observation and historical-DPS regressions.");
Console.WriteLine("No game input, capture, external API, user export or private data was opened.");
