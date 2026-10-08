namespace FishingAutomation;

internal sealed record AlteringInternalConsumptionSnapshot(
    IReadOnlyDictionary<string, long> Counts, string? TransactionId = null);

internal interface IAlteringInternalConsumptionObserver
{
    Task<AlteringInternalConsumptionSnapshot> CaptureBeforeRegistrationAsync(
        AlteringPlan consumerPlan,
        CancellationToken ct);

    Task CommitAfterRegistrationAsync(
        AlteringPlan consumerPlan,
        AlteringInternalConsumptionSnapshot before,
        CancellationToken ct);
}

/// <summary>
/// Accounts only inventory decreases that happen inside the narrow window of one
/// confirmed multi-altering registration. This lets one selected output be consumed
/// as another selected recipe's material without weakening the normal monotonic
/// inventory safety rule outside that registration window.
/// </summary>
internal sealed class MultiAlteringConsumptionLedger : IAlteringInternalConsumptionObserver
{
    private readonly Func<IReadOnlyList<string>, CancellationToken,
        Task<IReadOnlyDictionary<string, long>>> _readCounts;
    private readonly Dictionary<string, Action<long, string>> _creditByOutput =
        new(StringComparer.Ordinal);
    private readonly MultiAlteringBatchStore? _durableBatch;

    internal event Action<string>? Log;

    internal MultiAlteringConsumptionLedger(
        Func<IReadOnlyList<string>, CancellationToken,
            Task<IReadOnlyDictionary<string, long>>> readCounts,
        MultiAlteringBatchStore? durableBatch = null)
    {
        _readCounts = readCounts ?? throw new ArgumentNullException(nameof(readCounts));
        _durableBatch = durableBatch;
    }

    internal void RegisterProducer(
        AlteringPlan plan,
        Action<long, string> creditConsumption)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(creditConsumption);

        if (!_creditByOutput.TryAdd(plan.OutputName, creditConsumption))
            throw new InvalidOperationException(
                $"다중가공 내부 재료 추적에서 동일 결과물을 둘 이상 구분할 수 없습니다: {plan.OutputName}");
    }

    internal void UnregisterProducer(AlteringPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _creditByOutput.Remove(plan.OutputName);
    }

    public async Task<AlteringInternalConsumptionSnapshot> CaptureBeforeRegistrationAsync(
        AlteringPlan consumerPlan,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(consumerPlan);

        string[] names = _creditByOutput.Keys
            .Where(x => !x.Equals(consumerPlan.OutputName, StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
            return new(new Dictionary<string, long>(StringComparer.Ordinal));

        var counts = await _readCounts(names, ct);
        var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!counts.TryGetValue(name, out long quantity) || quantity < 0)
                throw new InvalidDataException(
                    $"다중가공 내부 재료 등록 전 {name} 보유량을 안전하게 확인하지 못했습니다.");
            snapshot[name] = quantity;
        }

        if (_durableBatch is null)
            return new(snapshot);
        string transactionId = Guid.NewGuid().ToString("N");
        // Persist the exact before-image BEFORE the irreversible registration.
        _durableBatch.PrepareConsumption(consumerPlan, transactionId, snapshot, ct);
        return new(snapshot, transactionId);
    }

    public async Task CommitAfterRegistrationAsync(
        AlteringPlan consumerPlan,
        AlteringInternalConsumptionSnapshot before,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(consumerPlan);
        ArgumentNullException.ThrowIfNull(before);

        if (before.Counts.Count == 0)
            return;

        string[] names = before.Counts.Keys
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var after = await _readCounts(names, ct);

        if (_durableBatch is not null)
        {
            if (string.IsNullOrWhiteSpace(before.TransactionId))
                throw new InvalidOperationException(
                    "F05 등록 확인용 소비 거래 ID 소실 · 중복 보정 차단");
            // The store commits ALL producer credits and the applied transaction
            // in one atomic batch.json replacement. No callback writes credits.
            bool applied = _durableBatch.ApplyConsumption(
                consumerPlan, before.TransactionId, after, ct);
            if (applied)
            {
                foreach (var (name, previous) in before.Counts)
                {
                    long current = after[name];
                    if (current >= previous) continue;
                    if (!_creditByOutput.TryGetValue(name, out var refresh))
                        throw new InvalidOperationException(
                            "F05 소비 확정 후 생산자 메모리 새로고침 실패 · 안전 정지");
                    refresh(checked(previous - current), consumerPlan.DisplayName);
                    Log?.Invoke(
                        $"[다중가공] 거래형 내부 소비 확정 · {before.TransactionId} · " +
                        $"{consumerPlan.DisplayName} → {name} {previous:N0}→{current:N0}");
                }
            }
            return; // Already-applied transaction is a strict no-op.
        }

        foreach (string name in names)
        {
            if (!after.TryGetValue(name, out long current) || current < 0)
                throw new InvalidDataException(
                    $"다중가공 내부 재료 등록 후 {name} 보유량을 안전하게 확인하지 못했습니다.");

            long previous = before.Counts[name];
            if (current >= previous)
                continue;

            long consumed = checked(previous - current);
            if (!_creditByOutput.TryGetValue(name, out var credit))
                throw new InvalidOperationException(
                    $"다중가공 내부 재료 소비 장부에서 {name} 생산 작업을 찾지 못했습니다.");

            credit(consumed, consumerPlan.DisplayName);
            Log?.Invoke(
                $"[다중가공] 내부 재료 소비 확인 · {consumerPlan.DisplayName} 등록 중 " +
                $"{name} {previous:N0}→{current:N0} · {consumed:N0}개 생산 추적 보정");
        }
    }
}
