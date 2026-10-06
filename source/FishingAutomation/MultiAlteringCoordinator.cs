namespace FishingAutomation;

/// <summary>
/// Batch scheduler for multi-altering. Each facility is an independent seven-slot
/// lane. Different facilities run in parallel, while plans that share one facility
/// are round-robin mixed into the same seven-slot batch. A facility is revisited only
/// after its whole current lane has completed, so one completed slot never causes an
/// extra trip.
/// </summary>
internal sealed class MultiAlteringCoordinator
{
    internal event Action<string>? Log;

    internal async Task RunAsync(
        IReadOnlyList<AlteringPlan> plans,
        Func<AlteringPlan, int, CancellationToken, Task<bool>> runBatch,
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
        var plansByFacility = orderedFacilities.ToDictionary(
            facility => facility,
            facility => plans.Where(x => x.FacilityName == facility).ToArray(),
            StringComparer.Ordinal);
        var nextPlanIndex = orderedFacilities.ToDictionary(
            facility => facility,
            _ => 0,
            StringComparer.Ordinal);
        var completed = new HashSet<(string Facility, string Display, int Ordinal)>();

        (string Facility, string Display, int Ordinal) Key(AlteringPlan p)
            => (p.FacilityName, p.DisplayName, p.RecipeOrdinal);

        int FindNextPendingIndex(string facility)
        {
            var facilityPlans = plansByFacility[facility];
            int start = nextPlanIndex[facility] % facilityPlans.Length;
            for (int offset = 0; offset < facilityPlans.Length; offset++)
            {
                int index = (start + offset) % facilityPlans.Length;
                if (!completed.Contains(Key(facilityPlans[index])))
                    return index;
            }
            return -1;
        }

        int PendingCount(string facility)
            => plansByFacility[facility].Count(x => !completed.Contains(Key(x)));

        Log?.Invoke(
            $"[다중가공] 혼합 병렬 배치 계획 확정 · {plans.Count}종 / {orderedFacilities.Length}시설 · " +
            string.Join(" → ", plans.Select(x => $"{x.DisplayName} {x.TargetQuantity}개")));
        Log?.Invoke(
            "[다중가공] 운용 방식 · 시설별 최대 7칸 · 같은 시설 여러 품목은 라운드로빈 혼합 · " +
            "한 칸 완료마다 이동하지 않음 · 현재 배치 전체 완료 후 묶음 수령/재충전");

        while (completed.Count < plans.Count)
        {
            ct.ThrowIfCancellationRequested();
            var works = await readWorks(ct);
            bool acted = false;

            foreach (string facility in orderedFacilities)
            {
                ct.ThrowIfCancellationRequested();
                if (PendingCount(facility) == 0)
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
                        $"[다중가공] {facility.Replace(" 시설", "")} 배치 완료 · " +
                        $"{facilityWorks.Length}건 모아서 수령 후 혼합 재충전");
                else
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 빈 대기열 · 혼합 배치 등록 시작");

                int idleSelections = 0;

                while (PendingCount(facility) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    works = await readWorks(ct);
                    facilityWorks = works.Where(x => x.FacilityName == facility).ToArray();
                    if (facilityWorks.Length >= 7)
                        break;

                    int pendingBefore = PendingCount(facility);
                    int index = FindNextPendingIndex(facility);
                    if (index < 0)
                        break;

                    var plan = plansByFacility[facility][index];
                    int beforeCount = facilityWorks.Length;

                    // One registration per turn is deliberate. Repeated turns fill
                    // the seven-slot lane A/B/A/B... without changing the proven
                    // single-plan registration/receipt implementation.
                    bool planCompleted = await runBatch(plan, 1, ct);
                    acted = true;
                    nextPlanIndex[facility] = (index + 1) % plansByFacility[facility].Length;

                    if (planCompleted)
                    {
                        completed.Add(Key(plan));
                        Log?.Invoke(
                            $"[다중가공] 품목 완료 {completed.Count}/{plans.Count} · {plan.DisplayName}");
                    }

                    works = await readWorks(ct);
                    int afterCount = works.Count(x => x.FacilityName == facility);

                    if (planCompleted || afterCount != beforeCount)
                    {
                        idleSelections = 0;
                    }
                    else
                    {
                        idleSelections++;
                        if (idleSelections >= Math.Max(1, pendingBefore))
                        {
                            Log?.Invoke(
                                $"[다중가공] {facility.Replace(" 시설", "")} 혼합 배치 추가 등록 없음 · " +
                                "남은 품목은 이미 전량 등록되었거나 현재 슬롯 상태를 기다리는 중");
                            break;
                        }
                    }
                }

                works = await readWorks(ct);
                facilityWorks = works.Where(x => x.FacilityName == facility).ToArray();
                if (facilityWorks.Length > 0)
                {
                    string composition = string.Join(", ",
                        facilityWorks
                            .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
                            .Select(g => $"{g.Key} {g.Count()}칸"));
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 현재 배치 {facilityWorks.Length}/7 · {composition}");
                }
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
