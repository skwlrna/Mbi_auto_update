namespace FishingAutomation;

/// <summary>
/// Direct CLI implementation of the altering action surface. Catalog and work verification
/// remain in AlteringAutomation; this class only replaces OCR/keyboard action delivery.
/// </summary>
internal sealed class AlteringCliActions : IAlteringScreen, IDirectAlteringActions
{
    private readonly MabinogiMobileCli _cli;
    internal string InputMode => "MabinogiMobile CLI";
    internal event Action<string>? Log;

    internal AlteringCliActions(MabinogiMobileCli cli) => _cli = cli;

    public async Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        if (plan.RecipeOrdinal != 1)
            throw new InvalidOperationException("CLI 가공은 게임이 같은 이름의 첫 번째 제법만 실행하므로 2번째 이후 중복 제법은 화면 방식이 필요합니다.");

        bool requiresConfirm = await _cli.ActionRequiresConfirmAsync("execute_altering", ct).ConfigureAwait(false);
        if (requiresConfirm)
        {
            if (!plan.AllowPaidButton)
                throw new InvalidOperationException("이 가공 CLI 명령은 게임에서 실행 확인이 필요한 명령입니다. 비용 허용이 꺼져 있어 실행하지 않습니다.");
            reserveFiveWings();
        }

        Log?.Invoke($"[자동 가공] CLI 작업 등록 요청 · {plan.DisplayName}");
        var result = await _cli.ExecuteAlteringAsync(plan.DisplayName, plan.AllowPaidButton, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI 가공 등록 실패: {result.Error ?? result.State}");
    }

    public async Task CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        var works = AlteringQueries.ParseWorks(await _cli.GetAlteringWorksAsync(ct).ConfigureAwait(false));
        var target = works.FirstOrDefault(x => x.IsCompleted && x.FacilityName == plan.FacilityName &&
            (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
        if (target is null)
            throw new InvalidOperationException("CLI 완료 작업 목록에서 선택한 가공품의 정확한 수령 대상을 확인하지 못했습니다.");

        Log?.Invoke($"[자동 가공] CLI 완료 작업 수령 요청 · {target.DisplayName}");
        var result = await _cli.CompleteAlteringWorkAsync(target.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"CLI 완료 작업 수령 실패: {result.Error ?? result.State}");
    }

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Direct CLI collection does not use the two-stage on-screen travel/Space flow.
        // Never retry an uncertain state-changing command.
        return Task.FromResult(false);
    }

    public void Dispose() { }
}
