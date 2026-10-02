namespace FishingAutomation;

// Bulk gathering by the life-skill "100회 채집" route.
// One 100-action cycle can yield a variable number of items, so inventory
// delta is always authoritative and no fixed "100 items" assumption is made.
internal sealed class LifeSkillBulkGatheringAutomation
{
    private readonly Func<CancellationToken, Task<long>> _count;
    private readonly Func<CancellationToken, Task> _startHundredActions;
    private readonly Func<long, CancellationToken, Task> _waitNaturalStop;

    internal event Action<string>? Log;

    internal LifeSkillBulkGatheringAutomation(
        Func<CancellationToken, Task<long>> count,
        Func<CancellationToken, Task> startHundredActions,
        Func<long, CancellationToken, Task> waitNaturalStop)
    {
        _count = count;
        _startHundredActions = startHundredActions;
        _waitNaturalStop = waitNaturalStop;
    }

    internal async Task RunAsync(GatheringPlan plan, CancellationToken ct)
    {
        plan.Validate();
        long baseline = await _count(ct);
        long previous = baseline;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long current = await _count(ct);
            if (current < previous)
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 100회 채집 중 재고가 감소해 정지합니다.");
            previous = current;

            long gained = current - baseline;
            if (gained >= plan.TargetQuantity)
            {
                Log?.Invoke(
                    $"[대량 채집] 목표 확보 · {plan.DisplayName} +{gained}/{plan.TargetQuantity}");
                return;
            }

            Log?.Invoke(
                $"[대량 채집] 생활 스킬 100회 시작 · {plan.DisplayName} · 현재 +{gained}/{plan.TargetQuantity}");
            long before = current;
            await _startHundredActions(ct);
            await _waitNaturalStop(before, ct);

            long after = await _count(ct);
            if (after <= before)
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 생활 스킬 100회 자연 종료 후 재고 증가가 없어 정지합니다.");

            Log?.Invoke(
                $"[대량 채집] 생활 스킬 100회 자연 종료 · {plan.DisplayName} {before}→{after} · +{after - before}");
            previous = after;
        }
    }
}
