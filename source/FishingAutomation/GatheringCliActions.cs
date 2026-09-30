using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// One-shot direct CLI gathering action. GatheringAutomation owns validation, inventory
/// deltas and repetition. A capability requiring confirmation is blocked by ZeroWingMode.
/// </summary>
internal sealed class GatheringCliActions : IGatheringScreen, IDirectGatheringAction
{
    private readonly MabinogiMobileCli _cli;
    internal string InputMode => "MabinogiMobile CLI";
    internal event Action<string>? Log;

    internal GatheringCliActions(MabinogiMobileCli cli) => _cli = cli;

    public async Task ExecuteOnceAsync(GatheringPlan plan, CancellationToken ct)
    {
        Log?.Invoke($"[자동채집] CLI 채집 요청 · {plan.DisplayName}");
        var result = await _cli.ExecuteGatheringAsync(plan.DisplayName, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            if (result.Error == "confirmation_required")
                throw new InvalidOperationException("execute_gathering이 실행 확인이 필요한 명령으로 표시되어 ZeroWingMode에서 차단했습니다. 정령의 날개를 사용하지 않습니다.");
            throw new InvalidOperationException($"CLI 채집 실행 실패: {result.Error ?? result.State}");
        }

        // mobi-Support treats result=started as the special persistent fishing case.
        // Do not enter that mode from the generic gathering automation.
        if (result.Data is JsonElement data && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("result", out var state) && state.ValueKind == JsonValueKind.String &&
            state.GetString()?.Equals("started", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stopped = await _cli.StopOwnedFishingAsync(cleanup.Token).ConfigureAwait(false);
            throw new InvalidOperationException(stopped.Success
                ? "CLI가 지속형 낚시를 시작해 stop_action으로 즉시 정리했습니다. 일반 자동채집에서는 낚시를 지원하지 않습니다."
                : "CLI가 지속형 낚시를 시작했지만 stop_action 확인에 실패했습니다. 게임에서 낚시 상태를 확인하세요.");
        }
    }

    // Existing interface compatibility. Direct mode uses ExecuteOnceAsync from
    // GatheringAutomation instead of StartAsync.
    public Task StartAsync(GatheringPlan plan, CancellationToken ct) => ExecuteOnceAsync(plan, ct);

    public Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // One-shot commands are terminal before this method is reached. We deliberately
        // do not issue stop_action for an action whose ownership cannot be proven.
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
