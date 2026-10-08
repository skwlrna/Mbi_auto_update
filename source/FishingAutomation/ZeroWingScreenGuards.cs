using DungeonVisionBot;

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

internal sealed class ZeroWingAlteringScreen : IAlteringScreen, IAlteringCoordinatorQueueScreen, IAlteringCoordinatorReceiptScreen, IAlteringReceiptBoundaryScreen, IAlteringRecoveryScreen, IAlteringCoordinatorStallRecoveryScreen, IAlteringFieldExitScreen
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

    public Task QueueAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Action reserveFiveWings,
        CancellationToken ct)
        => GuardAsync("[자동 가공] 중간관리자 지시 가공", async () =>
        {
            if (plan.AllowPaidButton)
                throw new InvalidOperationException("정령의 날개 사용 허용 가공은 실행하지 않습니다.");
            if (_inner is not IAlteringCoordinatorQueueScreen coordinatorScreen)
                throw new InvalidOperationException(
                    "현재 가공 화면은 다중가공 중간관리자 시설 지시를 지원하지 않습니다.");

            await coordinatorScreen.QueueAsync(
                plan,
                directive,
                reserveFiveWings,
                ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
        => GuardAsync("[자동 가공] 완료품 수령",
            () => _inner.CollectAsync(plan, ct), ct);

    public Task<bool> CollectAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
        => GuardAsync("[자동 가공] 중간관리자 지시 완료품 수령", () =>
        {
            if (_inner is not IAlteringCoordinatorReceiptScreen coordinated)
                throw new InvalidOperationException(
                    "현재 가공 화면은 다중가공 중간관리자 수령 지시를 지원하지 않습니다.");
            return coordinated.CollectAsync(plan, directive, ct);
        }, ct);

    public Task<bool> CollectAsyncAtBoundary(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Func<CancellationToken, Task> beforeReceiveInput,
        CancellationToken ct)
        => GuardAsync("[자동 가공] 중간관리자 지시 수령 입력 경계", () =>
        {
            if (_inner is not IAlteringReceiptBoundaryScreen bounded)
                throw new InvalidOperationException(
                    "수령 입력 직전 영구 기록을 보장할 수 없는 화면 구현입니다. 입력 없이 정지합니다.");
            return bounded.CollectAsyncAtBoundary(plan, directive, beforeReceiveInput, ct);
        }, ct);

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
        => GuardAsync("[자동 가공] 설비 이동 후 완료품 수령",
            () => _inner.CollectAfterTravelAsync(plan, ct), ct);

    public Task RecoverStallAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
        => GuardAsync("[자동 가공] 정체 화면 재판정", async () =>
        {
            if (_inner is not IAlteringRecoveryScreen recovery)
                throw new InvalidOperationException("현재 가공 화면은 정체 복구를 지원하지 않습니다.");
            await recovery.RecoverStallAsync(plan, attempt, reason, ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<AlteringStallRecoveryObservation> RecoverStallForCoordinatorAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
        => GuardAsync("[자동 가공] 중간관리자 정체 복구 증거 확인", async () =>
        {
            if (_inner is not IAlteringCoordinatorStallRecoveryScreen coordinated)
                throw new InvalidOperationException(
                    "현재 가공 화면은 중간관리자 정체 복구 결과 보고를 지원하지 않습니다.");
            return await coordinated.RecoverStallForCoordinatorAsync(
                plan, attempt, reason, ct).ConfigureAwait(false);
        }, ct);

    public Task ExitToFieldAsync(CancellationToken ct)
        => GuardAsync("[자동 가공] 채집 전 가공 UI 종료", async () =>
        {
            if (_inner is not IAlteringFieldExitScreen fieldExit)
                throw new InvalidOperationException("현재 가공 화면은 채집 전 일반 필드 복귀를 지원하지 않습니다.");
            await fieldExit.ExitToFieldAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);


    private async Task<T> GuardAsync<T>(string action, Func<Task<T>> run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var before = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = SpiritWingSafety.Read(before);

        T result = default!;
        Exception? actionFailure = null;
        try
        {
            result = await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            actionFailure = ex;
        }

        Exception? verificationFailure = null;
        try
        {
            // Verification must still run after input failure or user cancellation.
            // A separate bounded token prevents a cancelled action token from skipping
            // the only check that can detect an unexpected wing spend.
            using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _identity.VerifyWithLoadingRetryAsync(verifyCts.Token).ConfigureAwait(false);
            var after = await CliAutomationGuards.CurrencySnapshotWithLoadingRetryAsync(_cli, verifyCts.Token).ConfigureAwait(false);
            decimal wingsAfter = SpiritWingSafety.Read(after);
            SpiritWingSafety.EnsureNotSpent(wingsBefore, wingsAfter, action);

            string[] changes = CliAutomationGuards.CurrencyChanges(before, after);
            Log?.Invoke(changes.Length == 0
                ? $"{action} · 정령의 날개 변화 없음 ({wingsAfter})"
                : $"{action} · " + string.Join(" · ", changes));
        }
        catch (Exception ex)
        {
            verificationFailure = ex;
        }

        if (verificationFailure is not null)
        {
            if (actionFailure is not null)
                throw new AggregateException(actionFailure, verificationFailure);
            throw verificationFailure;
        }
        if (actionFailure is not null) throw actionFailure;
        return result;
    }

    public void Dispose() => _inner.Dispose();
}

internal sealed class ZeroWingGatheringScreen : IGatheringScreen, IGatheringStopVisualProbe
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

        Exception? actionFailure = null;
        try
        {
            await _inner.StartAsync(plan, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            actionFailure = ex;
        }

        string verificationAction = actionFailure is null
            ? "[자동 채집] 일반 이동 시작"
            : "[자동 채집] 시작 실패 후 날개 검사";
        Exception? verificationFailure = await VerifyAfterActionAsync(
            verificationAction, wingsBefore).ConfigureAwait(false);
        if (verificationFailure is not null)
        {
            if (actionFailure is not null) throw new AggregateException(actionFailure, verificationFailure);
            throw verificationFailure;
        }
        if (actionFailure is not null) throw actionFailure;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        decimal before = _sessionWings ?? await CurrentWingsAsync(ct).ConfigureAwait(false);

        Exception? actionFailure = null;
        try
        {
            await _inner.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            actionFailure = ex;
        }

        Exception? verificationFailure = await VerifyAfterActionAsync("[자동 채집] 정지", before).ConfigureAwait(false);
        _sessionWings = null;
        if (verificationFailure is not null)
        {
            if (actionFailure is not null) throw new AggregateException(actionFailure, verificationFailure);
            throw verificationFailure;
        }
        if (actionFailure is not null) throw actionFailure;
    }

    private async Task<Exception?> VerifyAfterActionAsync(string action, decimal baseline)
    {
        try
        {
            using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _identity.VerifyWithLoadingRetryAsync(verifyCts.Token).ConfigureAwait(false);
            var current = await CliAutomationGuards.CurrencySnapshotWithLoadingRetryAsync(_cli, verifyCts.Token).ConfigureAwait(false);
            decimal wings = SpiritWingSafety.Read(current);
            SpiritWingSafety.EnsureNotSpent(baseline, wings, action);
            Log?.Invoke($"{action} · 정령의 날개 변화 없음 ({wings})");
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<decimal> CurrentWingsAsync(CancellationToken ct)
        => SpiritWingSafety.Read(await CliAutomationGuards.CurrencySnapshotWithLoadingRetryAsync(_cli, ct).ConfigureAwait(false));

    public Task<bool> IsStopButtonVisibleAsync(CancellationToken ct)
        => _inner is IGatheringStopVisualProbe probe
            ? probe.IsStopButtonVisibleAsync(ct)
            : Task.FromResult(true); // Unsupported screen: fail closed.

    public void Dispose() => _inner.Dispose();
}


internal sealed class ZeroWingCraftingScreen : ICraftingScreen
{
    private readonly ICraftingScreen _inner;
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityGuard _identity;
    public event Action<string>? Log;
    public string InputMode => _inner.InputMode;

    internal ZeroWingCraftingScreen(ICraftingScreen inner, MabinogiMobileCli cli, CliIdentityGuard identity)
    {
        _inner = inner;
        _cli = cli;
        _identity = identity;
        _inner.Log += text => Log?.Invoke(text);
    }

    public Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
        => GuardAsync("[제작] 퀘스트 생성", () => _inner.CreateQuestAsync(plan, craftCount, ct), ct);

    public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan plan, CancellationToken ct)
        => _inner.ReadQuestDeficitsAsync(plan, ct);

    public Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
        => GuardAsync("[제작] 퀘스트 재료 채집", () => _inner.GatherQuestDeficitAsync(deficit, ct), ct);

    public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct)
        => _inner.RecoverBatchPreparationAsync(plan, ct);

    public Task CloseOverlayAsync(CancellationToken ct)
        => _inner.CloseOverlayAsync(ct);

    public Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
        => GuardAsync("[제작] 제작대 복귀 및 제작", () => _inner.ReturnToStationAndCraftAsync(plan, craftCount, ct), ct);

    private async Task GuardAsync(string action, Func<Task> run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _identity.VerifyAsync(ct).ConfigureAwait(false);
        var before = await CliAutomationGuards.CurrencySnapshotAsync(_cli, ct).ConfigureAwait(false);
        decimal wingsBefore = SpiritWingSafety.Read(before);

        Exception? actionFailure = null;
        try { await run().ConfigureAwait(false); }
        catch (Exception ex) { actionFailure = ex; }

        Exception? verificationFailure = null;
        try
        {
            using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _identity.VerifyWithLoadingRetryAsync(verifyCts.Token).ConfigureAwait(false);
            var after = await CliAutomationGuards.CurrencySnapshotWithLoadingRetryAsync(_cli, verifyCts.Token).ConfigureAwait(false);
            decimal wingsAfter = SpiritWingSafety.Read(after);
            SpiritWingSafety.EnsureNotSpent(wingsBefore, wingsAfter, action);
            Log?.Invoke($"{action} · 정령의 날개 변화 없음 ({wingsAfter})");
        }
        catch (Exception ex) { verificationFailure = ex; }

        if (verificationFailure is not null)
        {
            if (actionFailure is not null) throw new AggregateException(actionFailure, verificationFailure);
            throw verificationFailure;
        }
        if (actionFailure is not null) throw actionFailure;
    }

    public void Dispose() => _inner.Dispose();
}
