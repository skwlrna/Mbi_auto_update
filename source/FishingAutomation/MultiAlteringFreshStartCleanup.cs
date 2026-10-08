namespace FishingAutomation;

// No history reconciliation on F9. Clear live works at selected facilities
// BEFORE recording the new order's inventory baseline or registering anything.
internal static class MultiAlteringFreshStartCleanup
{
    internal static async Task ClearAsync(
        IReadOnlyList<AlteringPlan> plans,
        IAlteringData data,
        IAlteringCoordinatorReceiptScreen screen,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string>? log,
        CancellationToken ct)
    {
        foreach (var group in plans.GroupBy(p => p.FacilityName, StringComparer.Ordinal))
        {
            int checks = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var works = (await data.WorksAsync(ct))
                    .Where(w => w.FacilityName == group.Key).ToArray();
                if (works.Length == 0)
                    break;
                if (works.Length > 7)
                    throw new InvalidOperationException(
                        $"새 F9 기존 작업 정리: {group.Key} 대기열이 7칸을 초과합니다 · 입력 없이 정지");

                // The supported visual API has a safe all-completed receipt,
                // but NO confirmed per-work cancel operation. Never fabricate
                // an unsafe click or collect a partially completed facility.
                if (works.Any(w => !w.IsCompleted))
                {
                    if (checks++ % 12 == 0)
                        log?.Invoke($"[다중가공] 새 F9 이전 작업 정리 · {group.Key} " +
                            $"기존 {works.Length}건 완료 대기 · 새 목표 등록 0회");
                    await delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                log?.Invoke($"[다중가공] 새 F9 이전 작업 정리 · {group.Key} " +
                    $"완료된 {works.Length}건 전체 수령 후 새 목표 시작");
                bool collected = await screen.CollectAsync(
                    group.First(), AlteringFacilityEntryDirective.FreshMoveRequired, ct);
                if (!collected)
                    throw new InvalidOperationException(
                        $"새 F9 기존 작업 정리: {group.Key} 완료품 수령을 확인하지 못했습니다 · 재입력 없이 정지");
                ct.ThrowIfCancellationRequested();
                if ((await data.WorksAsync(ct)).Any(w => w.FacilityName == group.Key))
                    throw new InvalidOperationException(
                        $"새 F9 기존 작업 정리: {group.Key} 수령 후 대기열이 남았습니다 · 재수령 없이 정지");
                log?.Invoke($"[다중가공] 새 F9 이전 작업 정리 완료 · {group.Key} 대기열 0건");
                break;
            }
        }
        // Last read prevents taking a fresh baseline over works added elsewhere.
        var finalWorks = await data.WorksAsync(ct);
        if (finalWorks.Any(w => plans.Any(p => p.FacilityName == w.FacilityName)))
            throw new InvalidOperationException(
                "새 F9 이전 작업 정리 직후 선택 시설에 작업이 생겼습니다 · 새 목표 등록 중단");
    }
}
