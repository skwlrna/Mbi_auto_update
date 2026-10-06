namespace FishingAutomation;

internal interface IAlteringDependencyScheduler
{
    Task RunAsync(
        AlteringPlan requestedPlan,
        long requestBaselineQuantity,
        int requiredAdditionalQuantity,
        IAlteringSupplyResolver resolver,
        CancellationToken ct);
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

    internal event Action<string>? Log;

    internal MultiAlteringDependencyScheduler(
        IAlteringData data,
        IAlteringScreen screen,
        CliIdentityContext identity,
        string sessionDirectory,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int verificationAttempts = 120,
        FacilityLaneState? laneState = null)
    {
        _data = data;
        _screen = screen;
        _identity = identity;
        _sessionDirectory = sessionDirectory;
        _delay = delay ?? Task.Delay;
        _verificationAttempts = Math.Clamp(verificationAttempts, 1, 120);
        _laneState = laneState;
    }

    public async Task RunAsync(
        AlteringPlan requestedPlan,
        long requestBaselineQuantity,
        int requiredAdditionalQuantity,
        IAlteringSupplyResolver resolver,
        CancellationToken ct)
    {
        if (requiredAdditionalQuantity <= 0)
            return;

        requestedPlan.Validate();

        Log?.Invoke(
            $"[중간재료 스케줄] 요청 · {requestedPlan.DisplayName} 추가 {requiredAdditionalQuantity:N0}개 · " +
            $"{requestedPlan.FacilityName.Replace(" 시설", "")} 시설 배치에 통합");

        var boundaryWorks = await WaitForFacilityBatchBoundaryAsync(requestedPlan, ct);
        if (boundaryWorks.Length > 0)
        {
            Log?.Invoke(
                $"[중간재료 스케줄] {requestedPlan.FacilityName.Replace(" 시설", "")} 기존 배치 " +
                $"{boundaryWorks.Length}건 전체 완료 확인 · 기존 안전 수령 후 중간재료 배치 시작");

            var collector = new AlteringAutomation(
                _data,
                _screen,
                _delay,
                _verificationAttempts);
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
            Log?.Invoke(
                $"[중간재료 스케줄] 기존 완료 배치 수령으로 부족량 해소 · " +
                $"{requestedPlan.OutputName} +{alreadyGained:N0}개 · 추가 등록 없음");
            return;
        }

        if (remainingLong > int.MaxValue)
            throw new InvalidOperationException(
                $"{requestedPlan.OutputName} 중간재료 남은 수량이 안전 범위를 넘었습니다: {remainingLong:N0}");

        var plan = requestedPlan with { TargetQuantity = (int)remainingLong };

        var emptyCheck = await _data.WorksAsync(ct);
        if (emptyCheck.Any(x => x.FacilityName == plan.FacilityName))
            throw new InvalidOperationException(
                $"{plan.FacilityName} 중간재료 배치 시작 직전에 다른 작업이 생겨 시설 소유권을 확보하지 못했습니다.");

        string dependencyDir = Path.Combine(_sessionDirectory, "dependencies");
        Directory.CreateDirectory(dependencyDir);
        var store = new AlteringSessionStore(
            AlteringSessionStore.MultiPlanPath(dependencyDir, plan));

        var saved = store.Load();
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
            session);
        automation.Log += text => Log?.Invoke(text);

        var coordinator = new MultiAlteringCoordinator(
            _laneState,
            FacilityLaneOwner.Intermediate);
        coordinator.Log += text => Log?.Invoke(text);

        Log?.Invoke(
            $"[중간재료 스케줄] 메인과 동일한 시설 7칸 Coordinator 시작 · " +
            $"{plan.DisplayName} {plan.RequiredWorks}작업");

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
                return result == AlteringRunResult.Completed;
            },
            _data.WorksAsync,
            _delay,
            ct);

        var finalWorks = await _data.WorksAsync(ct);
        var finalFacilityWorks = finalWorks
            .Where(x => x.FacilityName == plan.FacilityName)
            .ToArray();
        _laneState?.ReleaseIntermediate(
            plan.FacilityName,
            finalFacilityWorks);

        long outputAfter = await _data.ItemCountAsync(plan.OutputName, ct);
        long totalGain = outputAfter - requestBaselineQuantity;
        if (totalGain < requiredAdditionalQuantity)
            throw new InvalidOperationException(
                $"{plan.OutputName} 통합 중간가공 후 수량 검증 실패: " +
                $"+{totalGain:N0} / 필요 +{requiredAdditionalQuantity:N0}");

        store.Delete();
        Log?.Invoke(
            $"[중간재료 스케줄] 완료 · {plan.OutputName} 요청 기준 +{totalGain:N0}개 · " +
            "메인 시설 배치로 복귀");
    }

    private async Task<AlteringWork[]> WaitForFacilityBatchBoundaryAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
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
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 2, 30)),
                ct);
        }
    }
}
