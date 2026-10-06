namespace FishingAutomation;

internal sealed record AlteringInternalConsumptionSnapshot(
    IReadOnlyDictionary<string, long> Counts);

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

    internal event Action<string>? Log;

    internal MultiAlteringConsumptionLedger(
        Func<IReadOnlyList<string>, CancellationToken,
            Task<IReadOnlyDictionary<string, long>>> readCounts)
    {
        _readCounts = readCounts ?? throw new ArgumentNullException(nameof(readCounts));
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

        return new(snapshot);
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
