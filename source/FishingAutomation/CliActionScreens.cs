namespace FishingAutomation;

internal sealed class AlteringCliScreen : IAlteringScreen, IDirectCliAlteringScreen
{
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityGuard _identity;
    internal string InputMode => "CLI";
    internal event Action<string>? Log;

    internal AlteringCliScreen(MabinogiMobileCli cli, CliIdentityGuard identity)
    {
        _cli = cli;
        _identity = identity;
    }

    public async Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (plan.AllowPaidButton)
            throw new InvalidOperationException("정령의 날개 사용 허용 설정은 지원하지 않습니다. 자동 가공은 0개 사용 원칙입니다.");
        if (!_cli.ZeroWingMode)
            throw new InvalidOperationException("ZeroWingMode가 꺼져 있어 자동 가공 실행을 차단합니다.");
        if (plan.RecipeOrdinal != 1)
            throw new InvalidOperationException("CLI execute_altering는 같은 이름의 첫 번째 제법만 실행할 수 있습니다.");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesBefore = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = SpiritWings(currenciesBefore);

        // execute_altering is known to be capable of using Spirit Wings in the current
        // game CLI. A post-action check would be too late, so never call it while any
        // spendable wings are present. This preserves the absolute zero-wing rule.
        if (wingsBefore != 0)
            throw new InvalidOperationException(
                $"정령의 날개 0개 사용 원칙으로 CLI execute_altering를 실행하지 않습니다. 현재 보유 {wingsBefore}개. " +
                "무료 가공 실행 경로가 확인되기 전에는 자동 등록을 중단합니다.");

        var result = await _cli.ExecuteAlteringAsync(plan.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI execute_altering 실패: {result.State}/{result.Error ?? "unknown"}");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesAfter = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsAfter = SpiritWings(currenciesAfter);
        if (wingsAfter != 0)
            throw new InvalidOperationException("정령의 날개 수량이 0이 아닌 상태로 바뀌어 자동 가공을 즉시 중단합니다.");

        LogCurrencyChanges("[자동 가공] 등록 재화 변화", currenciesBefore, currenciesAfter);
        Log?.Invoke($"[자동 가공] CLI execute_altering 완료 · {plan.DisplayName} · 정령의 날개 0개 확인");
    }

    private static decimal SpiritWings(IReadOnlyDictionary<string, decimal> currencies)
    {
        if (!currencies.TryGetValue("정령의 날개", out decimal amount))
            throw new InvalidOperationException("정령의 날개 보유량을 확인할 수 없어 자동 가공 실행을 차단합니다.");
        return amount;
    }

    public async Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        await CompleteAsync(plan.DisplayName, ct).ConfigureAwait(false);
        return true;
    }

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
        => Task.FromResult(false);

    public async Task CompleteAsync(string displayName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesBefore = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);

        var result = await _cli.CompleteAlteringWorkAsync(displayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI complete_altering_work 실패: {result.State}/{result.Error ?? "unknown"}");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesAfter = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        LogCurrencyChanges("[자동 가공] 수령 재화 변화", currenciesBefore, currenciesAfter);
        Log?.Invoke($"[자동 가공] CLI complete_altering_work 완료 · {displayName} · 캐릭터 문맥 재확인");
    }

    private void LogCurrencyChanges(string prefix,
        IReadOnlyDictionary<string, decimal> before,
        IReadOnlyDictionary<string, decimal> after)
    {
        string[] changes = CliAutomationGuards.CurrencyChanges(before, after);
        Log?.Invoke(changes.Length == 0
            ? prefix + " · 변화 없음"
            : prefix + " · " + string.Join(" · ", changes));
    }

    public void Dispose() { }
}

internal sealed class GatheringCliScreen : IGatheringScreen
{
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityGuard _identity;
    internal string InputMode => "CLI";
    internal event Action<string>? Log;

    internal GatheringCliScreen(MabinogiMobileCli cli, CliIdentityGuard identity)
    {
        _cli = cli;
        _identity = identity;
    }

    public async Task StartAsync(GatheringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cli.ZeroWingMode)
            throw new InvalidOperationException("ZeroWingMode가 꺼져 있어 자동 채집 실행을 차단합니다.");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesBefore = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = GatheringSpiritWings(currenciesBefore);

        // execute_gathering may perform travel under the game CLI's rules. Because a
        // post-action currency check cannot undo a spend, only run it when there are
        // no Spirit Wings available to spend.
        if (wingsBefore != 0)
            throw new InvalidOperationException(
                $"정령의 날개 0개 사용 원칙으로 CLI execute_gathering를 실행하지 않습니다. 현재 보유 {wingsBefore}개. " +
                "무료 이동 경로가 확인되기 전에는 자동 채집을 중단합니다.");

        var result = await _cli.ExecuteGatheringAsync(plan.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI execute_gathering 실패: {result.State}/{result.Error ?? "unknown"}");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesAfter = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsAfter = GatheringSpiritWings(currenciesAfter);
        if (wingsAfter != 0)
            throw new InvalidOperationException("정령의 날개 수량이 0이 아닌 상태로 바뀌어 자동 채집을 즉시 중단합니다.");

        string[] changes = CliAutomationGuards.CurrencyChanges(currenciesBefore, currenciesAfter);
        Log?.Invoke(changes.Length == 0
            ? "[자동 채집] 시작 재화 변화 · 변화 없음 · 정령의 날개 0개 확인"
            : "[자동 채집] 시작 재화 변화 · " + string.Join(" · ", changes));
        Log?.Invoke($"[자동 채집] CLI execute_gathering 완료 · {plan.DisplayName} · 캐릭터 문맥 재확인");
    }

    private static decimal GatheringSpiritWings(IReadOnlyDictionary<string, decimal> currencies)
    {
        if (!currencies.TryGetValue("정령의 날개", out decimal amount))
            throw new InvalidOperationException("정령의 날개 보유량을 확인할 수 없어 자동 채집 실행을 차단합니다.");
        return amount;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var result = await _cli.StopActionAsync(ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI stop_action 실패: {result.State}/{result.Error ?? "unknown"}");
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        Log?.Invoke("[자동 채집] CLI stop_action 완료 · 캐릭터 문맥 재확인");
    }

    public void Dispose() { }
}
