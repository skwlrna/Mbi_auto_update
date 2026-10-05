namespace FishingAutomation;

/// <summary>
/// Batch scheduler for multi-altering. It owns no screen/input. Each facility is
/// treated as an independent seven-slot lane: fill a lane, leave it running, and
/// revisit it only after the whole current lane batch has completed (or is empty).
/// This keeps different facilities working in parallel without moving for every
/// single completed slot.
/// </summary>
internal sealed class MultiAlteringCoordinator
{
    internal event Action<string>? Log;

    internal async Task RunAsync(
        IReadOnlyList<AlteringPlan> plans,
        Func<AlteringPlan, CancellationToken, Task<bool>> runBatch,
        Func<CancellationToken, Task<IReadOnlyList<AlteringWork>>> readWorks,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runBatch);
        ArgumentNullException.ThrowIfNull(readWorks);
        ArgumentNullException.ThrowIfNull(delay);
        if (plans.Count == 0)
            throw new InvalidOperationException("다중가공 작업 목록이 비어 있습니다.");

        var seen = new HashSet<(string Facility, string Display, int Ordinal)>();
        var outputSeen = new HashSet<(string Facility, string Output)>();
        foreach (var plan in plans)
        {
            plan.Validate();
            var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
            if (!seen.Add(key))
                throw new InvalidOperationException(
                    $"다중가공 작업 목록에 같은 제법이 중복되어 있습니다: {plan.DisplayName}");

            // Works report the base output name, so two qualified recipes producing
            // the same output in one facility cannot be attributed safely.
            var outputKey = (plan.FacilityName, plan.OutputName);
            if (!outputSeen.Add(outputKey))
                throw new InvalidOperationException(
                    $"다중가공에서 같은 시설의 동일 결과물 제법을 동시에 구분할 수 없습니다: {plan.OutputName}");
        }

        var orderedFacilities = plans
            .Select(x => x.FacilityName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var completed = new HashSet<(string Facility, string Display, int Ordinal)>();

        (string Facility, string Display, int Ordinal) Key(AlteringPlan p)
            => (p.FacilityName, p.DisplayName, p.RecipeOrdinal);

        AlteringPlan? NextPlan(string facility)
            => plans.FirstOrDefault(x =>
                x.FacilityName == facility && !completed.Contains(Key(x)));

        Log?.Invoke(
            $"[다중가공] 병렬 배치 계획 확정 · {plans.Count}종 / {orderedFacilities.Length}시설 · " +
            string.Join(" → ", plans.Select(x => $"{x.DisplayName} {x.TargetQuantity}개")));
        Log?.Invoke("[다중가공] 운용 방식 · 시설별 최대 7칸 배치 등록 · 한 칸 완료마다 이동하지 않음 · 현재 배치 전체 완료 후 묶음 수령/재충전");

        while (completed.Count < plans.Count)
        {
            ct.ThrowIfCancellationRequested();
            var works = await readWorks(ct);
            bool acted = false;

            foreach (string facility in orderedFacilities)
            {
                ct.ThrowIfCancellationRequested();
                var plan = NextPlan(facility);
                if (plan is null)
                    continue;

                var facilityWorks = works
                    .Where(x => x.FacilityName == facility)
                    .ToArray();

                bool laneReady =
                    facilityWorks.Length == 0 ||
                    facilityWorks.All(x => x.IsCompleted);

                if (!laneReady)
                    continue;

                if (facilityWorks.Length > 0)
                    Log?.Invoke(
                        $"[다중가공] {plan.ScreenTitle} 배치 완료 · {facilityWorks.Length}건 모아서 수령 후 재충전");
                else
                    Log?.Invoke(
                        $"[다중가공] {plan.ScreenTitle} 빈 대기열 · {plan.DisplayName} 배치 등록 시작");

                bool planCompleted = await runBatch(plan, ct);
                acted = true;
                if (planCompleted)
                {
                    completed.Add(Key(plan));
                    Log?.Invoke(
                        $"[다중가공] 품목 완료 {completed.Count}/{plans.Count} · {plan.DisplayName}");

                    // If finishing the plan left this facility empty, seed the next
                    // queued plan in the same facility without leaving/revisiting it.
                    while (completed.Count < plans.Count)
                    {
                        works = await readWorks(ct);
                        if (works.Any(x => x.FacilityName == facility))
                            break;

                        var next = NextPlan(facility);
                        if (next is null)
                            break;

                        Log?.Invoke(
                            $"[다중가공] 같은 시설 다음 품목 즉시 배치 · {next.DisplayName}");
                        bool nextCompleted = await runBatch(next, ct);
                        if (!nextCompleted)
                            break;

                        completed.Add(Key(next));
                        Log?.Invoke(
                            $"[다중가공] 품목 완료 {completed.Count}/{plans.Count} · {next.DisplayName}");
                    }
                }

                // Refresh after each UI action so another facility decision never
                // uses stale queue state.
                works = await readWorks(ct);
            }

            if (completed.Count >= plans.Count)
                break;

            if (acted)
                continue;

            works = await readWorks(ct);
            var active = works
                .Where(x => orderedFacilities.Contains(x.FacilityName, StringComparer.Ordinal))
                .ToArray();

            // No facility is revisited merely because one slot completed. Wait until
            // its entire current batch drains to Completed, then collect in one trip.
            long waitSeconds = active
                .Where(x => x.State == "InProgress")
                .Select(x => x.RemainingSeconds)
                .DefaultIfEmpty(5)
                .Min();
            int completedSlots = active.Count(x => x.IsCompleted);
            int runningSlots = active.Count(x => !x.IsCompleted);
            Log?.Invoke(
                $"[다중가공] 병렬 대기 · 진행/대기 {runningSlots}건 · 완료 누적 {completedSlots}건 · " +
                $"다음 확인 약 {Math.Clamp(waitSeconds, 2, 30)}초 · 배치 전체 완료 전 이동 없음");

            await delay(
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 2, 30)),
                ct);
        }

        Log?.Invoke("[다중가공] 전체 작업 완료");
    }
}
