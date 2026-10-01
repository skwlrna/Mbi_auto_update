namespace FishingAutomation;

internal static class SpiritWingSafety
{
    internal static decimal Read(IReadOnlyDictionary<string, decimal> currencies)
    {
        if (!currencies.TryGetValue("정령의 날개", out decimal amount))
            throw new InvalidOperationException("정령의 날개 보유량을 확인할 수 없어 자동화를 중단합니다.");
        return amount;
    }

    internal static void EnsureNotSpent(decimal before, decimal after, string action)
    {
        if (after < before)
            throw new InvalidOperationException(
                $"{action} 중 정령의 날개가 {before}→{after}로 감소했습니다. 즉시 정지합니다.");
    }
}

internal sealed class ZeroWingAlteringScreen : IAlteringScreen
{
    private readonly IAlteringScreen _inner;
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityGuard _identity;
    internal event Action<string>? Log;

    internal ZeroWingAlteringScreen(IAlteringScreen inner, MabinogiMobileCli cli, CliIdentityGuard identity)
    {
        _inner = inner;
        _cli = cli;
        _identity = identity;
    }

    public Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
        => GuardAsync("[자동 가공] 무료 현장 가공", async () =>
        {
            if (plan.AllowPaidButton)
                throw new InvalidOperationException("정령의 날개 사용 허용 가공은 실행하지 않습니다.");
            await _inner.QueueAsync(plan, reserveFiveWings, ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task CollectAsync(AlteringPlan plan, CancellationToken ct)
        => GuardAsync("[자동 가공] 완료품 수령", async () =>
        {
            await _inner.CollectAsync(plan, ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
        => GuardAsync("[자동 가공] 설비 이동 후 완료품 수령",
            () => _inner.CollectAfterTravelAsync(plan, ct), ct);

    private async Task<T> GuardAsync<T>(string action, Func<Task<T>> run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var before = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = SpiritWingSafety.Read(before);

        T result = await run().ConfigureAwait(false);

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var after = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsAfter = SpiritWingSafety.Read(after);
        SpiritWingSafety.EnsureNotSpent(wingsBefore, wingsAfter, action);

        string[] changes = CliAutomationGuards.CurrencyChanges(before, after);
        Log?.Invoke(changes.Length == 0
            ? $"{action} · 정령의 날개 변화 없음 ({wingsAfter})"
            : $"{action} · " + string.Join(" · ", changes));
        return result;
    }

    public void Dispose() => _inner.Dispose();
}

internal sealed class ZeroWingGatheringScreen : IGatheringScreen
{
    private readonly IGatheringScreen _inner;
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityGuard _identity;
    private decimal? _sessionWings;
    internal event Action<string>? Log;

    internal ZeroWingGatheringScreen(IGatheringScreen inner, MabinogiMobileCli cli, CliIdentityGuard identity)
    {
        _inner = inner;
        _cli = cli;
        _identity = identity;
    }

    public async Task StartAsync(GatheringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var before = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = SpiritWingSafety.Read(before);
        _sessionWings = wingsBefore;

        await _inner.StartAsync(plan, ct).ConfigureAwait(false);

        await VerifyAsync("[자동 채집] 일반 이동 시작", wingsBefore, ct).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        decimal before = _sessionWings ?? await CurrentWingsAsync(ct).ConfigureAwait(false);
        await _inner.StopAsync(ct).ConfigureAwait(false);
        await VerifyAsync("[자동 채집] 정지", before, ct).ConfigureAwait(false);
        _sessionWings = null;
    }

    private async Task VerifyAsync(string action, decimal baseline, CancellationToken ct)
    {
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var current = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wings = SpiritWingSafety.Read(current);
        SpiritWingSafety.EnsureNotSpent(baseline, wings, action);
        Log?.Invoke($"{action} · 정령의 날개 변화 없음 ({wings})");
    }

    private async Task<decimal> CurrentWingsAsync(CancellationToken ct)
        => SpiritWingSafety.Read(await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false));

    public void Dispose() => _inner.Dispose();
}
