namespace FishingAutomation;

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
    private readonly Dictionary<string, LifeSkillCountEvidence> _inventory =
        new(StringComparer.Ordinal);
    private LifeSkillStage? _current;
    private long _epoch;
    internal event Action<string>? Log;

    internal LifeSkillManagerState(Func<DateTimeOffset>? now = null, TimeSpan? ttl = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromSeconds(5);
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

    // Each invocation performs a real CLI-backed read. It is never a cached
    // result for registration, gathering, crafting or collection permissions.
    internal async Task<long> ReadFreshCountAsync(
        LifeSkillStage stage, string item,
        Func<string, CancellationToken, Task<long>> read, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        ArgumentNullException.ThrowIfNull(read);
        ct.ThrowIfCancellationRequested();
        long expected;
        lock (_gate)
        {
            if (_current != stage)
                throw new InvalidOperationException(
                    "중간관리자 작업 단계 불일치 · 기존 상태를 새 작업에 사용하지 않습니다.");
            expected = _epoch;
        }
        long count = await read(item, ct);
        ct.ThrowIfCancellationRequested();
        if (count < 0)
            throw new InvalidDataException("CLI 재고 수량이 음수라 안전 정지합니다.");
        lock (_gate)
        {
            if (_epoch != expected || _current != stage)
                throw new InvalidOperationException(
                    "CLI 조회 도중 작업 단계가 변경돼 해당 응답을 폐기합니다.");
            _inventory[item] = new(stage, expected, count, _now());
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
