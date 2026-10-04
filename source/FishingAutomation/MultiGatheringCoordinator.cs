namespace FishingAutomation;

internal sealed record MultiGatheringRequest(
    string DisplayName,
    long AdditionalQuantity,
    AlteringPlan SourceRecipe);

/// <summary>
/// Coordinates several raw-material acquisitions while deliberately reusing the
/// already-verified single-material GatheringAutomation. This class never sends
/// input itself and never implements a second stop/Space path.
/// </summary>
internal sealed class MultiGatheringCoordinator
{
    private readonly IGatheringData _data;
    private readonly IGatheringScreen _screen;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly int _verificationAttempts;

    internal event Action<string>? Log;

    internal MultiGatheringCoordinator(
        IGatheringData data,
        IGatheringScreen screen,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int verificationAttempts = 120)
    {
        _data = data;
        _screen = screen;
        _delay = delay;
        _verificationAttempts = Math.Clamp(verificationAttempts, 1, 120);
    }

    internal async Task RunAsync(
        IReadOnlyList<MultiGatheringRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0) return;

        var grouped = requests
            .Where(x => x.AdditionalQuantity > 0)
            .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
            .Select(g => new MultiGatheringRequest(
                g.Key,
                checked(g.Sum(x => x.AdditionalQuantity)),
                g.First().SourceRecipe with { AllowPaidButton = false }))
            .ToArray();

        Log?.Invoke(
            $"[다중 채집] 계획 확정 · {grouped.Length}종 · " +
            string.Join(", ", grouped.Select(x => $"{x.DisplayName} +{x.AdditionalQuantity}")));

        foreach (var request in grouped)
        {
            ct.ThrowIfCancellationRequested();

            // Re-read immediately before this material. Previous gathering or a
            // manual inventory change may already have satisfied part of the plan.
            long before = await _data.ItemCountAsync(request.DisplayName, ct);
            long targetTotal = checked(before + request.AdditionalQuantity);

            // One more fresh read keeps "start-time inventory is authority" explicit.
            long fresh = await _data.ItemCountAsync(request.DisplayName, ct);
            long remaining = Math.Max(0, targetTotal - fresh);
            if (remaining == 0)
            {
                Log?.Invoke(
                    $"[다중 채집] 건너뜀 · {request.DisplayName} · 시작 직전 재고로 이미 목표 충족 {fresh}/{targetTotal}");
                continue;
            }

            if (remaining > 1_000_000)
                throw new InvalidOperationException(
                    $"다중 채집 안전 한도를 넘었습니다: {request.DisplayName} +{remaining}");

            var plan = new GatheringPlan(request.DisplayName, checked((int)remaining))
            {
                SourceRecipe = request.SourceRecipe with { AllowPaidButton = false }
            };

            Log?.Invoke(
                $"[다중 채집] 시작 · {request.DisplayName} · 현재 {fresh} / 목표 {targetTotal} · 추가 {remaining}");

            var automation = new GatheringAutomation(
                _data, _screen, _delay, _verificationAttempts);
            automation.Log += text => Log?.Invoke(text);
            await automation.RunAsync(plan, ct);

            long after = await _data.ItemCountAsync(request.DisplayName, ct);
            if (after < targetTotal)
                throw new InvalidOperationException(
                    $"{request.DisplayName} 다중 채집 후 재고 검증 실패: {after}/{targetTotal}");

            Log?.Invoke(
                $"[다중 채집] 완료 · {request.DisplayName} · {before}→{after} · 목표 {targetTotal}");
        }

        Log?.Invoke("[다중 채집] 전체 원재료 확보 완료 · 호출한 가공 단계로 복귀");
    }
}
