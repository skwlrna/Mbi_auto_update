namespace FishingAutomation;

// Cooperative pause issued BEFORE this dependency scheduler acquires any facility
// lease, registers a job, or receives work. A parent F9 manager can service
// unrelated facilities and retry only after fresh CLI evidence.
internal sealed class IntermediateFacilityBusyException : Exception
{
    internal string FacilityName { get; }
    internal IntermediateFacilityBusyException(string facilityName)
        : base("중간재료 시설의 기존 배치가 아직 완료되지 않았습니다: " + facilityName)
        => FacilityName = facilityName;
}

internal interface IAlteringDependencyScheduler
{
    Task RunAsync(
        AlteringPlan requestedPlan,
        long requestBaselineQuantity,
        int requiredAdditionalQuantity,
        IAlteringSupplyResolver resolver,
        CancellationToken ct,
        int? minimumAdditionalQuantity = null);
}

/// <summary>
/// Runs recursive intermediate processing through the same seven-slot facility batch
/// policy used by multi-altering. It never enters a facility while any slot in that
/// facility is still running. Once the current facility batch is fully completed, it
/// receives that batch through the existing guarded receipt path, rechecks inventory,
/// and only then owns the empty lane for the intermediate material.
/// </summary>
internal sealed class MultiAlteringDependencyScheduler : IAlteringDependencyScheduler
{
    private readonly IAlteringData _data;
    private readonly IAlteringScreen _screen;
    private readonly CliIdentityContext _identity;
    private readonly string _sessionDirectory;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _verificationAttempts;
    private readonly FacilityLaneState? _laneState;
    private readonly IAlteringInternalConsumptionObserver? _internalConsumptionObserver;
    private readonly Func<DateTimeOffset> _boundaryNow;
    private readonly TimeSpan _boundaryIdleThreshold;
    private readonly Func<AlteringPlan, CancellationToken, Task>? _beforeReceipt;
    private readonly Func<AlteringPlan, CancellationToken, Task>? _afterReceipt;
    private readonly bool _yieldBusyFacilityToParent;

    internal event Action<string>? Log;

    internal MultiAlteringDependencyScheduler(
        IAlteringData data,
        IAlteringScreen screen,
        CliIdentityContext identity,
        string sessionDirectory,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int verificationAttempts = 120,
        FacilityLaneState? laneState = null,
        IAlteringInternalConsumptionObserver? internalConsumptionObserver = null,
        Func<DateTimeOffset>? boundaryNow = null,
        TimeSpan? boundaryIdleThreshold = null,
        Func<AlteringPlan, CancellationToken, Task>? beforeReceipt = null,
        Func<AlteringPlan, CancellationToken, Task>? afterReceipt = null,
        bool yieldBusyFacilityToParent = false)
    {
        _data = data;
        _screen = screen;
        _identity = identity;
        _sessionDirectory = sessionDirectory;
        _delay = delay ?? Task.Delay;
        _verificationAttempts = Math.Clamp(verificationAttempts, 1, 120);
        _laneState = laneState;
        _internalConsumptionObserver = internalConsumptionObserver;
        _boundaryNow = boundaryNow ?? (() => DateTimeOffset.UtcNow);
        _boundaryIdleThreshold = boundaryIdleThreshold ?? TimeSpan.FromMinutes(2);
        _beforeReceipt = beforeReceipt;
        _afterReceipt = afterReceipt;
        _yieldBusyFacilityToParent = yieldBusyFacilityToParent;
        if (_boundaryIdleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(boundaryIdleThreshold));
    }

    public async Task RunAsync(
        AlteringPlan requestedPlan,
        long requestBaselineQuantity,
        int requiredAdditionalQuantity,
        IAlteringSupplyResolver resolver,
        CancellationToken ct,
        int? minimumAdditionalQuantity = null)
    {
        if (requiredAdditionalQuantity <= 0)
            return;
        int minimum = minimumAdditionalQuantity ?? requiredAdditionalQuantity;
        if (minimum <= 0 || minimum > requiredAdditionalQuantity)
            throw new ArgumentOutOfRangeException(nameof(minimumAdditionalQuantity));

        requestedPlan.Validate();
        string dependencyDir = Path.Combine(_sessionDirectory, "dependencies");
        var store = new AlteringSessionStore(
            AlteringSessionStore.MultiPlanPath(dependencyDir, requestedPlan));
        string ownerKey = $"{requestedPlan.DisplayName}#{requestedPlan.RecipeOrdinal}";
        bool resumingOwnedBatch = _yieldBusyFacilityToParent &&
            _laneState?.IsActiveIntermediateOwner(requestedPlan.FacilityName, ownerKey) == true;
        // Cooperative suspension is permitted only for one top-level child
        // lease. Nested intermediate owners cannot be reconstructed safely.
        if (resumingOwnedBatch &&
            _laneState!.Snapshot(requestedPlan.FacilityName).IntermediateDepth != 1)
            throw new InvalidOperationException(
                "중첩 중간재료 작업의 자동 복귀는 지원되지 않습니다 · 소유권 보존 후 정지");

        Log?.Invoke(
            $"[중간재료 스케줄] 요청 · {requestedPlan.DisplayName} 추가 {requiredAdditionalQuantity:N0}개 · " +
            $"{requestedPlan.FacilityName.Replace(" 시설", "")} 시설 배치에 통합");

        // A previously queued child already owns its lane. Re-collecting its
        // completed jobs here as an unrelated 'existing batch' would violate
        // its durable receipt ledger and orphan the retained lease.
        var boundaryWorks = resumingOwnedBatch
            ? Array.Empty<AlteringWork>()
            : await WaitForFacilityBatchBoundaryAsync(
                requestedPlan, ct, allowCooperativeYield: true);
        if (resumingOwnedBatch)
            Log?.Invoke($"[중간재료 스케줄] {requestedPlan.FacilityName} 등록된 자식 배치 이어받기 · " +
                "기존 소유권/세션 유지 · 신규 등록 전 CLI 장부 재검증");
        if (boundaryWorks.Length > 0)
        {
            Log?.Invoke(
                $"[중간재료 스케줄] {requestedPlan.FacilityName.Replace(" 시설", "")} 기존 배치 " +
                $"{boundaryWorks.Length}건 전체 완료 확인 · 기존 안전 수령 후 중간재료 배치 시작");

            var collector = new AlteringAutomation(
                _data,
                _screen,
                _delay,
                _verificationAttempts,
                facilityState: _laneState,
                beforeReceipt: _beforeReceipt,
                afterReceipt: _afterReceipt);
            collector.Log += text => Log?.Invoke(text);

            if (!await collector.CollectReadyBatchAsync(requestedPlan, ct))
                throw new InvalidOperationException(
                    $"{requestedPlan.FacilityName}의 완료 배치를 중간재료 전환 전에 수령하지 못했습니다.");

            var afterCollection = await _data.WorksAsync(ct);
            var afterFacilityCollection = afterCollection
                .Where(x => x.FacilityName == requestedPlan.FacilityName)
                .ToArray();
            _laneState?.Observe(
                requestedPlan.FacilityName,
                afterFacilityCollection,
                allowShrink: true);
            if (afterFacilityCollection.Length > 0)
                throw new InvalidOperationException(
                    $"{requestedPlan.FacilityName} 완료 배치 수령 후에도 작업이 남아 있어 중간재료가 시설을 소유하지 않습니다.");

            // The collector's safe receipt path confirms that the facility UI
            // returned; Observe above proves that the entire seven-slot batch
            // was received. Only the shared manager may record onsite authority.
            // Without this handoff the very next intermediate registration
            // incorrectly receives FreshMoveRequired and repeats facility travel.
            _laneState?.ConfirmOnsite(
                requestedPlan.FacilityName,
                "중간재료 전환 경계 · 완료 배치 수령 및 시설 작업 0건 확인 · 같은 시설 재사용");
        }

        long current = await _data.ItemCountAsync(requestedPlan.OutputName, ct);
        if (current < requestBaselineQuantity)
            throw new InvalidOperationException(
                $"{requestedPlan.OutputName} 중간재료 계산 중 보유량이 감소했습니다: " +
                $"요청 기준 {requestBaselineQuantity:N0} / 현재 {current:N0}. " +
                "필요 수량을 안전하게 재계산할 수 없어 정지합니다.");

        long alreadyGained = current - requestBaselineQuantity;
        long remainingLong = Math.Max(0, requiredAdditionalQuantity - alreadyGained);
        if (remainingLong == 0)
        {
            // The whole request was satisfied by receiving the existing facility
            // batch. No new dependency is needed; a checkpoint left by a prior
            // cancellation must not be resumed by a later request for this recipe.
            // Only clean up the same character's matching recipe checkpoint.
            var completedSession = store.Load();
            if (completedSession is not null &&
                completedSession.MatchesIdentity(_identity) &&
                completedSession.FacilityName == requestedPlan.FacilityName &&
                completedSession.DisplayName == requestedPlan.DisplayName)
            {
                store.Delete();
                Log?.Invoke(
                    $"[중간재료 스케줄] 기존 완료 배치로 충족 · 오래된 중간재료 이어하기 기록 삭제 · " +
                    requestedPlan.DisplayName);
            }

            Log?.Invoke(
                $"[중간재료 스케줄] 기존 완료 배치 수령으로 부족량 해소 · " +
                $"{requestedPlan.OutputName} +{alreadyGained:N0}개 · 추가 등록 없음");
            return;
        }

        if (remainingLong > int.MaxValue)
            throw new InvalidOperationException(
                $"{requestedPlan.OutputName} 중간재료 남은 수량이 안전 범위를 넘었습니다: {remainingLong:N0}");

        var plan = requestedPlan with { TargetQuantity = (int)remainingLong };
        // A resumed lease MUST have the exact saved job identity. Mismatched
        // quantities cannot delete/recreate a journal while work is queued.
        var saved = store.Load();
        if (resumingOwnedBatch &&
            (saved is null || !saved.MatchesPlan(plan) ||
             !saved.MatchesIdentity(_identity)))
            throw new InvalidOperationException(
                "중간재료 이어하기 소유권과 저장 장부가 일치하지 않습니다 · 새 등록 없이 정지");

        var emptyCheck = await _data.WorksAsync(ct);
        var emptyFacilityCheck = emptyCheck
            .Where(x => x.FacilityName == plan.FacilityName)
            .ToArray();
        if (resumingOwnedBatch)
        {
            // Count or identity conflicts still fail closed via shared lane
            // observations and the immutable child session journal.
            _laneState!.AssertAccess(plan.FacilityName, FacilityLaneOwner.Intermediate);
            _laneState.Observe(plan.FacilityName, emptyFacilityCheck, allowShrink: false);
        }
        else
        {
            if (emptyFacilityCheck.Length > 0)
                throw new InvalidOperationException(
                    $"{plan.FacilityName} 중간재료 배치 시작 직전에 다른 작업이 생겨 시설 소유권을 확보하지 못했습니다.");
            _laneState?.AcquireIntermediate(
                plan.FacilityName, emptyFacilityCheck, ownerKey);
        }

        Directory.CreateDirectory(dependencyDir);

        AlteringSessionState session;
        if (saved is not null &&
            saved.MatchesPlan(plan) &&
            saved.MatchesIdentity(_identity))
        {
            session = saved;
            Log?.Invoke(
                $"[중간재료 스케줄] 이어하기 · {plan.DisplayName} · " +
                $"등록 {saved.QueuedWorks}/{saved.RequiredWorks}");
        }
        else
        {
            if (saved is not null)
            {
                store.Delete();
                Log?.Invoke(
                    $"[중간재료 스케줄] 이전 중간재료 세션 불일치 · {plan.DisplayName} 새 세션 생성");
            }

            session = AlteringSessionState.Create(
                plan,
                _identity,
                current,
                initialExistingWorks: 0);
            store.Save(session);
        }

        var automation = new AlteringAutomation(
            _data,
            _screen,
            _delay,
            _verificationAttempts,
            resolver,
            store,
            session,
            // F05: child registration may consume a still-pending main output.
            // Credit the same durable producer session via its shared observer.
            internalConsumptionObserver: _internalConsumptionObserver,
            onConfirmedReceipt: (receiptPlan, remainingWorks) =>
                _laneState?.Observe(
                    receiptPlan.FacilityName,
                    remainingWorks,
                    allowShrink: true),
            facilityState: _laneState,
            beforeReceipt: _beforeReceipt,
            afterReceipt: _afterReceipt);
        automation.Log += text => Log?.Invoke(text);

        var coordinator = new MultiAlteringCoordinator(
            _laneState,
            FacilityLaneOwner.Intermediate,
            yieldIntermediateActiveBatch: _yieldBusyFacilityToParent);
        coordinator.Log += text => Log?.Invoke(text);

        Log?.Invoke(
            $"[중간재료 스케줄] 메인과 동일한 시설 7칸 Coordinator 시작 · " +
            $"{plan.DisplayName} {plan.RequiredWorks}작업");

        MaterialSupplyHoldException? materialHold = null;
        try
        {
            await coordinator.RunAsync(
                new[] { plan },
                async (job, slotBudget, token) =>
                {
                    int registrationsBefore = automation.ConfirmedRegistrationsThisRun;
                    var result = await automation.RunBatchAsync(job, slotBudget, token);
                    int registered = automation.ConfirmedRegistrationsThisRun - registrationsBefore;
                    _laneState?.NoteRegistration(
                        job,
                        FacilityLaneOwner.Intermediate,
                        registered);
                    // Only the leather lane may stop an oversized batch on a
                    // newly verified external raw shortage. The minimum one
                    // parent-ticket supply is checked after safe receipt; no
                    // unverified partial production is credited.
                    return result == AlteringRunResult.Completed ||
                           job.FacilityName == "가죽 가공 시설" &&
                           result == AlteringRunResult.RawMaterialExhausted;
                },
                _data.WorksAsync,
                _delay,
                ct);
        }
        catch (MaterialSupplyHoldException hold)
            when (plan.FacilityName == "가죽 가공 시설" &&
                  LeatherRawMaterialPolicy.CanDrainOnHold(hold.Evidence))
        {
            // The 17:58 incident already had four registered child jobs.
            // Never return control to the Main owner or clear the lease while
            // they are outstanding. Stop registrations, wait for the WHOLE
            // batch, and reuse the original journalled receipt implementation.
            materialHold = hold;
            Log?.Invoke($"[중간재료 스케줄] {plan.DisplayName} 신규 등록 보류 · " +
                $"분류={hold.Evidence.Category} · {hold.Evidence.Item} " +
                $"{hold.Evidence.Observed}/{hold.Evidence.Required} · " +
                "중간재료 소유권 유지 · 등록 작업 전체 완료/수령 대기");
            var pending = await WaitForFacilityBatchBoundaryAsync(plan, ct);
            if (pending.Length > 0)
            {
                _laneState?.AssertAccess(plan.FacilityName, FacilityLaneOwner.Intermediate);
                if (!await automation.CollectReadyBatchAsync(plan, ct))
                    throw new InvalidOperationException(
                        "중간재료 보류 배치 수령 미확정 · 소유권 유지 및 메인 복귀 차단", hold);
                var afterReceipt = await _data.WorksAsync(ct);
                _laneState?.Observe(plan.FacilityName, afterReceipt, allowShrink: true);
                _laneState?.ConfirmOnsite(plan.FacilityName,
                    "중간재료 등록 보류 · 전체 작업 수령 및 시설 복귀 확인");
            }
        }

        var finalWorks = await _data.WorksAsync(ct);
        var finalFacilityWorks = finalWorks
            .Where(x => x.FacilityName == plan.FacilityName)
            .ToArray();
        _laneState?.ReleaseIntermediate(
            plan.FacilityName,
            finalFacilityWorks,
            ownerKey);

        long outputAfter = await _data.ItemCountAsync(plan.OutputName, ct);
        long totalGain = outputAfter - requestBaselineQuantity;
        if (totalGain < 0)
            throw new InvalidOperationException(
                "중간재료 수령 후 재고가 요청 기준보다 감소했습니다 · 재료 보류로 축소하지 않습니다.");
        if (totalGain < minimum)
        {
            // A material HOLD cannot explain missing outputs from a batch
            // whose registrations should already cover the minimum request.
            int registered = store.Load()?.QueuedWorks ?? 0;
            if (registered * (long)plan.ProducedPerWork >= minimum)
                throw new InvalidOperationException(
                    $"중간재료 수령 후 산출량 미확정 · 등록 {registered}건 / 재고 증가 {totalGain} · 보류로 축소하지 않습니다.");
            if (materialHold is not null)
                throw new DrainedMaterialSupplyHoldException(
                    plan.FacilityName, materialHold.Evidence);
            throw new InvalidOperationException(
                $"{plan.OutputName} 통합 중간가공 후 최소 수량 검증 실패: " +
                $"+{totalGain:N0} / 최소 +{minimum:N0} · 목표 +{requiredAdditionalQuantity:N0}");
        }

        store.Delete();
        if (totalGain < requiredAdditionalQuantity)
            Log?.Invoke(
                $"[중간재료 스케줄] 부분 보충 · {plan.OutputName} +{totalGain:N0}개 / " +
                $"목표 +{requiredAdditionalQuantity:N0}개 · 최소 +{minimum:N0}개 확인 · " +
                (materialHold is null
                    ? "외부 원재료 부족으로 남은 가공 생략 · 상위 품목 재검증"
                    : $"분류={materialHold.Evidence.Category} · 신규 등록 보류 · " +
                      "수령 확인된 재고만 사용 · 상위 품목 재검증"));
        else
            Log?.Invoke(
                $"[중간재료 스케줄] 완료 · {plan.OutputName} 요청 기준 +{totalGain:N0}개 · " +
                "메인 시설 배치로 복귀");
    }

    private async Task<AlteringWork[]> WaitForFacilityBatchBoundaryAsync(
        AlteringPlan plan,
        CancellationToken ct,
        bool allowCooperativeYield = false)
    {
        // F06: this boundary runs while the parent Coordinator is awaited and
        // cannot execute its own watchdog. Reuse exactly its progress policy.
        var boundaryWatch = new MultiAlteringWaitWatchdog(
            _boundaryIdleThreshold, _boundaryNow);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var works = await _data.WorksAsync(ct);
            var facilityWorks = works
                .Where(x => x.FacilityName == plan.FacilityName)
                .ToArray();
            _laneState?.Observe(
                plan.FacilityName,
                facilityWorks,
                allowShrink: false);

            if (facilityWorks.Length == 0 ||
                facilityWorks.All(x => x.IsCompleted))
                return facilityWorks;

            // Only the first entry boundary may yield, before acquiring the
            // intermediate owner lease or touching the processing screen.
            // Drain-on-hold paths retain their original blocking/watchdog logic.
            if (allowCooperativeYield && _yieldBusyFacilityToParent)
            {
                Log?.Invoke($"[중간재료 스케줄] {plan.FacilityName} 기존 배치 진행 중 · " +
                    "중간재료 작업 미등록 · 상위 F9에 제어권 반환 · 독립 시설 계속");
                throw new IntermediateFacilityBusyException(plan.FacilityName);
            }

            try
            {
                boundaryWatch.Observe(plan.FacilityName, facilityWorks);
            }
            catch (InvalidOperationException ex)
            {
                _laneState?.InvalidateOnsite(
                    $"중간재료 경계 정체 · {plan.FacilityName} · 이동/수령 없이 정지");
                Log?.Invoke($"[중간재료 스케줄] {ex.Message}");
                throw;
            }

            long waitSeconds = facilityWorks
                .Where(x => x.State == "InProgress")
                .Select(x => x.RemainingSeconds)
                .DefaultIfEmpty(5)
                .Min();

            Log?.Invoke(
                $"[중간재료 스케줄] {plan.FacilityName.Replace(" 시설", "")} 기존 배치 대기 · " +
                $"진행/대기 {facilityWorks.Count(x => !x.IsCompleted)}건 · " +
                $"완료 {facilityWorks.Count(x => x.IsCompleted)}건 · " +
                $"배치 전체 완료 전 이동 없음");

            await _delay(
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 30)),
                ct);
        }
    }
}
