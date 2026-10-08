namespace FishingAutomation;

/// <summary>
/// Batch scheduler for multi-altering. Each facility is an independent seven-slot
/// lane. Different facilities run in parallel, while plans that share one facility
/// are round-robin mixed into the same seven-slot batch. A facility is revisited only
/// after its whole current lane has completed, so one completed slot never causes an
/// extra trip.
/// </summary>
// A coordinator-scoped watchdog survives RunBatchAsync boundaries. Each
// facility is tracked independently, so healthy progress elsewhere cannot
// hide a stalled seven-slot lane. Read-only CLI observations never authorize
// new screen input, receipt or a facility move.
internal sealed class MultiAlteringWaitWatchdog
{
    private sealed record Checkpoint(
        string Shape,
        long LowestRunningSeconds,
        DateTimeOffset LastProgressAt);

    private readonly Dictionary<string, Checkpoint> _last = new(StringComparer.Ordinal);
    private readonly TimeSpan _threshold;
    private readonly Func<DateTimeOffset> _now;

    internal MultiAlteringWaitWatchdog(
        TimeSpan threshold,
        Func<DateTimeOffset>? now = null)
    {
        if (threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        _threshold = threshold;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // The CLI exposes no stable work IDs. Compare sorted recipe/status shapes
    // and the lowest in-progress countdown; a decreasing countdown is real
    // evidence of progress even if the queue count remains seven.
    internal void Observe(string facility, IReadOnlyList<AlteringWork> works)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        ArgumentNullException.ThrowIfNull(works);

        string shape = string.Join("|",
            works.OrderBy(x => x.DisplayName, StringComparer.Ordinal)
                .ThenBy(x => x.State, StringComparer.Ordinal)
                .ThenBy(x => x.IsCompleted)
                .Select(x => $"{x.DisplayName}:{x.State}:{x.IsCompleted}"));
        long lowestRunning = works
            .Where(x => !x.IsCompleted && x.State == "InProgress")
            .Select(x => x.RemainingSeconds)
            .DefaultIfEmpty(long.MaxValue)
            .Min();

        DateTimeOffset now = _now();
        if (!_last.TryGetValue(facility, out var previous) ||
            !string.Equals(shape, previous.Shape, StringComparison.Ordinal))
        {
            _last[facility] = new(shape, lowestRunning, now);
            return;
        }

        // A timer jumping back UP is not proof of work being completed. Do not
        // keep resetting the watchdog on a noisy CLI countdown.
        if (lowestRunning < previous.LowestRunningSeconds)
        {
            _last[facility] = new(shape, lowestRunning, now);
            return;
        }

        TimeSpan idle = now - previous.LastProgressAt;
        if (idle < _threshold)
            return;

        int completed = works.Count(x => x.IsCompleted);
        string remaining = lowestRunning == long.MaxValue
            ? "진행 타이머 없음"
            : $"최소 진행 남은시간 {lowestRunning}초";
        throw new InvalidOperationException(
            $"다중가공 시설별 정체 감지 · {facility} · CLI 작업 {works.Count}건 " +
            $"(완료 {completed}건) · {remaining} · " +
            $"실질 변화 없이 {Math.Max(0, idle.TotalSeconds):0}초 경과 " +
            $"(한도 {_threshold.TotalSeconds:0}초). " +
            "임의 설비 이동/모두 받기/Space 재시도 없이 안전 정지합니다.");
    }

    // Successful managed registration/collection or plan completion is
    // authoritative progress even if a same-count batch replaces old works.
    internal void ConfirmManagerProgress(string facility)
        => _last.Remove(facility);
}

internal sealed class MultiAlteringCoordinator
{
    private readonly FacilityLaneState? _laneState;
    private readonly FacilityLaneOwner _laneOwner;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _idleThreshold;

    internal event Action<string>? Log;

    internal MultiAlteringCoordinator(
        FacilityLaneState? laneState = null,
        FacilityLaneOwner laneOwner = FacilityLaneOwner.Main,
        Func<DateTimeOffset>? now = null,
        TimeSpan? idleThreshold = null)
    {
        _laneState = laneState;
        _laneOwner = laneOwner;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _idleThreshold = idleThreshold ?? TimeSpan.FromMinutes(2);
        if (_idleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleThreshold));
    }

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
        var idleWatchdog = new MultiAlteringWaitWatchdog(_idleThreshold, _now);

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

            // Persist per-facility evidence across all 1-slot RunBatch returns.
            // Never reset just because the scheduler went around another loop.
            foreach (string facility in orderedFacilities)
            {
                if (PendingCount(facility) == 0)
                    continue;
                var observed = works.Where(x => x.FacilityName == facility).ToArray();
                try
                {
                    idleWatchdog.Observe(facility, observed);
                }
                catch (InvalidOperationException ex)
                {
                    // A stalled facility is no longer a safe onsite assumption.
                    // Invalidate its shared authority and surface the CLI-only
                    // diagnostic; never start an autonomous movement or receipt.
                    _laneState?.InvalidateOnsite(
                        $"시설별 대기 정체 · {facility} · 화면 재확인 필요");
                    Log?.Invoke($"[다중가공] {ex.Message}");
                    throw;
                }
            }

            foreach (string facility in orderedFacilities)
            {
                ct.ThrowIfCancellationRequested();
                if (PendingCount(facility) == 0)
                    continue;

                var facilityWorks = works
                    .Where(x => x.FacilityName == facility)
                    .ToArray();
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);

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
                    _laneState?.Observe(facility, facilityWorks, allowShrink: false);
                    bool completedLaneNeedsCollection =
                        facilityWorks.Length > 0 &&
                        facilityWorks.All(x => x.IsCompleted);
                    if (facilityWorks.Length >= 7 && !completedLaneNeedsCollection)
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
                    _laneState?.AssertAccess(facility, _laneOwner);
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
                    var afterFacilityWorks = works
                        .Where(x => x.FacilityName == facility)
                        .ToArray();
                    _laneState?.Observe(facility, afterFacilityWorks, allowShrink: true);
                    int afterCount = afterFacilityWorks.Length;

                    if (planCompleted || afterCount != beforeCount)
                    {
                        // Count changes or confirmed completion prove a manager
                        // action, not merely another idle poll.
                        idleWatchdog.ConfirmManagerProgress(facility);
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
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);
                if (facilityWorks.Length > 0)
                {
                    string composition = string.Join(", ",
                        facilityWorks
                            .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
                            .Select(g => $"{g.Key} {g.Count()}칸"));
                    string ownership = _laneState is null
                        ? ""
                        : $" · 소유권 {_laneState.Describe(facility)}";
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 현재 배치 {facilityWorks.Length}/7 · {composition}{ownership}");
                }
            }

            if (completed.Count >= plans.Count)
                break;

            if (acted)
                continue;

            works = await readWorks(ct);
            foreach (string facility in orderedFacilities)
            {
                var facilityWorks = works
                    .Where(x => x.FacilityName == facility)
                    .ToArray();
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);
            }

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
                $"다음 확인 약 {Math.Clamp(waitSeconds, 1, 30)}초 · 배치 전체 완료 전 이동 없음");

            await delay(
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 30)),
                ct);
        }

        Log?.Invoke("[다중가공] 전체 작업 완료");
    }
}
