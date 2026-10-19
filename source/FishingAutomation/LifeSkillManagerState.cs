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


// Per-run observation ledger for the existing middle manager. Never authorizes
// GUI input; all action paths continue their own fresh CLI and visual checks.
internal enum LifeSkillStage { Altering, Gathering, Crafting }
internal sealed record LifeSkillCountEvidence(
    LifeSkillStage Stage, long Epoch, long Quantity, DateTimeOffset ObservedAtUtc);
internal sealed class LifeSkillManagerState
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _ttl;
    private readonly Func<TimeSpan, CancellationToken, Task> _recoveryDelay;
    private readonly Dictionary<string, LifeSkillCountEvidence> _inventory =
        new(StringComparer.Ordinal);
    private LifeSkillStage? _current;
    private long _epoch;
    internal event Action<string>? Log;

    internal LifeSkillManagerState(
        Func<DateTimeOffset>? now = null, TimeSpan? ttl = null,
        Func<TimeSpan, CancellationToken, Task>? recoveryDelay = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromSeconds(5);
        _recoveryDelay = recoveryDelay ?? Task.Delay;
        if (_ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
    }

    internal void BeginStage(LifeSkillStage stage, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        long epoch;
        lock (_gate)
        {
            _current = stage;
            epoch = ++_epoch;
            _inventory.Clear();
        }
        Log?.Invoke($"[중간관리자] 단계 전환 {stage} · epoch={epoch} · " +
            $"기존 재고 관측 무효화 · 새로운 CLI 재조회 · {reason}");
    }

    internal void Invalidate(string reason)
    {
        lock (_gate)
        {
            _current = null;
            _epoch++;
            _inventory.Clear();
        }
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
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            AssertEpoch(stage, epoch);
            try
            {
                T result = await readOnly(ct);
                ct.ThrowIfCancellationRequested();
                AssertEpoch(stage, epoch);
                if (attempt > 0)
                    Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                        "새 CLI 응답 확인 · 기존 작업/장부 유지");
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (attempt == 0 && !ct.IsCancellationRequested &&
                    LifeSkillReadRecoveryPolicy.IsTransient(error))
                {
                    Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                        "일시적인 CLI 조회 실패 · 300ms 뒤 읽기 전용 1회 재조회 · 화면 입력 없음");
                    await _recoveryDelay(TimeSpan.FromMilliseconds(300), ct);
                    continue;
                }
                Log?.Invoke($"[중간관리자][조회 복구] {stage}/{operation} · " +
                    $"분류={LifeSkillReadRecoveryPolicy.Category(error)} · " +
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
            if (_current != stage)
                throw new InvalidOperationException(
                    "중간관리자 재고 기록 직전 작업 단계 변경 · 해당 증거 폐기");
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
