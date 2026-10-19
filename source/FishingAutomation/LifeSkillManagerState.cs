using System.Net;

namespace FishingAutomation;

// Only transient failures of confirmed read-only CLI queries may be retried.
// State conflicts, material shortages and uncertain GUI actions are not recoverable
// by reissuing a click, claim, craft, stop or registration.
internal static class LifeSkillReadRecoveryPolicy
{
    internal static bool IsTransient(Exception error) => error switch
    {
        TimeoutException => true,
        IOException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout } => true,
        _ => false
    };

    internal static string Category(Exception error) => error switch
    {
        OperationCanceledException => "cancelled",
        InvalidDataException => "invalid_cli_data",
        InvalidOperationException => "state_conflict",
        _ when IsTransient(error) => "transient_read",
        _ => "unclassified_no_retry"
    };
}


// V3.2.36: deterministic, evidence-limited recovery decisions shared by
// altering, gathering and crafting. Previous AI patches and diagnostic
// similarity are NEVER permission for a GUI action or an extra CLI retry.
internal enum LifeSkillRecoveryDisposition { RetryReadOnlyOnce, ContinueAfterVerifiedRead, SafeStopAndDiagnose, Cancel }
internal sealed record LifeSkillRecoveryDecision(
    string Category, LifeSkillRecoveryDisposition Disposition);

internal static class LifeSkillRecoveryDecisionPolicy
{
    internal static LifeSkillRecoveryDecision ForReadFailure(
        Exception error, int attempt, bool cancellationRequested = false)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (cancellationRequested || error is OperationCanceledException)
            return new("cancelled", LifeSkillRecoveryDisposition.Cancel);
        if (LifeSkillReadRecoveryPolicy.IsTransient(error))
            return attempt == 0
                ? new("transient_read", LifeSkillRecoveryDisposition.RetryReadOnlyOnce)
                : new("transient_read_exhausted", LifeSkillRecoveryDisposition.SafeStopAndDiagnose);
        return new(LifeSkillReadRecoveryPolicy.Category(error),
            LifeSkillRecoveryDisposition.SafeStopAndDiagnose);
    }

    internal static LifeSkillRecoveryDecision ForUncertainAction()
        => new("unconfirmed_action", LifeSkillRecoveryDisposition.SafeStopAndDiagnose);

    internal static LifeSkillRecoveryDecision ForIncident(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        // An incident is reached after the active stage failed. Even a
        // transient read at this point cannot authorize retrying F9/F05.
        var category = error switch
        {
            OperationCanceledException => "cancelled",
            _ when LifeSkillReadRecoveryPolicy.IsTransient(error) => "transient_read_exhausted",
            _ => LifeSkillReadRecoveryPolicy.Category(error)
        };
        return new(category, error is OperationCanceledException
            ? LifeSkillRecoveryDisposition.Cancel
            : LifeSkillRecoveryDisposition.SafeStopAndDiagnose);
    }
}

// Bounded, local session metadata. It has no reference to user IDs,
// packet bytes, screen coordinates or full exception messages. It is diagnostic
// input for existing incident review, not a learned game-action policy.
internal sealed record LifeSkillRecoveryPattern(
    LifeSkillStage Stage, string Operation, string Category,
    LifeSkillRecoveryDisposition Disposition, int Occurrences);

internal sealed class LifeSkillRecoveryJournal
{
    private const int MaxPatterns = 24;
    private readonly object _gate = new();
    private readonly List<LifeSkillRecoveryPattern> _patterns = [];

    internal LifeSkillRecoveryPattern Note(
        LifeSkillStage stage, string operation, LifeSkillRecoveryDecision decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(decision);
        // Operation is a small internal read tag, not untrusted error text.
        string tag = operation.Length <= 40 &&
            operation.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? operation : "other";
        lock (_gate)
        {
            int index = _patterns.FindIndex(x => x.Stage == stage &&
                x.Operation == tag && x.Category == decision.Category &&
                x.Disposition == decision.Disposition);
            if (index >= 0)
            {
                var previous = _patterns[index];
                var updated = previous with
                {
                    Occurrences = Math.Min(1000000, previous.Occurrences + 1)
                };
                _patterns[index] = updated;
                return updated;
            }
            if (_patterns.Count == MaxPatterns) _patterns.RemoveAt(0);
            var entry = new LifeSkillRecoveryPattern(
                stage, tag, decision.Category, decision.Disposition, 1);
            _patterns.Add(entry);
            return entry;
        }
    }

    internal IReadOnlyList<LifeSkillRecoveryPattern> Snapshot()
    {
        lock (_gate) return _patterns.ToArray();
    }
}

// Per-run fence for non-idempotent screen operations. A CLI inventory read,
// cached observation, or a timeout NEVER authorizes replaying an uncertain action.
// This is deliberately not persisted: resuming F9 is governed by its durable
// batch/receipt ledger, not by these in-memory diagnostics.
internal sealed record LifeSkillWorkTicket(string Key, long Attempt);
internal sealed class LifeSkillWorkFence
{
    private enum Phase { InFlight, Verified, Uncertain }
    private sealed record Entry(long Attempt, Phase State);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _nextAttempt;

    internal event Action<string>? Log;

    internal LifeSkillWorkTicket Begin(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        LifeSkillWorkTicket ticket;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var previous) &&
                previous.State is Phase.InFlight or Phase.Uncertain)
                throw new InvalidOperationException(
                    $"[중간관리자][중복 차단] {key} · " +
                    (previous.State == Phase.InFlight
                        ? "동일 작업이 아직 진행 중이므로 중복 실행하지 않습니다."
                        : "이전 실행 결과가 미확정이므로 자동 재실행하지 않습니다."));
            ticket = new LifeSkillWorkTicket(key, ++_nextAttempt);
            _entries[key] = new Entry(ticket.Attempt, Phase.InFlight);
        }
        Log?.Invoke($"[중간관리자][작업 검증] {key} · 실행 구간 진입 · 중복 입력 차단");
        return ticket;
    }

    // Only use AFTER the existing screen completion proof and a fresh CLI
    // observation. Inventory alone is not proof that an action completed.
    internal void ConfirmInventoryIncrease(LifeSkillWorkTicket ticket,
        long before, long after, long minimumIncrease)
    {
        if (before < 0 || after < 0 || minimumIncrease <= 0 || after < before ||
            after - before < minimumIncrease)
            throw new InvalidOperationException(
                "[중간관리자][누락 감지] 작업 완료 후 재고 증가 근거가 부족합니다. 자동 재실행하지 않습니다.");
        Confirm(ticket, $"화면 완료 및 신규 CLI 재고 확인 · {before}→{after} · 최소 +{minimumIncrease}");
    }

    // The existing F9 durable registration/receipt state remains authoritative.
    // A successful coordinator return is only evidence that a batch invocation
    // ended; do not claim inventory or receipt confirmation from this method.
    internal void ConfirmBatchReturn(LifeSkillWorkTicket ticket,
        int confirmedRegistrations, bool terminal)
    {
        if (confirmedRegistrations < 0)
            throw new InvalidOperationException(
                "[중간관리자][누락 감지] 확정 등록 증가량이 음수입니다. 자동 재실행하지 않습니다.");
        Confirm(ticket, $"기존 F9 배치 반환 확인 · 확정 등록 +{confirmedRegistrations} · 종료={terminal} · 수령 확정은 기존 장부 기준");
    }

    private void Confirm(LifeSkillWorkTicket ticket, string evidence)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
        {
            if (!_entries.TryGetValue(ticket.Key, out var entry) ||
                entry.Attempt != ticket.Attempt || entry.State != Phase.InFlight)
                throw new InvalidOperationException(
                    "[중간관리자][중복 차단] 이전 작업 또는 미확정 작업의 늦은 검증을 폐기합니다.");
            _entries[ticket.Key] = entry with { State = Phase.Verified };
        }
        Log?.Invoke($"[중간관리자][작업 검증] {ticket.Key} · {evidence}");
    }

    internal void MarkUncertain(LifeSkillWorkTicket ticket, string category)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        bool changed = false;
        lock (_gate)
        {
            if (_entries.TryGetValue(ticket.Key, out var entry) &&
                entry.Attempt == ticket.Attempt && entry.State == Phase.InFlight)
            {
                _entries[ticket.Key] = entry with { State = Phase.Uncertain };
                changed = true;
            }
        }
        if (changed)
            Log?.Invoke($"[중간관리자][작업 미확정] {ticket.Key} · 분류={category} · 중복 실행 차단 · 기존 진단/안전 정지로 이관");
    }
}

// V3.2.34 bounded, diagnostic-only heartbeat for long unattended sessions.
// The clock is injectable for deterministic regression tests. A heartbeat
// never approves input and it never resets any existing no-progress watchdog.
internal sealed class LifeSkillLongRunHeartbeat
{
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _interval;
    private DateTimeOffset? _lastReport;
    private string? _lastPhase;
    private long? _lastQuantity;

    internal LifeSkillLongRunHeartbeat(
        TimeSpan? interval = null, Func<DateTimeOffset>? now = null)
    {
        _interval = interval ?? TimeSpan.FromMinutes(1);
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal bool ShouldReport(string phase, long confirmedQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        if (confirmedQuantity < 0)
            throw new InvalidDataException("장시간 진단 수량이 음수입니다.");
        DateTimeOffset observedAt = _now();
        TimeSpan elapsed = _lastReport.HasValue
            ? observedAt - _lastReport.Value
            : TimeSpan.MaxValue;
        bool report = !_lastReport.HasValue ||
            !string.Equals(_lastPhase, phase, StringComparison.Ordinal) ||
            !_lastQuantity.HasValue || _lastQuantity.Value != confirmedQuantity ||
            elapsed >= _interval || elapsed < TimeSpan.Zero;
        if (!report) return false;
        _lastReport = observedAt;
        _lastPhase = phase;
        _lastQuantity = confirmedQuantity;
        return true;
    }
}

// A real combat state is not permission to press Start/Stop or Space again.
// Two-second polling for 15 minutes without returning to the field is
// an anomalous state; hand it off to existing safe-stop diagnostics.
internal static class LifeSkillLongRunCombatPolicy
{
    internal const int MaximumConsecutivePolls = 450;

    internal static void ThrowIfStalled(int consecutivePolls, string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        if (consecutivePolls < MaximumConsecutivePolls) return;
        throw new InvalidOperationException(
            $"[중간관리자][장시간 정체] {phase} · 전투 상태가 " +
            $"{MaximumConsecutivePolls}회 연속 확인됨 · 화면 입력 없이 안전 정지");
    }
}

// Per-run observation ledger for the existing middle manager. Never authorizes
// GUI input; all action paths continue their own fresh CLI and visual checks.
internal enum LifeSkillStage { Altering, Gathering, Crafting }
internal sealed record LifeSkillCountEvidence(
    LifeSkillStage Stage, long Epoch, long Quantity, DateTimeOffset ObservedAtUtc);
// Diagnostics only. These counts are scoped to the active stage/epoch and
// are never used to grant movement, collection, registration or crafting input.
internal sealed record LifeSkillReadDiagnostics(
    long Requests, long Attempts, long Recovered, long Failed, long StaleInventoryReplies);

internal sealed class LifeSkillManagerState
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _ttl;
    private readonly Func<TimeSpan, CancellationToken, Task> _recoveryDelay;
    private readonly Dictionary<string, LifeSkillCountEvidence> _inventory =
        new(StringComparer.Ordinal);
    // Same-item concurrent reads must not commit in completion order: a slow
    // old reply can otherwise replace a newer and more accurate observation.
    private readonly Dictionary<string, long> _latestInventoryRead =
        new(StringComparer.Ordinal);
    private long _nextInventoryRead;
    private LifeSkillReadDiagnostics _readDiagnostics = new(0, 0, 0, 0, 0);
    private LifeSkillStage? _current;
    private long _epoch;
    internal event Action<string>? Log;
    internal LifeSkillWorkFence WorkFence { get; } = new();
    internal LifeSkillRecoveryJournal RecoveryJournal { get; } = new();

    internal LifeSkillManagerState(
        Func<DateTimeOffset>? now = null, TimeSpan? ttl = null,
        Func<TimeSpan, CancellationToken, Task>? recoveryDelay = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromSeconds(5);
        _recoveryDelay = recoveryDelay ?? Task.Delay;
        WorkFence.Log += message => Log?.Invoke(message);
        if (_ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
    }

    internal LifeSkillReadDiagnostics DiagnosticsSnapshot()
    {
        lock (_gate) return _readDiagnostics;
    }

    internal IReadOnlyList<LifeSkillRecoveryPattern> RecoveryPatternsSnapshot()
        => RecoveryJournal.Snapshot();

    // Call only after a non-idempotent screen operation became uncertain.
    // A history match or a recent inventory count may NEVER clear this flag.
    internal void RecordUncertainAction(LifeSkillStage stage, string operation)
    {
        NoteRecovery(stage, operation,
            LifeSkillRecoveryDecisionPolicy.ForUncertainAction());
    }

    internal void RecordIncident(LifeSkillStage stage, Exception error)
        => NoteRecovery(stage, "incident", LifeSkillRecoveryDecisionPolicy.ForIncident(error));

    private void NoteRecovery(LifeSkillStage stage, string operation,
        LifeSkillRecoveryDecision decision)
    {
        var observed = RecoveryJournal.Note(stage, operation, decision);
        // First occurrence and sparse repetition only. Do not flood UI during
        // overnight polling or misreport a prior offline patch as confirmed.
        if (observed.Occurrences is 1 or 3 or 10 ||
            (observed.Occurrences > 10 && observed.Occurrences % 50 == 0))
            Log?.Invoke($"[중간관리자][복구 판단] {stage}/{observed.Operation} · " +
                $"분류={observed.Category} · 판단={observed.Disposition} · " +
                $"반복={observed.Occurrences} · 과거 유사 사례는 실행 허가 아님");
    }

    private static string Summary(LifeSkillStage stage, LifeSkillReadDiagnostics diagnostics)
        => $"[중간관리자][조회 통계] {stage} · 요청 {diagnostics.Requests} · " +
            $"실제 CLI 시도 {diagnostics.Attempts} · 복구 {diagnostics.Recovered} · " +
            $"실패 {diagnostics.Failed} · 늦은 재고 응답 폐기 {diagnostics.StaleInventoryReplies} · 진단 전용";

    private void NoteRead(LifeSkillStage stage, long expectedEpoch,
        Func<LifeSkillReadDiagnostics, LifeSkillReadDiagnostics> update)
    {
        lock (_gate)
        {
            if (_current != stage || _epoch != expectedEpoch)
                throw new InvalidOperationException(
                    "중간관리자 조회 집계 도중 단계 변경 · 이전 CLI 결과 폐기");
            _readDiagnostics = update(_readDiagnostics);
        }
    }

    internal void BeginStage(LifeSkillStage stage, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        long epoch;
        string? previousSummary = null;
        lock (_gate)
        {
            if (_current.HasValue && _readDiagnostics.Requests > 0)
                previousSummary = Summary(_current.Value, _readDiagnostics);
            _current = stage;
            epoch = ++_epoch;
            _inventory.Clear();
            _latestInventoryRead.Clear();
            _readDiagnostics = new(0, 0, 0, 0, 0);
        }
        if (previousSummary is not null) Log?.Invoke(previousSummary);
        Log?.Invoke($"[중간관리자] 단계 전환 {stage} · epoch={epoch} · " +
            $"기존 재고 관측 무효화 · 새로운 CLI 재조회 · {reason}");
    }

    internal void Invalidate(string reason)
    {
        string? previousSummary = null;
        lock (_gate)
        {
            if (_current.HasValue && _readDiagnostics.Requests > 0)
                previousSummary = Summary(_current.Value, _readDiagnostics);
            _current = null;
            _epoch++;
            _inventory.Clear();
            _latestInventoryRead.Clear();
            _readDiagnostics = new(0, 0, 0, 0, 0);
        }
        if (previousSummary is not null) Log?.Invoke(previousSummary);
        Log?.Invoke("[중간관리자] 상태 미확정 · 재고 관측 무효화 · " + reason);
    }

    private long CaptureEpoch(LifeSkillStage stage)
    {
        lock (_gate)
        {
            if (_current != stage)
                throw new InvalidOperationException(
                    "중간관리자 단계 불일치 · 다른 작업 상태로 CLI 조회/입력을 진행하지 않습니다.");
            return _epoch;
        }
    }

    private void AssertEpoch(LifeSkillStage stage, long expected)
    {
        lock (_gate)
        {
            if (_current != stage || _epoch != expected)
                throw new InvalidOperationException(
                    "중간관리자 조회 도중 작업 단계가 변경돼 늦은 CLI 응답/재시도를 폐기합니다.");
        }
    }

    // V3.2.32 shared bounded recovery. Only call this for side-effect-free
    // CLI reads. Do not wrap screen reads (which may manipulate UI), actions,
    // F05 receipts, batch registration, writes or an entire stage.
    internal async Task<T> ReadWithRecoveryAsync<T>(
        LifeSkillStage stage, string operation,
        Func<CancellationToken, Task<T>> readOnly, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(readOnly);
        ct.ThrowIfCancellationRequested();
        long epoch = CaptureEpoch(stage);
        NoteRead(stage, epoch, d => d with { Requests = d.Requests + 1 });
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            AssertEpoch(stage, epoch);
            NoteRead(stage, epoch, d => d with { Attempts = d.Attempts + 1 });
            try
            {
                T result = await readOnly(ct);
                ct.ThrowIfCancellationRequested();
                AssertEpoch(stage, epoch);
                if (attempt > 0)
                {
                    NoteRead(stage, epoch, d => d with { Recovered = d.Recovered + 1 });
                    NoteRecovery(stage, operation, new(
                        "transient_read_recovered", LifeSkillRecoveryDisposition.ContinueAfterVerifiedRead));
                    Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                        "새 CLI 응답 확인 · 기존 작업/장부 유지");
                }
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                // A stage switch or cancelled F10 must never trigger another
                // read of the old stage even if the transport error is transient.
                ct.ThrowIfCancellationRequested();
                AssertEpoch(stage, epoch);
                var decision = LifeSkillRecoveryDecisionPolicy.ForReadFailure(error, attempt);
                NoteRecovery(stage, operation, decision);
                if (decision.Disposition == LifeSkillRecoveryDisposition.RetryReadOnlyOnce)
                {
                    Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                        "일시적인 CLI 조회 실패 · 300ms 뒤 읽기 전용 1회 재조회 · 화면 입력 없음");
                    await _recoveryDelay(TimeSpan.FromMilliseconds(300), ct);
                    continue;
                }
                NoteRead(stage, epoch, d => d with { Failed = d.Failed + 1 });
                Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                    $"분류={decision.Category} · " +
                    "안전 정지 · 클릭/등록/수령 자동 반복 없음");
                throw;
            }
        }
        throw new InvalidOperationException("중간관리자 조회 복구 횟수 초과");
    }

    // Each invocation performs a real CLI-backed read. It is never a cached
    // result for registration, gathering, crafting or collection permissions.
    internal async Task<long> ReadFreshCountAsync(
        LifeSkillStage stage, string item,
        Func<string, CancellationToken, Task<long>> read, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        ArgumentNullException.ThrowIfNull(read);
        // Preserve the original epoch through the post-query evidence commit.
        // Otherwise an Altering -> Gathering -> Altering transition could
        // accidentally commit an observation under the newer Altering epoch.
        long expectedEpoch;
        long expectedRead;
        lock (_gate)
        {
            if (_current != stage)
                throw new InvalidOperationException(
                    "중간관리자 재고 조회 시작 전 단계 불일치 · CLI 조회 중단");
            expectedEpoch = _epoch;
            expectedRead = ++_nextInventoryRead;
            _latestInventoryRead[item] = expectedRead;
        }
        // A negative count is a semantic CLI inconsistency, never a transient
        // transport issue. Validation occurs before the evidence is committed.
        long count = await ReadWithRecoveryAsync(stage, "inventory",
            async token =>
            {
                long observed = await read(item, token);
                if (observed < 0)
                    throw new InvalidDataException("CLI 재고 수량이 음수라 안전 정지합니다.");
                return observed;
            }, ct);
        lock (_gate)
        {
            if (_current != stage || _epoch != expectedEpoch)
                throw new InvalidOperationException(
                    "중간관리자 재고 기록 직전 작업 단계/epoch 변경 · 해당 증거 폐기");
            if (!_latestInventoryRead.TryGetValue(item, out long newest) ||
                newest != expectedRead)
            {
                _readDiagnostics = _readDiagnostics with {
                    StaleInventoryReplies = _readDiagnostics.StaleInventoryReplies + 1
                };
                throw new InvalidOperationException(
                    "중간관리자 동일 재료의 더 최신 재고 조회가 시작되어 이전 CLI 응답을 폐기합니다.");
            }
            // A new read cannot stand in for a committed queue/receipt; it is
            // diagnostic evidence only, never click authorization.
            _inventory[item] = new(stage, _epoch, count, _now());
        }
        return count;
    }

    // Diagnostics only. A positive result MUST NOT be used for game input.
    internal bool TryGetFreshEvidence(LifeSkillStage stage,
        string item, out LifeSkillCountEvidence? evidence)
    {
        lock (_gate)
        {
            evidence = null;
            if (_current != stage ||
                !_inventory.TryGetValue(item, out var value) ||
                value.Epoch != _epoch) return false;
            TimeSpan age = _now() - value.ObservedAtUtc;
            if (age < TimeSpan.Zero || age > _ttl) return false;
            evidence = value;
            return true;
        }
    }
}
