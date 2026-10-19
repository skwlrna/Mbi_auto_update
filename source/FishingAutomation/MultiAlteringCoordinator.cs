namespace FishingAutomation;

/// <summary>
/// Batch scheduler for multi-altering. Each facility is an independent seven-slot
/// lane. Different facilities run in parallel, while plans that share one facility
/// are completed in user order, one recipe at a time, using up to seven slots.
/// The whole current lane must complete before receipt; independent facilities
/// remain parallel. Existing pre-F9 works are not cancelled.
/// </summary>
// A coordinator-scoped watchdog survives RunBatchAsync boundaries. Each
// facility is tracked independently, so healthy progress elsewhere cannot
// hide a stalled seven-slot lane. Read-only CLI observations never authorize
// new screen input, receipt or a facility move.
// A read-only diagnostic for selected facilities left empty while another
// lane's full queue masks the recipe. Never registers, collects, or moves.
internal sealed class MultiAlteringLaneDeferralTracker
{
    private sealed record Observation(DateTimeOffset Since, string Signature, bool Warned);
    private readonly Dictionary<string, Observation> _blocked = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _threshold;

    internal MultiAlteringLaneDeferralTracker(
        TimeSpan? threshold = null, Func<DateTimeOffset>? now = null)
    {
        _threshold = threshold ?? TimeSpan.FromMinutes(2);
        if (_threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal string? ObserveBlocked(string facility, string recipe, string reason,
        int localWorks, int otherFullFacilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe);
        // A lane containing live work is not "missing"; let the normal
        // facility watchdog handle that work's countdown.
        if (localWorks != 0 || otherFullFacilities <= 0)
        {
            Reset(facility);
            return null;
        }
        var signature = recipe + "|" + reason;
        var now = _now();
        if (!_blocked.TryGetValue(facility, out var last) ||
            !string.Equals(signature, last.Signature, StringComparison.Ordinal))
        {
            _blocked[facility] = new(now, signature, false);
            return null;
        }
        if (last.Warned || now - last.Since < _threshold) return null;
        _blocked[facility] = last with { Warned = true };
        return $"[다중가공][시설 누락 감시] {facility} · {recipe} · " +
            $"빈 슬롯 0/7 · 대기 {Math.Max(0, (now - last.Since).TotalSeconds):0}초 · " +
            $"타 시설 포화 {otherFullFacilities}개 · CLI Reason={reason} · " +
            "재료 목록 미확정 · 읽기 전용 재조회 지속 · 임의 클릭/모두 받기 금지";
    }

    internal void Reset(string facility) => _blocked.Remove(facility);
}

internal sealed class MultiAlteringWaitWatchdog
{
    private sealed record Checkpoint(
        string Shape,
        long LowestRunningSeconds,
        DateTimeOffset LastProgressAt);

    private readonly Dictionary<string, Checkpoint> _last = new(StringComparer.Ordinal);
    private readonly TimeSpan _threshold;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset? _pauseStartedAt;

    // Only the scheduler's own stall clock pauses. Production remains active
    // on the server. Preserve the seconds already accrued before leaving, so
    // returning from an arbitrarily long Abyss run does not create a false
    // two-minute "stalled facility" alarm.
    internal void PauseForAbyss()
    {
        if (_pauseStartedAt.HasValue)
            throw new InvalidOperationException("다중가공 정체 감시가 이미 일시정지 상태입니다.");
        _pauseStartedAt = _now();
    }

    internal void ResumeAfterAbyssExit()
    {
        if (!_pauseStartedAt.HasValue)
            throw new InvalidOperationException("일시정지하지 않은 가공 정체 감시를 재개할 수 없습니다.");

        TimeSpan paused = _now() - _pauseStartedAt.Value;
        if (paused < TimeSpan.Zero)
            throw new InvalidOperationException("가공 정체 감시 시간 역행 감지 · 안전 정지");

        foreach (string facility in _last.Keys.ToArray())
        {
            Checkpoint checkpoint = _last[facility];
            _last[facility] = checkpoint with
            {
                LastProgressAt = checkpoint.LastProgressAt + paused
            };
        }
        _pauseStartedAt = null;
    }

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
    internal void Observe(string facility, IReadOnlyList<AlteringWork> works, bool deferTimeout = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        ArgumentNullException.ThrowIfNull(works);
        if (_pauseStartedAt.HasValue)
            throw new InvalidOperationException("어비스 진행 중 가공 정체 감시/입력 상태를 조회할 수 없습니다.");

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

        // Idle facilities have no active work to time. The owner ledger still
        // rejects an unexplained queue shrink; an empty wait is not progress.
        if (works.Count == 0)
        {
            _last.Remove(facility);
            return;
        }

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
        if (idle < _threshold || deferTimeout)
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
    private readonly Func<IReadOnlyList<AlteringPlan>, IReadOnlyList<AlteringPlan>>? _restoreCompleted;
    private readonly bool _fillInitialVacancies;
    private readonly bool _deferOtherLanesWhileQueueFull;

    internal event Action<string>? Log;

    internal MultiAlteringCoordinator(
        FacilityLaneState? laneState = null,
        FacilityLaneOwner laneOwner = FacilityLaneOwner.Main,
        Func<DateTimeOffset>? now = null,
        TimeSpan? idleThreshold = null,
        Func<IReadOnlyList<AlteringPlan>, IReadOnlyList<AlteringPlan>>? restoreCompleted = null,
        bool fillInitialVacancies = false,
        bool deferOtherLanesWhileQueueFull = false)
    {
        _laneState = laneState;
        _laneOwner = laneOwner;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _idleThreshold = idleThreshold ?? TimeSpan.FromMinutes(2);
        _restoreCompleted = restoreCompleted;
        _fillInitialVacancies = fillInitialVacancies;
        _deferOtherLanesWhileQueueFull = deferOtherLanesWhileQueueFull;
        if (_idleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleThreshold));
    }

    internal async Task RunAsync(
        IReadOnlyList<AlteringPlan> plans,
        Func<AlteringPlan, int, CancellationToken, Task<bool>> runBatch,
        Func<CancellationToken, Task<IReadOnlyList<AlteringWork>>> readWorks,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct,
        Func<AlteringPlan, CancellationToken, Task>? preparePlan = null,
        Func<CancellationToken, Task>? onIdleAbyssRound = null,
        long minimumAbyssWaitSeconds = AbyssAlteringSchedulePolicy.MinimumUsefulWaitSeconds,
        Func<CancellationToken, Task<IReadOnlyList<AlteringRecipe>>>? readRecipes = null)
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
        var completed = new HashSet<(string Facility, string Display, int Ordinal)>();
        var prepared = new HashSet<(string Facility, string Display, int Ordinal)>();
        // Keep diagnostic evidence when the CLI refusal reason changes. Avoid
        // repeating an identical line every 30-second observation cycle.
        var globalQueueBlocked = new Dictionary<string, string>(StringComparer.Ordinal);
        var globalQueueUnblocked = new HashSet<string>(StringComparer.Ordinal);
        var idleWatchdog = new MultiAlteringWaitWatchdog(_idleThreshold, _now);
        var deferrals = new MultiAlteringLaneDeferralTracker(_idleThreshold, _now);
        // Only the explicit new-F9 mode gets one initial pass into vacant
        // slots already occupied by old game jobs. Ordinary batching is unchanged.
        var startupVacancies = new HashSet<string>(
            orderedFacilities.Where(f => _fillInitialVacancies &&
                _laneState is not null &&
                _laneState.Snapshot(f).InitialObservedWorks > 0),
            StringComparer.Ordinal);

        (string Facility, string Display, int Ordinal) Key(AlteringPlan p)
            => (p.FacilityName, p.DisplayName, p.RecipeOrdinal);

        void RestoreCompleted()
        {
            if (_restoreCompleted is null) return;
            foreach (var restored in _restoreCompleted(plans))
            {
                if (!plans.Contains(restored))
                    throw new InvalidOperationException("복원 완료 품목이 현재 배치와 다릅니다.");
                completed.Add(Key(restored));
            }
        }
        RestoreCompleted();

        int FindNextPendingIndex(string facility)
        {
            // Stick to the FIRST unfinished recipe. Receipt plus durable
            // completion is mandatory before the next recipe is selected.
            var facilityPlans = plansByFacility[facility];
            for (int index = 0; index < facilityPlans.Length; index++)
                if (!completed.Contains(Key(facilityPlans[index])))
                    return index;
            return -1;
        }

        int PendingCount(string facility)
            => plansByFacility[facility].Count(x => !completed.Contains(Key(x)));

        Log?.Invoke(
            $"[다중가공] 시설별 품목 순차 배치 계획 확정 · {plans.Count}종 / {orderedFacilities.Length}시설 · " +
            string.Join(" → ", plans.Select(x => $"{x.DisplayName} {x.TargetQuantity}개")));
        Log?.Invoke(
            "[다중가공] 운용 방식 · 시설별 최대 7칸 · 같은 시설 여러 품목은 앞 품목 완료 후 다음 품목 · " +
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
                {
                    deferrals.Reset(facility);
                    continue;
                }
                var observed = works.Where(x => x.FacilityName == facility).ToArray();
                try
                {
                    // An all-completed lane needs one guarded manager turn for
                    // receipt. Do not reset its original stall deadline here.
                    idleWatchdog.Observe(facility, observed,
                        deferTimeout: observed.Length > 0 && observed.All(x => x.IsCompleted));
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
                bool initialVacancy = startupVacancies.Contains(facility) &&
                    facilityWorks.Length is > 0 and < 7;

                if (!laneReady && !initialVacancy)
                {
                    deferrals.Reset(facility);
                    continue;
                }

                // The first F9 CLI snapshot may report global queue_full just
                // because an unrelated facility has seven works. It is NOT a
                // lifetime embargo on an otherwise independent food/medicine
                // lane: re-query the currently selected recipe while slots are
                // available. Only affirmative live evidence may lift the mask.
                // MissingIngredients authorizes the existing resolver, NEVER
                // a registration click; AlteringAutomation rechecks Alterable
                // and live works before each actual UI operation.
                int otherFullLanes = orderedFacilities.Count(other =>
                    !string.Equals(other, facility, StringComparison.Ordinal) &&
                    works.Count(w => w.FacilityName == other) == 7);
                bool anotherFullLane = otherFullLanes > 0;
                if (_deferOtherLanesWhileQueueFull &&
                    facilityWorks.Length < 7 && anotherFullLane)
                {
                    if (readRecipes is null)
                        continue; // Legacy callers cannot prove independent readiness.

                    int next = FindNextPendingIndex(facility);
                    if (next < 0) continue;
                    var nextPlan = plansByFacility[facility][next];
                    var freshRecipes = await readRecipes(ct);
                    var selected = freshRecipes.Where(x =>
                        string.Equals(x.DisplayName, nextPlan.DisplayName,
                            StringComparison.Ordinal)).ToArray();
                    if (selected.Length < nextPlan.RecipeOrdinal ||
                        selected[nextPlan.RecipeOrdinal - 1].ProducedPerWork != nextPlan.ProducedPerWork ||
                        AlteringFacilityResolver.Resolve(
                            selected[nextPlan.RecipeOrdinal - 1]) != facility)
                        throw new InvalidOperationException(
                            $"전역 queue_full 재검증 중 제법 동일성 확인 실패 · {nextPlan.DisplayName} · 추가 등록 없이 정지");

                    var currentRecipe = selected[nextPlan.RecipeOrdinal - 1];
                    if (!currentRecipe.Alterable &&
                        currentRecipe.MissingIngredients.Count == 0)
                    {
                        string refusal = currentRecipe.Reason ?? "unknown";
                        string evidence = nextPlan.DisplayName + "\u001f" + refusal +
                            "\u001f" + facilityWorks.Length;
                        if (!globalQueueBlocked.TryGetValue(facility, out string? previous) ||
                            !string.Equals(previous, evidence, StringComparison.Ordinal))
                        {
                            globalQueueBlocked[facility] = evidence;
                            Log?.Invoke(
                                $"[다중가공] 독립 시설 보류 · {nextPlan.DisplayName} · " +
                                $"현재 CLI Reason={refusal} · 부족 재료 증거 없음 · " +
                                $"해당 시설 {facilityWorks.Length}/7칸, 타 시설 7/7칸 · " +
                                "다음 대기 주기에 실제 제법 재조회 · 무확인 등록 클릭 금지");
                        }
                        globalQueueUnblocked.Remove(facility);
                        string? starvation = deferrals.ObserveBlocked(
                            facility, nextPlan.DisplayName, refusal,
                            facilityWorks.Length, otherFullLanes);
                        if (starvation is not null) Log?.Invoke(starvation);
                        continue;
                    }

                    deferrals.Reset(facility);
                    globalQueueBlocked.Remove(facility);
                    if (globalQueueUnblocked.Add(facility))
                        Log?.Invoke(
                            $"[다중가공] 독립 시설 진행 승인 · {nextPlan.DisplayName} · " +
                            $"CLI {(currentRecipe.Alterable ? "가공 가능" : "구체적 부족 재료 있음")} · " +
                            $"해당 시설 {facilityWorks.Length}/7칸 · " +
                            "기존 화면·재료·등록 안전 검증 유지");
                }

                if (!(_deferOtherLanesWhileQueueFull &&
                    facilityWorks.Length < 7 && anotherFullLane))
                    deferrals.Reset(facility);

                if (initialVacancy)
                    Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} " +
                        $"기존 작업 그대로 · 빈 {7 - facilityWorks.Length}칸에 새 F9 등록");
                else if (facilityWorks.Length > 0)
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 배치 완료 · " +
                        $"{facilityWorks.Length}건 모아서 수령 후 같은 품목 재충전");
                else
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 빈 대기열 · 현재 품목 배치 등록 시작");

                int idleSelections = 0;

                while (PendingCount(facility) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    // A facility-wide receipt can complete another main item
                    // inside this turn or a recursive dependency. Rehydrate before
                    // selecting the active recipe, not just at startup.
                    RestoreCompleted();
                    if (PendingCount(facility) == 0) break;
                    works = await readWorks(ct);
                    facilityWorks = works.Where(x => x.FacilityName == facility).ToArray();
                    _laneState?.Observe(facility, facilityWorks, allowShrink: false);
                    bool completedLaneNeedsCollection =
                        facilityWorks.Length > 0 &&
                        facilityWorks.All(x => x.IsCompleted);
                    // A fast item may complete while the 7-slot mixed lane is
                    // still being seeded. Do not call an Automation whose entry
                    // could attempt a prohibited partial "collect all". Leave the
                    // running slots intact and revisit at the full batch boundary.
                    if (AlteringReceiptPolicy.IsPartialManagedFacility(facilityWorks, facility) &&
                        !initialVacancy)
                    {
                        Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} 부분 완료 · " +
                                    "수령/추가 등록 보류 · 시설 전체 완료까지 다른 시설로 양보");
                        break;
                    }
                    if (facilityWorks.Length >= 7 && !completedLaneNeedsCollection)
                        break;

                    int pendingBefore = PendingCount(facility);
                    int index = FindNextPendingIndex(facility);
                    if (index < 0)
                        break;

                    var plan = plansByFacility[facility][index];
                    int beforeCount = facilityWorks.Length;
                    // Snapshot the facility-specific durable registration evidence.
                    // CLI work counts have no stable work IDs: a guarded receipt
                    // followed by registration can still look like 1 -> 1.
                    var registrationBefore = _laneState?.Snapshot(facility);

                    // Keep the existing proven one-slot engine and receipt
                    // guards. Prepare materials just once, before the FIRST
                    // registration of each selected recipe.
                    _laneState?.AssertAccess(facility, _laneOwner);
                    if (!prepared.Contains(Key(plan)))
                    {
                        if (preparePlan is not null)
                        {
                            Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} · {plan.DisplayName} 새 품목 재료 점검");
                            await preparePlan(plan, ct);
                        }
                        // A failed/cancelled preparation never authorizes a click.
                        prepared.Add(Key(plan));
                    }
                    bool planCompleted = await runBatch(plan, 1, ct);
                    acted = true;

                    if (planCompleted)
                    {
                        RestoreCompleted();
                        if (_restoreCompleted is not null && !completed.Contains(Key(plan)))
                            throw new InvalidOperationException("품목 완료의 영구 저장 증거 부족 · 안전 정지");
                        completed.Add(Key(plan));
                        Log?.Invoke(
                            $"[다중가공] 품목 처리 종료 {completed.Count}/{plans.Count} · {plan.DisplayName} · " +
                            "목표 전량 완료 또는 원재료 소진");
                    }

                    works = await readWorks(ct);
                    var afterFacilityWorks = works
                        .Where(x => x.FacilityName == facility)
                        .ToArray();
                    // Guarded receipt callbacks must reconcile their own verified
                    // shrink before control returns here. An unproven drop during
                    // runBatch must not be silently accepted as a valid receipt.
                    _laneState?.Observe(facility, afterFacilityWorks, allowShrink: false);
                    int afterCount = afterFacilityWorks.Length;
                    var registrationAfter = _laneState?.Snapshot(facility);
                    bool confirmedRegistration =
                        registrationBefore is not null &&
                        registrationAfter is not null &&
                        (registrationAfter.MainRegisteredWorks > registrationBefore.MainRegisteredWorks ||
                         registrationAfter.IntermediateRegisteredWorks > registrationBefore.IntermediateRegisteredWorks);

                    if (planCompleted || confirmedRegistration || afterCount != beforeCount)
                    {
                        // Only verified manager progress refreshes this facility:
                        // CLI-confirmed NoteRegistration, a guarded queue change
                        // (including verified receipt), or durable plan completion.
                        // Never reset just because RunBatch returned or time passed.
                        idleWatchdog.ConfirmManagerProgress(facility);
                        if (confirmedRegistration && afterCount == beforeCount && !planCompleted)
                            Log?.Invoke(
                                $"[다중가공] {facility.Replace(" 시설", "")} 정체 감시 초기화 · " +
                                $"CLI 확인 신규 등록 · 시설 작업수 {beforeCount}->{afterCount} 동일 · " +
                                $"메인등록 {registrationBefore!.MainRegisteredWorks}->{registrationAfter!.MainRegisteredWorks} · " +
                                $"중간재료등록 {registrationBefore.IntermediateRegisteredWorks}->{registrationAfter.IntermediateRegisteredWorks}");
                        idleSelections = 0;
                    }
                    else
                    {
                        // A no-op manager must still enforce the ORIGINAL deadline.
                        // Never allow unbounded retries or a read-only CLI to
                        // authorize receipt, Space, or movement.
                        try
                        {
                            idleWatchdog.Observe(facility, afterFacilityWorks);
                        }
                        catch (InvalidOperationException)
                        {
                            _laneState?.InvalidateOnsite(
                                "시설별 대기 정체 · " + facility + " · 화면 재확인 필요");
                            throw;
                        }
                        idleSelections++;
                        if (idleSelections >= Math.Max(1, pendingBefore))
                        {
                            Log?.Invoke(
                                $"[다중가공] {facility.Replace(" 시설", "")} 단일 품목 추가 등록 없음 · " +
                                "남은 품목은 이미 전량 등록되었거나 현재 슬롯 상태를 기다리는 중");
                            break;
                        }
                    }
                }

                // Never revisit a partially completed batch to top up slots
                // merely because a job finished after this initial fill pass.
                startupVacancies.Remove(facility);
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

            // No crafting, receipt, or movement is in flight here. This callback
            // owns all input until the abyss has EXITED to the verified outside HUD.
            // Immediately re-query works afterwards; never act on stale observations.
            if (onIdleAbyssRound is not null &&
                AbyssAlteringSchedulePolicy.CanRun(active, minimumAbyssWaitSeconds))
            {
                Log?.Invoke($"[병행] 모든 진행 중 가공 {minimumAbyssWaitSeconds}초 이상 남음 -> 가공 감시 일시정지 · 어비스에 입력권 양도");
                // Suspend only manager/watchdog clock. A different-length
                // dungeon is fine: no scheduler countdown can end the Abyss.
                idleWatchdog.PauseForAbyss();
                await onIdleAbyssRound(ct); // MUST verify outside HUD before returning
                ct.ThrowIfCancellationRequested();
                // Never resume processing on exit failure/F10 cancellation.
                idleWatchdog.ResumeAfterAbyssExit();
                Log?.Invoke("[병행] 던전 밖 복귀 확정 -> 가공 감시 재개 · 실제 서버 가공 대기열 즉시 재조회");
                continue;
            }

            await delay(
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 30)),
                ct);
        }

        Log?.Invoke("[다중가공] 전체 작업 완료");
    }
}
