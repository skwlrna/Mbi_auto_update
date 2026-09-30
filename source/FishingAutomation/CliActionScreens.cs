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
        if (!plan.AllowPaidButton)
            throw new InvalidOperationException("CLI 가공 실행은 허용된 유료 가공 명령에서만 사용할 수 있습니다.");
        if (plan.RecipeOrdinal != 1)
            throw new InvalidOperationException("CLI execute_altering는 같은 이름의 첫 번째 제법만 실행할 수 있습니다.");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesBefore = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);

        // Reserve before the action. If the CLI result becomes uncertain, never retry for free.
        reserveFiveWings();
        var result = await _cli.ExecuteAlteringAsync(plan.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI execute_altering 실패: {result.State}/{result.Error ?? "unknown"}");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesAfter = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        LogCurrencyChanges("[자동 가공] 등록 재화 변화", currenciesBefore, currenciesAfter);
        Log?.Invoke($"[자동 가공] CLI execute_altering 완료 · {plan.DisplayName} · 캐릭터 문맥 재확인");
    }

    public Task CollectAsync(AlteringPlan plan, CancellationToken ct)
        => CompleteAsync(plan.DisplayName, ct);

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
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesBefore = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);

        var result = await _cli.ExecuteGatheringAsync(plan.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI execute_gathering 실패: {result.State}/{result.Error ?? "unknown"}");

        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var currenciesAfter = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        string[] changes = CliAutomationGuards.CurrencyChanges(currenciesBefore, currenciesAfter);
        Log?.Invoke(changes.Length == 0
            ? "[자동채집] 시작 재화 변화 · 변화 없음"
            : "[자동채집] 시작 재화 변화 · " + string.Join(" · ", changes));
        Log?.Invoke($"[자동채집] CLI execute_gathering 완료 · {plan.DisplayName} · 캐릭터 문맥 재확인");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var result = await _cli.StopActionAsync(ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI stop_action 실패: {result.State}/{result.Error ?? "unknown"}");
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        Log?.Invoke("[자동채집] CLI stop_action 완료 · 캐릭터 문맥 재확인");
    }

    public void Dispose() { }
}
