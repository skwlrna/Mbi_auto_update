namespace FishingAutomation;

// Inventory-driven orchestration; screen callbacks provide the free quest route.
internal sealed class InventoryBulkGatheringAutomation
{
    private readonly Func<CancellationToken, Task<long>> _count;
    private readonly Func<CancellationToken, Task<long>> _bag;
    private readonly Func<CancellationToken, Task> _seed;
    private readonly Func<CancellationToken, Task> _startHundred;
    private readonly Func<long, long, CancellationToken, Task> _waitNaturalStop;
    internal event Action<string>? Log;

    internal InventoryBulkGatheringAutomation(
        Func<CancellationToken, Task<long>> count,
        Func<CancellationToken, Task<long>> bag,
        Func<CancellationToken, Task> seed,
        Func<CancellationToken, Task> startHundred,
        Func<long, long, CancellationToken, Task> waitNaturalStop)
    { _count = count; _bag = bag; _seed = seed; _startHundred = startHundred; _waitNaturalStop = waitNaturalStop; }

    internal async Task RunAsync(GatheringPlan plan, CancellationToken ct)
    {
        plan.Validate();
        long baseline = await _count(ct);
        long previous = baseline;
        if (await _bag(ct) == 0)
        {
            Log?.Invoke($"[대량 채집] {plan.DisplayName} 가방 보유 0개 · 최초 확보 1회 시작");
            await _seed(ct);
            await _waitNaturalStop(baseline, 1, ct);
            if (await _bag(ct) <= 0)
                throw new InvalidOperationException($"{plan.DisplayName} 최초 확보 후에도 가방 수량이 0개입니다.");
            Log?.Invoke($"[대량 채집] 최초 확보 확인 · {plan.DisplayName} · 100개 채집으로 전환");
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _count(ct);
            if (current < previous)
                throw new InvalidOperationException($"{plan.DisplayName} 대량 채집 중 재고가 감소해 정지합니다.");
            previous = current;
            long gained = current - baseline;
            if (gained >= plan.TargetQuantity)
            {
                Log?.Invoke($"[대량 채집] 목표 확보 · {plan.DisplayName} +{gained}/{plan.TargetQuantity}");
                return;
            }
            await _startHundred(ct);
            Log?.Invoke($"[대량 채집] {plan.DisplayName} 100개 채집 퀘스트 시작 · 현재 +{gained}/{plan.TargetQuantity}");
            await _waitNaturalStop(current, 100, ct);
            long after = await _count(ct);
            if (after - current < 100)
                throw new InvalidOperationException($"{plan.DisplayName} 자연 종료 후 100개 재고 증가를 확인하지 못했습니다.");
            Log?.Invoke($"[대량 채집] 100개 채집 자연 종료 확인 · {plan.DisplayName} {current}→{after}");
        }
    }
}
