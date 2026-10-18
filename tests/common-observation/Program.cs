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

// Stage 12: existing read-only CLI semantics, never action commands.
// Timeouts can be labeled disconnected in the existing CLI wrapper: do not
// interpret a timeout/error as a verified game disconnect.
Check(ReadOnlyCliObservationProjection.Connection("status", true, "connected", null)
    is { Status: "state_observed", Domain: CommonObservationDomain.Connection,
        State: CommonObservationState.Connected, AuthorizesGameplay: false },
    "verified status did not map to diagnostic connection");
Check(ReadOnlyCliObservationProjection.Connection("status", false, "disconnected",
    "disconnected") is { State: CommonObservationState.Disconnected },
    "explicit disconnected evidence not classified");
foreach (string reason in new[] { "timeout", "cli_unavailable", "invalid_json", "game_off" })
    Check(ReadOnlyCliObservationProjection.Connection("status", false,
        "disconnected", reason).State is null,
        "ambiguous connection failure became disconnection: " + reason);
Check(ReadOnlyCliObservationProjection.Connection("execute_altering",
    true, "connected", null).State is null,
    "CLI action response accepted by projector");

Check(ReadOnlyCliObservationProjection.Activity(true, false, false, false, false)
    is { State: CommonObservationState.Active,
        Domain: CommonObservationDomain.Activity },
    "auto travel not reported as activity diagnostic");
Check(ReadOnlyCliObservationProjection.Activity(false, false, false, true, false)
    is { State: CommonObservationState.InProgress,
        Domain: CommonObservationDomain.Gathering },
    "typed running-gathering signal lost");
Check(ReadOnlyCliObservationProjection.Activity(false, false, true, true, false)
    is { Status: "gathering_observed",
        State: CommonObservationState.InProgress,
        Domain: CommonObservationDomain.Gathering },
    "generic auto-play overrode a verified gathering activity");
Check(ReadOnlyCliObservationProjection.Activity(false, false, false, false, true)
    is { State: null, Status: "fishing_seen_unmapped" },
    "unverified fishing CLI signal promoted to gameplay state");
Check(ReadOnlyCliObservationProjection.Activity(false, false, false, false, false)
    is { State: null, Status: "activity_not_classified" },
    "absence of activity evidence promoted to idle");
Check(ReadOnlyCliObservationProjection.AlteringWorks(0, 0, 0, 0)
    is { State: null, Status: "empty_work_list_not_completion" },
    "empty works misclassified as job completion");
var runningWorks = ReadOnlyCliObservationProjection.AlteringWorks(2, 1, 1, 0);
Check(runningWorks is { Domain: CommonObservationDomain.Altering,
    State: CommonObservationState.InProgress,
    Count: 2, InProgress: 1, Completed: 1 },
    "running works aggregate incorrect");
var endedWorks = ReadOnlyCliObservationProjection.AlteringWorks(2, 0, 2, 0);
Check(endedWorks is { Status: "all_work_rows_completed_not_receipted",
    State: CommonObservationState.Completed, AuthorizesGameplay: false },
    "server rows mistaken for a received item");
Check(ReadOnlyCliObservationProjection.AlteringWorks(2, 0, 1, 1)
    is { State: null, Status: "mixed_or_pending_work_not_completion" },
    "pending work incorrectly marked finished");
Check(ReadOnlyCliObservationProjection.AlteringWorks(2, 3, 0, 0)
    is { State: null, Status: "invalid_work_counts" },
    "invalid aggregate not rejected");
Check(ReadOnlyCliObservationProjection.InventoryShape(true, true, 24)
    is { State: null, Count: 24, Status: "inventory_snapshot_only" },
    "single inventory read incorrectly established a quantity delta");
Check(ReadOnlyCliObservationProjection.InventoryShape(true, false, 0)
    .Status == "inventory_unavailable",
    "invalid inventory promoted to stable");

Check(MultiAlteringObservationAudit.Compare(
    CommonObservationState.Completed, runningWorks)
    is { Status: "local_complete_but_work_running",
        NeedsReview: true, AuthorizesGameplay: false },
    "local complete with active server work not flagged");
Check(MultiAlteringObservationAudit.Compare(
    CommonObservationState.InProgress, endedWorks)
    is { Status: "server_rows_completed_receipt_unknown",
        NeedsReview: true, AuthorizesGameplay: false },
    "unreceived server work rows automatically completed local process");
Check(MultiAlteringObservationAudit.Compare(
    CommonObservationState.InProgress, runningWorks)
    is { Status: "same_stage_diagnostic_only",
        NeedsReview: false, AuthorizesGameplay: false },
    "agreement authorized gameplay");
Check(MultiAlteringObservationAudit.Compare(
    CommonObservationState.Completed,
    ReadOnlyCliObservationProjection.AlteringWorks(0, 0, 0, 0))
    is { Status: "unknown_server_work_state", NeedsReview: true },
    "empty server work list incorrectly confirmed local completion");

var collectorEngine = new CommonObservationEngine(true,
    new[] { CommonObservationSource.ReadOnlyCli,
            CommonObservationSource.LocalScheduler });
int cliCalls = 0;
Task<CliDiagnosticProjection> FakeRead(CliDiagnosticQuery query, CancellationToken ct)
{
    cliCalls++;
    return Task.FromResult(query switch
    {
        CliDiagnosticQuery.Status =>
            ReadOnlyCliObservationProjection.Connection(
                "status", true, "connected", null),
        CliDiagnosticQuery.AlteringWorks => runningWorks,
        CliDiagnosticQuery.Items =>
            ReadOnlyCliObservationProjection.InventoryShape(true, true, 12),
        _ => new CliDiagnosticProjection("no_verified_gameplay_state")
    });
}
var disabledCollector = new ReadOnlyCliObservationCollector(
    collectorEngine, FakeRead, now: () => now);
Check((await disabledCollector.ReadOnceAsync(CliDiagnosticQuery.Status))
    .Status == "disabled" && cliCalls == 0,
    "disabled collector executed CLI");
var opted = new ReadOnlyCliObservationCollector(
    collectorEngine, FakeRead, explicitlyEnabled: true, now: () => now);
Check((await opted.ReadOnceAsync((CliDiagnosticQuery)999))
    .Status == "query_not_allowed" && cliCalls == 0,
    "arbitrary CLI query invoked");
var firstRead = await opted.ReadOnceAsync(CliDiagnosticQuery.Status);
Check(firstRead is { Status: "state_observed" } &&
    firstRead.Admission?.Accepted == true &&
    firstRead.Assessment is { Status: "single_source_only",
        State: CommonObservationState.Connected,
        AuthorizesGameplay: false } && cliCalls == 1,
    "one-shot status read failed");
var inventoryRead = await opted.ReadOnceAsync(CliDiagnosticQuery.Items);
Check(inventoryRead is { Status: "inventory_snapshot_only",
    Admission: null, Assessment: null } &&
    collectorEngine.Evaluate(CommonObservationDomain.Inventory, now).State is null,
    "inventory snapshot caused automatic stable/changed judgment");
var workRead = await opted.ReadOnceAsync(CliDiagnosticQuery.AlteringWorks);
Check(workRead.Admission?.Accepted == true &&
    collectorEngine.Evaluate(CommonObservationDomain.Altering, now)
        is { State: CommonObservationState.InProgress, AuthorizesGameplay: false },
    "real query projection failed to enter common observation");
Check(opted.CompareMultiAltering(CommonObservationState.Completed, workRead)
    is { Status: "local_complete_but_work_running", AuthorizesGameplay: false },
    "multi-altering mismatch not surfaced");
Check(opted.CompareMultiAltering(null, workRead).NeedsReview,
    "missing local stage auto-passed");
Check(opted.ObserveSchedulerRunning().Accepted &&
    collectorEngine.Evaluate(CommonObservationDomain.Scheduler, now)
        is { Status: "single_source_only",
            State: CommonObservationState.InProgress, AuthorizesGameplay: false },
    "scheduler runtime observation not recorded as diagnostic");

var noLocalSource = new CommonObservationEngine(true,
    new[] { CommonObservationSource.ReadOnlyCli });
var deniedScheduler = new ReadOnlyCliObservationCollector(
    noLocalSource, FakeRead, explicitlyEnabled: true, now: () => now);
Check(deniedScheduler.ObserveSchedulerRunning().Status == "source_not_allowed",
    "scheduler evidence bypassed allowlist");

var unavailable = new ReadOnlyCliObservationCollector(collectorEngine,
    (q, ct) => throw new IOException("PRIVATE_PATH"),
    explicitlyEnabled: true, now: () => now);
var unavailableResult = await unavailable.ReadOnceAsync(CliDiagnosticQuery.Status);
Check(unavailableResult is { Status: "cli_query_unavailable", Admission: null },
    "CLI failure leaked private content or invented connection");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await opted.ReadOnceAsync(CliDiagnosticQuery.Status, cancelled.Token);
    throw new InvalidOperationException("cancelled query executed");
}
catch (OperationCanceledException) { checks++; }

foreach (string secret in new[] { "PRIVATE_PATH", "account", "character", "token" })
    Check(!unavailableResult.ToString().Contains(secret,
        StringComparison.OrdinalIgnoreCase),
        "diagnostic output leaked user data");

// Stage 13: opt-in UI gate is checked by the same pure method used on the
// button click; synthetic tests cannot trigger a real CLI query.
Check(CommonObservationManualUiPolicy.Evaluate(false, false, false, false, false)
    == "explicit_click_required", "startup/timer diagnostic unexpectedly permitted");
Check(CommonObservationManualUiPolicy.Evaluate(true, true, false, false, false)
    == "automation_busy", "running multi-altering allowed competing CLI query");
Check(CommonObservationManualUiPolicy.Evaluate(true, false, true, false, false)
    == "automation_busy", "F9 startup and manual diagnostic can race");
Check(CommonObservationManualUiPolicy.Evaluate(true, false, false, true, false)
    == "automation_busy", "existing production CLI poll allowed concurrent read");
Check(CommonObservationManualUiPolicy.Evaluate(true, false, false, false, true)
    == "diagnostic_already_running", "parallel diagnostics enabled");
Check(CommonObservationManualUiPolicy.Evaluate(true, false, false, false, false)
    == "allowed", "explicit one-click diagnostics blocked");

var readable = CommonObservationDiagnosticReport.Render(
    CliDiagnosticQuery.AlteringWorks, workRead);
Check(readable.Contains("가공", StringComparison.Ordinal) &&
    readable.Contains("진행", StringComparison.Ordinal) &&
    !readable.Contains("execute_", StringComparison.Ordinal),
    "fixed-label UI projection missing or exposed game action");

var malicious = new CliDiagnosticReadResult(
    "SECRET_CHAR_0987",
    new CliDiagnosticProjection("SECRET_RAW_RESPONSE", Count: 2222),
    null, null);
string rendered = CommonObservationDiagnosticReport.Render(
    CliDiagnosticQuery.Items, malicious);
Check(!rendered.Contains("SECRET_", StringComparison.Ordinal) &&
    rendered.Contains("검증되지 않은 상태", StringComparison.Ordinal),
    "unknown status or private label leaked to diagnostic UI");
Check(CommonObservationDiagnosticReport.StatusName("PRIVATE_PATH_PASSWORD") ==
    "검증되지 않은 상태", "private status reflected into UI");
Check(CommonObservationDiagnosticReport.ComparisonName("PRIVATE_PATH_PASSWORD") ==
    "단계 비교 보류", "private comparison reason reflected");
Check(CommonObservationDiagnosticReport.QueryName((CliDiagnosticQuery)999) ==
    "알 수 없는 조회", "unknown CLI query displayed raw value");
Check(CommonObservationDiagnosticReport.ObservationName(
    CommonObservationState.Completed).Contains("수령 미확인", StringComparison.Ordinal),
    "completed CLI row displayed as confirmed receipt");
Check(CommonObservationDiagnosticReport.ComparisonName(
    "local_complete_but_work_running").StartsWith("불일치:", StringComparison.Ordinal),
    "actual multi-altering discrepancy not displayed");
Check(!rendered.Contains("character", StringComparison.OrdinalIgnoreCase) &&
    !rendered.Contains("account", StringComparison.OrdinalIgnoreCase),
    "diagnostic UI incorrectly displays user identifiers");

// Stage 14: AI watcher-consented passive error evidence is built ONLY from
// results the normal read-only CLI already delivered. It never reads a game.
AiPassiveCliEvidence.SetEnabled(false);
AiPassiveCliEvidence.Observe("status", true, "connected", null, now);
Check(!AiPassiveCliEvidence.Enabled &&
    AiPassiveCliEvidence.SnapshotForError(now) is null,
    "passive CLI evidence leaked while AI watcher disabled");

AiPassiveCliEvidence.SetEnabled(true);
Check(AiPassiveCliEvidence.Enabled &&
    AiPassiveCliEvidence.SnapshotForError(now) is null,
    "empty opt-in session fabricated evidence");
AiPassiveCliEvidence.Observe("execute_altering", true, "connected", null, now);
AiPassiveCliEvidence.Observe("get_my_info", true, "connected", null, now);
AiPassiveCliEvidence.Observe("get_currencies", true, "connected", null, now);
Check(AiPassiveCliEvidence.SnapshotForError(now) is null,
    "action or private-identity query was admitted");

AiPassiveCliEvidence.Observe("status", true, "connected", null, now);
AiPassiveCliEvidence.Observe("get_altering_works", false, "disconnected",
    "timeout", now.AddMilliseconds(-100));
AiPassiveCliEvidence.Observe("get_items", true, "connected",
    "USER_PRIVATE_TOKEN", now.AddMilliseconds(-500));
AiPassiveCliEvidence.Observe("get_activity", false, "disconnected",
    "cli_unavailable", now.AddMilliseconds(-250));
string? evidence = AiPassiveCliEvidence.SnapshotForError(now);
Check(evidence is not null &&
    evidence.Contains("status=cli_connected;age_ms=0", StringComparison.Ordinal) &&
    evidence.Contains("get_altering_works=cli_timeout_state_unknown;age_ms=100",
        StringComparison.Ordinal) &&
    evidence.Contains("get_activity=cli_unavailable_state_unknown;age_ms=250",
        StringComparison.Ordinal) &&
    evidence.Contains("get_items=cli_response_accepted_not_game_event;age_ms=500",
        StringComparison.Ordinal),
    "recent existing CLI outcomes did not produce bounded evidence");
Check(evidence!.Split('\n').Length <= 8,
    "AI context length exceeds bounded per-query source lines");
foreach (string secret in new[] {
    "USER_PRIVATE_TOKEN", "execute_altering", "get_my_info",
    "get_currencies", "character", "username", "password",
    "C:\\Users", "github_pat_", "sk-"
})
    Check(!evidence.Contains(secret, StringComparison.OrdinalIgnoreCase),
        "private name/token/action leaked into AI evidence: " + secret);

Check(!evidence.Contains("confirmed_game_event", StringComparison.Ordinal) &&
    evidence.Contains("not decoded packet data or permission to act",
        StringComparison.Ordinal),
    "passive metadata presented as a decoded game event");

AiPassiveCliEvidence.Observe("get_alterable_items", false, "error",
    "PRIVATE_ERROR_MESSAGE", now.AddMilliseconds(-50));
Check(AiPassiveCliEvidence.SnapshotForError(now)!.Contains(
    "get_alterable_items=cli_failed_unknown;age_ms=50", StringComparison.Ordinal),
    "unknown CLI error was echoed or classified as success");
AiPassiveCliEvidence.Observe("status", false, "disconnected",
    "disconnected", now.AddMilliseconds(50));
Check(AiPassiveCliEvidence.SnapshotForError(now)!.Contains(
    "status=cli_connected;age_ms=0", StringComparison.Ordinal),
    "post-error asynchronous query was included in an earlier error record");
Check(!AiPassiveCliEvidence.SnapshotForError(now)!.Contains(
    "cli_explicit_disconnect", StringComparison.Ordinal),
    "post-error evidence contaminated previous snapshot");
AiPassiveCliEvidence.Observe("status", true, "connected", null,
    now.AddMilliseconds(-100));
Check(AiPassiveCliEvidence.SnapshotForError(now.AddMilliseconds(100))!
    .Contains("status=cli_explicit_disconnect;age_ms=50",
        StringComparison.Ordinal),
    "out-of-order finished queries incorrectly replaced newer evidence");
Check(AiPassiveCliEvidence.SnapshotForError(now.AddSeconds(6)) is null,
    "stale CLI evidence was attached beyond 5 second TTL");
AiPassiveCliEvidence.SetEnabled(false);
Check(AiPassiveCliEvidence.SnapshotForError(now) is null,
    "stopping AI watcher did not immediately purge evidence");
AiPassiveCliEvidence.SetEnabled(true);
Check(AiPassiveCliEvidence.SnapshotForError(now) is null,
    "new watcher session inherited old CLI evidence");
AiPassiveCliEvidence.Observe("get_activity", false, "disconnected",
    "game_off", now);
Check(AiPassiveCliEvidence.SnapshotForError(now)!.Contains(
    "get_activity=cli_reports_game_off;age_ms=0", StringComparison.Ordinal),
    "documented game_off state was not distinguished");
AiPassiveCliEvidence.SetEnabled(false);

Console.WriteLine($"PASS {checks} synthetic common observation and historical-DPS regressions.");
Console.WriteLine("No game input, capture, external API, user export or private data was opened.");
