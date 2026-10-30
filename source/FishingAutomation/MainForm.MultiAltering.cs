using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _multiAlteringRunning;
    private CancellationTokenSource? _multiAlteringCts;
    private bool _resumeUiBusy;

    private async Task StartMultiAlteringAsync(
        IReadOnlyList<AlteringPlan> plans, string? resumeDirectory = null,
        string? expectedResumeBatchId = null,
        bool confirmedSingleCharacterResume = false)
    {
        if (_multiAlteringRunning)
            return;

        bool isResume = resumeDirectory is not null;
        // UI shortage evidence can be masked by a later global queue_full
        // response. Capture the selected entries before any asynchronous F9
        // work; live CLI inventory is still checked before gathering.
        var queuedMaterialEvidence = isResume
            ? Array.Empty<MultiAlteringSupplyPreflight>()
            : _alteringPage.QueuedAlteringMaterialEvidence;
        // Snapshot once before awaits; F7 cannot change it while this batch runs.
        bool combinedAbyss = _combinedAlteringRequested;
        long minimumAbyssWaitSeconds = combinedAbyss
            ? _schedulerPage.MinimumAbyssWaitSeconds
            : AbyssAlteringSchedulePolicy.MinimumUsefulWaitSeconds;
        // Snapshot all WinForms selections on the F9/UI thread.
        // A coordinator worker must not read ComboBox controls.
        string combinedAbyssDungeonName = SelectedAbyssDungeon;
        string combinedAbyssTargetId = SelectedAbyssTargetId;
        bool completed = false;
        MultiAlteringBatchStore? batchStore = null;
        MultiAlteringBatchStore? verifiedBatchLease = null;
        LifeSkillManagerState? managerState = null;
        LifeSkillWorkFence? alteringWorkFence = null;
        MultiAlteringFacilityBottleneckTracker? facilityBottlenecks = null;
        MultiAlteringFacilityEfficiencyTracker? facilityEfficiency = null;
        MultiAlteringReadQuarantineTracker? facilityReadQuarantine = null;
        bool singleCharacterMode = false;
        _multiAlteringRunning = true;
        _multiAlteringCts?.Dispose();
        _multiAlteringCts = new CancellationTokenSource();
        var token = _multiAlteringCts.Token;

        _activeMode = "가공";
        _mode.Enabled = false;
        _runError = null;
        _dungeonStartedAt = DateTime.Now;
        _dungeonStoppedAt = null;
        _productionLastMode = "가공";
        _productionDisplayName = "다중가공";
        _productionTargetQuantity = checked(plans.Sum(x => x.TargetQuantity));
        _productionCurrentQuantity = 0;
        _productionFacilityName = "시설별 병렬 배치";
        _productionProgressSummary = $"다중가공 준비 · {plans.Count}종 · 시설별 품목 순차 7칸 배치";
        ResetAlteringStatus(plans);
        SetStatus($"다중가공 병렬 배치 준비 · {plans.Count}종", Blue);
        _log.Write(
            $"[다중가공] {(isResume ? "최근 작업 이어하기" : "시작(F9)")} · 작업 {plans.Count}종 · 시설별 최대 7칸 독립 병렬 운용 · " +
            "같은 시설은 품목 완주 후 다음 품목 · 한 칸 완료마다 이동하지 않음");

        try
        {
            foreach (var plan in plans)
                plan.Validate();

            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_alterable_items", "get_altering_works",
                "get_items", "get_gatherable_items", "get_activity", "get_inventory"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(
                _cli, requiredCommands, token);
            var confirmCommands = requiredCommands.Where(x => capabilities[x].RequiresConfirm).ToArray();
            _log.Write("[다중가공] CLI capabilities 확인 완료 · 필수 명령 " + requiredCommands.Length + "개");
            if (confirmCommands.Length > 0)
                _log.Write("[다중가공] requiresConfirm 명령 · " +
                    string.Join(", ", confirmCommands) +
                    " · 메인 화면 시작 버튼을 사용자 승인으로 사용합니다.");

            var rawAlteringData = new AlteringCliData(_cli);
            var gatheringData = new GatheringCliData(_cli);
            var identity = await CliIdentityGuard.CaptureForMultiAlteringAsync(
                _cli, token, allowLimitedFreshTest: true);
            singleCharacterMode = !identity.Baseline.HasDurableMultiIdentity;
            _log.Write("[다중가공] 캐릭터 문맥 저장 · " + identity.Description);
            _alteringPage.CharacterStatus = isResume ? "이전 작업 재개 확인" :
                singleCharacterMode ? "1캐릭터 · 신규 작업" : "신규 작업";
            _log.Write(isResume
                ? "[다중가공] 이전 배치 이어하기 · 목표/완료 기록 유지 · 게임 상태 재검증 후에만 등록"
                : "[다중가공] F9 신규 실행 · 중간관리자가 선택 목록/시설별 대기열/7칸 " +
                  "혼합 배치를 전부 지휘 · 기존 F10 기록 자동 이어하기 없음");
            if (singleCharacterMode)
                _log.Write("[다중가공] 단일 캐릭터 전용 · CLI 서버명만 검증 · " +
                    "다른 캐릭터로 전환하지 않음");

            // Fresh recipe + works must agree before F9 starts. Re-query up to
            // twice only for a transient, unverified global queue_full mask.
            // No UI input or manifest exists at this boundary.
            var (recipes, currentWorks) =
                await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
                    plans, rawAlteringData.RecipesAsync, rawAlteringData.WorksAsync,
                    (delay, ct) => Task.Delay(delay, ct), token,
                    message => _log.Write(message));
            if (plans.Any(plan => recipes.Where(x => x.DisplayName == plan.DisplayName)
                    .ElementAtOrDefault(plan.RecipeOrdinal - 1)?.Reason == "queue_full"))
            {
                string laneEvidence = string.Join("; ",
                    plans.Select(x => x.FacilityName).Distinct(StringComparer.Ordinal).Select(f =>
                        $"{f} {currentWorks.Count(x => x.FacilityName == f)}/7" +
                        $" (완료 {currentWorks.Count(x => x.FacilityName == f && x.IsCompleted)})"));
                _log.Write("[CLI][가공 대기열] queue_full 재검증 · " + laneEvidence);
            }

            string mainSessionDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MabiAuto", "multi-altering");
            // Root file lease excludes legacy and other running versions.
            verifiedBatchLease = await Task.Run(
                () => new MultiAlteringBatchStore(mainSessionDir), token);

            // F9 still creates a unique fresh run. Explicit resume reopens only
            // the selected, latest manifest; neither path can redirect to the other.
            string sessionDir;
            if (isResume)
            {
                if (singleCharacterMode && !confirmedSingleCharacterResume)
                    throw new InvalidOperationException(
                        "1캐릭터 전용 이어하기는 사용자의 동일 캐릭터 확인이 필요합니다 · 자동 시작 차단");
                var latest = await Task.Run(
                    () => MultiAlteringRecentResume.Latest(mainSessionDir), token);
                if (latest is null || latest.BatchId != expectedResumeBatchId ||
                    !string.Equals(Path.GetFullPath(latest.Directory),
                        Path.GetFullPath(resumeDirectory!), StringComparison.OrdinalIgnoreCase) ||
                    !MultiAlteringBatchStore.ReadPendingPlans(latest.Directory)
                        .Select(MultiAlteringBatchStore.Key)
                        .SequenceEqual(plans.Select(MultiAlteringBatchStore.Key)))
                    throw new InvalidOperationException(
                        "이어하기 최근 기록이 변경됐습니다 · 재검증 전 자동 시작 차단");
                // Verification under the root lease, BEFORE any game-window input.
                await MultiAlteringRecentResume.VerifyAsync(
                    latest, identity.Baseline, rawAlteringData, token,
                    allowSingleCharacter: singleCharacterMode && confirmedSingleCharacterResume);
                sessionDir = latest.Directory;
                batchStore = await Task.Run(() => new MultiAlteringBatchStore(sessionDir), token);
                await batchStore.OpenAsync(
                    plans, identity.Baseline, rawAlteringData, token,
                    allowSingleCharacter: singleCharacterMode && confirmedSingleCharacterResume);
                if (!batchStore.IsResuming || batchStore.BatchId != latest.BatchId)
                    throw new InvalidOperationException(
                        "이어하기 기존 배치 대신 새 배치를 생성하려 했습니다 · 안전 정지");
                _log.Write($"[다중가공] 검증된 최근 배치 이어하기 · {batchStore.BatchId} · 기록 보존");
            }
            else
            {
                sessionDir = Path.Combine(mainSessionDir, "fresh-runs",
                    "f9-" + Guid.NewGuid().ToString("N"));
                batchStore = await Task.Run(() => new MultiAlteringBatchStore(sessionDir), token);
                await Task.Run(() => batchStore.OpenFreshAsync(
                    plans, identity.Baseline, rawAlteringData, token), token);
                _log.Write("[다중가공] 이번 F9 새 목표 0부터 시작 · 기존 게임 작업은 그대로 · 빈 슬롯부터 등록");
                _log.Write("[다중가공] 독립 신규 장부 생성 · " + sessionDir +
                    " · 과거 F05 미확정 기록 변경 없음");
            }
            // A new CLI observation rebuilds physical slot state for this run.
            currentWorks = await rawAlteringData.WorksAsync(token);
            _log.Write($"[다중가공] 배치 식별 · {batchStore.BatchId} · 완료 기록 {batchStore.CompletedPlans(plans).Count}/{plans.Count}");
            if (batchStore.IsTerminal)
            {
                await Task.Run(batchStore.Cleanup, token);
                _productionCurrentQuantity = _productionTargetQuantity;
                foreach (var plan in plans) SeedAlteringStatus(plan, plan.TargetQuantity, 0);
                _productionProgressSummary = "다중가공 저장된 전체 완료 확인 · 신규 등록 없음";
                _log.Write("[다중가공] 전체 완료 확정 기록 복원 · 게임 입력 없이 정리만 완료");
                SetStatus("다중가공 완료", Green);
                completed = true;
                return;
            }

            var windows = WindowTools.EnumerateVisibleWindows();
            if (windows.Count != 1)
                throw new InvalidOperationException("마비노기 모바일 창을 하나만 열어 주세요.");

            var settings = LoadJson<AppSettings>(
                Path.Combine(AppContext.BaseDirectory, "dungeon", "config", "appsettings.json"));
            WindowTools.EnsureClientSizeAndTopRight(windows[0].Handle, 800, 1000);
            await Task.Delay(500, token);

            var visualAltering = new AlteringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "altering"), _cli);
            var visualGathering = new InventoryBulkGatheringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "gathering"), _cli, gatheringData);
            using var interruptions = ProductionInterruptionCheckpoint.Install(visualAltering.CheckInterruptionsAsync);
            using var screen = new ZeroWingAlteringScreen(visualAltering, _cli, identity);
            using var gatheringScreen = new ZeroWingGatheringScreen(visualGathering, _cli, identity);

            _inputValue.Text = _dungeonInputName = visualAltering.InputMode;

            managerState = new LifeSkillManagerState();
            managerState.Log += text => Ui(() => _log.Write(text));
            managerState.BeginStage(LifeSkillStage.Altering,
                "F9 가공·채집 중간관리자 시작");
            // Separate per-batch execution fence: the durable F9 receipt and
            // registration ledger still decides actual completion/resume.
            alteringWorkFence = new LifeSkillWorkFence();
            alteringWorkFence.Log += text => Ui(() => _log.Write(text));
            var laneState = new FacilityLaneState(currentWorks);
            // M3: reloading sessions and existing CLI works never restores
            // physical onsite proof. This fresh in-memory manager owns all
            // subsequent location decisions for this F9 multi-altering run.
            _log.Write(
                "[다중가공] 이번 F9 위치 정책 · " +
                "저장된 작업/이전 실행의 현장확정은 복원하지 않음 · " +
                "초기 위치 미확정은 원격 확정이 아님 · " +
                "첫 시설은 Fresh 이동 1회, 이후 현장 CLI 확인 시 같은 시설 Reuse");
            // One shared consumption ledger covers both main and recursive
            // intermediate registrations in this F9 run.
            var consumptionLedger = new MultiAlteringConsumptionLedger(
                rawAlteringData.ItemCountsAsync, batchStore);
            async Task ConfirmBatchReceiptAsync(AlteringPlan receiptPlan, CancellationToken ct)
            {
                await batchStore.ConfirmReceiptAsync(receiptPlan, rawAlteringData, ct);
                // Preserve the existing completed-producer lifetime for peers that
                // finish in one facility-wide receipt, including dependency receipts.
                foreach (var done in batchStore.CompletedPlans(plans))
                    consumptionLedger.UnregisterProducer(done);
            }
            var dependencyScheduler = new MultiAlteringDependencyScheduler(
                rawAlteringData,
                screen,
                identity.Baseline,
                sessionDir,
                laneState: laneState,
                internalConsumptionObserver: consumptionLedger,
                beforeReceipt: (receiptPlan, receiptToken) =>
                    batchStore.BeginReceiptWithEvidenceAsync(receiptPlan, rawAlteringData, receiptToken),
                afterReceipt: ConfirmBatchReceiptAsync);
            var resolver = new RecursiveAlteringSupplyResolver(
                rawAlteringData,
                gatheringData,
                screen,
                gatheringScreen,
                dependencyScheduler: dependencyScheduler,
                laneState: laneState,
                managerState: managerState);

            visualAltering.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
            });
            visualGathering.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
            });
            screen.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
            });
            gatheringScreen.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
            });
            dependencyScheduler.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[중간재료 스케줄] ", ""), Blue);
                RefreshProductionDashboard();
            });
            laneState.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[시설 소유권] ", ""), Blue);
                RefreshProductionDashboard();
            });
            consumptionLedger.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[다중가공] ", ""), Blue);
                RefreshProductionDashboard();
            });
            resolver.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[재료 해결] ", ""), Blue);
                RefreshProductionDashboard();
            });

            // Food follows existing route checks. Leather may recursively craft
            // intermediate hides, but external raw hides/tannin must never be
            // auto-gathered or purchased. Inspect before any movement or input.
            async Task<MaterialSupplyHold?> InspectSupplyRouteAsync(
                AlteringPlan plan, CancellationToken ct,
                AlteringRecipe? selectedShortageEvidence = null)
            {
                bool food = string.Equals(plan.FacilityName, "식재료 가공 시설",
                    StringComparison.Ordinal);
                bool leather = string.Equals(plan.FacilityName, "가죽 가공 시설",
                    StringComparison.Ordinal);
                if (!food && !leather) return null;
                var liveRecipes = await managerState.ReadWithRecoveryAsync(
                    LifeSkillStage.Altering, "food-route-recipes",
                    rawAlteringData.RecipesAsync, ct);
                var selected = liveRecipes.Where(x =>
                    string.Equals(x.DisplayName, plan.DisplayName,
                        StringComparison.Ordinal)).ElementAtOrDefault(plan.RecipeOrdinal - 1);
                if (selected is null || selected.ProducedPerWork != plan.ProducedPerWork ||
                    AlteringFacilityResolver.Resolve(selected) != plan.FacilityName)
                    throw new InvalidOperationException(
                        "재료 경로 확인 중 제법 동일성이 불명확해 기존 입력을 중단합니다.");
                if (selected.Alterable) return null;
                // The current global CLI may mask a concrete previously
                // selected shortage behind queue_full from another facility.
                // Only reuse a same-recipe CLI/UI shortage to detect a HOLD;
                // stock quantities are always queried again.
                var evidence = selected.MissingIngredients.Count > 0 ? selected
                    : selectedShortageEvidence is not null &&
                      selectedShortageEvidence.DisplayName == selected.DisplayName &&
                      selectedShortageEvidence.ProducedPerWork == selected.ProducedPerWork &&
                      AlteringFacilityResolver.Resolve(selectedShortageEvidence) == plan.FacilityName &&
                      selectedShortageEvidence.MissingIngredients.Count > 0
                        ? selectedShortageEvidence : selected;
                if (evidence.MissingIngredients.Count == 0)
                    return null; // no identifiable shortage: existing guards prevail.
                var catalog = await managerState.ReadWithRecoveryAsync(
                    LifeSkillStage.Altering, "food-route-catalog",
                    gatheringData.CatalogAsync, ct);
                // F9 registers one ticket at a time. For leather, require
                // enough raw input for the next ticket, not the entire target;
                // a partial stock may still produce useful confirmed work.
                var inspectionPlan = leather
                    ? plan with { TargetQuantity = plan.ProducedPerWork }
                    : plan;
                return await MaterialSupplyRoutePolicy.FindHoldAsync(
                    inspectionPlan, evidence, liveRecipes, catalog,
                    (name, token) => managerState.ReadWithRecoveryAsync(
                        LifeSkillStage.Altering, "food-route-inventory",
                        inner => rawAlteringData.ItemCountAsync(name, inner), token),
                    ct);
            }

            // Prepare provable raw shortages across ALL selected recipes before
            // servicing the first full lane. Otherwise an unrelated wood/metal
            // failure can prevent food materials (e.g. well water) from ever
            // being collected. This stage performs only guarded free gathering;
            // it neither receives existing jobs nor clicks a processing slot.
            // Resume must preserve its original journal and skip this new pass.
            if (!isResume)
            {
                var freshForPreflight = await rawAlteringData.RecipesAsync(token);
                var initialShortages =
                    MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
                        plans, freshForPreflight, queuedMaterialEvidence);
                // Report ALL selected facilities, not only the first lane that
                // happens to be serviced. This has no game input side effects.
                var preflightWorks = await rawAlteringData.WorksAsync(token);
                foreach (string state in MultiAlteringMaterialPreflightPolicy
                    .DescribeSelectedEvidence(plans, freshForPreflight, preflightWorks))
                    _log.Write("[다중가공][선행재료 진단] " + state);
                _log.Write(
                    $"[다중가공] F9 전체 품목 선행채집 점검 · {plans.Count}종 중 " +
                    $"{initialShortages.Count}종 부족 재료 증거 · 빈/가득 찬 시설과 무관하게 현재 재고 재확인");
                // V3.2.42: forecast only materials for which the CLI or the
                // selected, exact-recipe shortage snapshot has evidence.
                // A missing ingredient line is NOT a complete recipe BOM.
                // The forecast may log a useful warning, but cannot authorize
                // gathering, movement or registration. All actual inputs still
                // go through the existing F9 guards and resolver.
                if (initialShortages.Count == 0)
                    _log.Write("[다중가공][재료 사전 예측] 확인된 부족 재료 없음 · 전체 재료 충분 의미 아님 · 실행 시 재검증");
                else
                {
                    try
                    {
                        var forecastCatalog = await managerState.ReadWithRecoveryAsync(
                            LifeSkillStage.Altering, "preflight-forecast-catalog",
                            gatheringData.CatalogAsync, token);
                        var forecast = await MultiAlteringMaterialForecastPolicy.ForecastAsync(
                            initialShortages, forecastCatalog,
                            (item, ct) => managerState.ReadWithRecoveryAsync(
                                LifeSkillStage.Altering, "preflight-forecast-inventory",
                                inner => rawAlteringData.ItemCountAsync(item, inner), ct),
                            token);
                        _log.Write(
                            $"[다중가공][재료 사전 예측] 부분 증거 {initialShortages.Count}/{plans.Count}품목 · " +
                            $"식별 재료 {forecast.Count}종 · 신규 CLI 재고 각 1회 · " +
                            "공유 재고 중복 차감 방지 · 표시 우선순위는 실행 순서를 변경하지 않음");
                        foreach (var estimate in forecast.Take(20))
                        {
                            _log.Write(
                                $"[다중가공][재료 사전 예측] {estimate.DisplayName} · " +
                                $"필요 추정 {estimate.EvidencedDemand} / 현재 {estimate.FreshStock} / " +
                                $"부족 {estimate.Shortage} · 사용 품목 {estimate.SelectedRecipes}종 · " +
                                $"경로={estimate.AcquisitionRoute}");
                        }
                        if (forecast.Count > 20)
                            _log.Write(
                                $"[다중가공][재료 사전 예측] 나머지 {forecast.Count - 20}종은 로그 요약 생략");
                        if (initialShortages.Count != plans.Count)
                            _log.Write(
                                $"[다중가공][재료 사전 예측] 정보 미확인 {plans.Count - initialShortages.Count}품목 · " +
                                "CLI queue_full/원래 재료 목록 미노출 시 수량 추정 금지");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw; // F10 is never swallowed by diagnostics.
                    }
                    catch (Exception ex)
                    {
                        _log.Write(
                            $"[다중가공][재료 사전 예측] 읽기 전용 진단 실패({ex.GetType().Name}) · " +
                            "추정 정보는 사용하지 않음 · 기존 F9 실행 직전 재검증 유지");
                    }
                }

                if (initialShortages.Count > 0)
                {
                    // Avoid sending a food plan with an unverified input route
                    // into the global gather pass. It will instead enter the
                    // coordinator's isolated food hold when selected.
                    var supported = new List<MultiAlteringSupplyPreflight>();
                    foreach (var shortage in initialShortages)
                    {
                        var hold = await InspectSupplyRouteAsync(
                            shortage.Plan, token, shortage.Recipe);
                        if (hold is null) supported.Add(shortage);
                        else _log.Write(
                            $"[다중가공][식재료 확보 경로 미확인] {shortage.Plan.DisplayName} · " +
                            $"{hold.Item} {hold.Observed}/{hold.Required} · " +
                            $"분류={hold.Category} · 선행채집 제외 · 다른 시설 유지");
                    }
                    if (supported.Count > 0)
                        await resolver.PreGatherKnownShortagesAsync(supported, token);
                    _log.Write("[다중가공] F9 전체 품목 선행채집 종료 · 음식 보류 대상은 시설별 재확인");
                }
            }

            var automations = new Dictionary<(string Facility, string Display, int Ordinal), AlteringAutomation>();
            var progress = new Dictionary<(string Facility, string Display, int Ordinal), long>();
            for (int index = 0; index < plans.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var plan = plans[index];
                var selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (selected.Length < plan.RecipeOrdinal ||
                    selected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 시작 직전 제법 조회 결과가 선택 내용과 달라졌습니다. 목록을 새로고침하세요.");
                var store = batchStore.PlanStore(plan);
                var session = store.Load() ?? throw new InvalidDataException("배치 품목 기록 소실 · 안전 정지");
                bool itemCompleted = session.MultiState == MultiAlteringItemState.Completed;
                _log.Write($"[다중가공] 중간관리자 신규 작업 · {plan.DisplayName} · 등록 {session.QueuedWorks}/{session.RequiredWorks} · 상태={session.MultiState}");

                var automation = new AlteringAutomation(
                    rawAlteringData,
                    screen,
                    supplyResolver: resolver,
                    sessionStore: store,
                    session: session,
                    internalConsumptionObserver: consumptionLedger,
                    onConfirmedReceipt: (receiptPlan, remainingWorks) =>
                        laneState.Observe(
                            receiptPlan.FacilityName,
                            remainingWorks,
                            allowShrink: true),
                    facilityState: laneState,
                    beforeReceipt: (receiptPlan, receiptToken) =>
                    batchStore.BeginReceiptWithEvidenceAsync(receiptPlan, rawAlteringData, receiptToken),
                    afterReceipt: ConfirmBatchReceiptAsync);

                var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                automations.Add(key, automation);
                if (!MultiAlteringItemStatePolicy.IsTerminal(session.MultiState)) consumptionLedger.RegisterProducer(
                    plan,
                    (quantity, consumerDisplayName) =>
                        automation.RefreshDurableConsumption());

                long currentOutputForStatus =
                    itemCompleted ? 0 : await rawAlteringData.ItemCountAsync(plan.OutputName, token);
                long restoredConfirmed = itemCompleted ? plan.TargetQuantity :
                    session.ReceiptTrackingEnabled
                        ? Math.Clamp(
                            checked((long)Math.Max(0,
                                session.ConfirmedReceivedWorks - session.InitialExistingWorks) *
                                plan.ProducedPerWork), 0, plan.TargetQuantity)
                        : Math.Clamp(
                            currentOutputForStatus +
                                session.CreditedInternalConsumptionQuantity -
                                session.BaselineQuantity -
                                session.InitialExistingMinimum,
                            0, plan.TargetQuantity);
                var matchingWorks = currentWorks.Where(x =>
                    x.FacilityName == plan.FacilityName &&
                    (x.DisplayName == plan.DisplayName ||
                     x.DisplayName == plan.OutputName)).ToArray();
                long? batchRemaining = matchingWorks
                    .Where(x => x.State == "InProgress")
                    .Select(x => (long?)x.RemainingSeconds)
                    .DefaultIfEmpty(null)
                    .Max();
                long? restoredEta = itemCompleted ? 0 : AlteringEtaEstimator.Estimate(
                    plan.RequiredWorks,
                    session.QueuedWorks,
                    matchingWorks.Length,
                    batchRemaining,
                    batchRemaining);

                progress[key] = restoredConfirmed;
                SeedAlteringStatus(plan, restoredConfirmed, restoredEta);
                SeedMultiAlteringTelegramItem(plan, session.QueuedWorks, session.Stage);
                _log.Write(
                    $"[다중가공] {(isResume ? "이어하기 복원 상태" : "새 목표 시작 상태")} · {plan.DisplayName} " +
                    $"{restoredConfirmed:N0}/{plan.TargetQuantity:N0} · " +
                    $"등록 {session.QueuedWorks}/{plan.RequiredWorks} · " +
                    $"ETA={(restoredEta?.ToString() ?? "계산 중")}초");

                automation.Log += text => Ui(() =>
                {
                    _log.Write(text);
                    NoteMultiAlteringAction(text);
                    SetStatus(text.Replace("[자동 가공] ", ""), Blue);
                    RefreshProductionDashboard();
                });
                automation.Progress += p => Ui(() =>
                {
                    UpdateAlteringStatus(plan, p);
                    progress[key] = p.ConfirmedQuantity;
                    _productionCurrentQuantity = progress.Values.Sum();
                    _productionProgressSummary =
                        $"다중가공 {_productionCurrentQuantity:N0}/{_productionTargetQuantity:N0} · " +
                        $"{plan.DisplayName} {p.ConfirmedQuantity:N0}/{p.TargetQuantity:N0} · " +
                        $"대기열 {p.FacilityWorks}/7 · 전체 ETA " +
                        $"{(p.TotalRemainingSeconds?.ToString() ?? "계산 중")}초";
                    UpdateStats();
                    RefreshProductionDashboard();
                });
            }

            _productionCurrentQuantity = progress.Values.Sum();
            _productionProgressSummary =
                $"중간관리자 {(isResume ? "이전 배치 재개" : "신규 배치 시작")} · {_productionCurrentQuantity:N0}/{_productionTargetQuantity:N0} · " +
                $"시설별 품목 순차 7칸 배치";
            UpdateStats();
            RefreshProductionDashboard();

            // The only failures converted to per-lane quarantine are transient
            // read-only queries BEFORE gathering, travel, screen work or tickets.
            // Do not wrap the later resolver.PreGatherKnownShortagesAsync call.
            async Task<T> ReadBeforeInputAsync<T>(
                string operation, Func<Task<T>> read, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                try { return await read(); }
                catch (Exception ex) when (
                    !ct.IsCancellationRequested &&
                    LifeSkillReadRecoveryPolicy.IsTransient(ex))
                {
                    throw new SafePreInputReadException(operation);
                }
            }

            // Materials are prepared only when the next recipe becomes active.
            // CLI reports known shortages only: still recheck on registration.
            async Task PreparePlanMaterialsAsync(AlteringPlan plan, CancellationToken ct)
            {
                var freshRecipes = await ReadBeforeInputAsync(
                    "preflight-recipes",
                    () => managerState.ReadWithRecoveryAsync(
                        LifeSkillStage.Altering, "preflight-recipes",
                        rawAlteringData.RecipesAsync, ct), ct);
                var matching = freshRecipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (matching.Length < plan.RecipeOrdinal ||
                    matching[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 재료 점검 직전 제법이 변경되었습니다. 목록을 새로고침하세요.");

                var recipe = matching[plan.RecipeOrdinal - 1];
                var liveWorks = await ReadBeforeInputAsync(
                    "preflight-works",
                    () => managerState.ReadWithRecoveryAsync(
                        LifeSkillStage.Altering, "preflight-works",
                        rawAlteringData.WorksAsync, ct), ct);
                bool occupied = liveWorks.Any(x => x.FacilityName == plan.FacilityName);
                // Leather raw hides and tannin are terminal for this selected
                // item (not a 30-second HOLD). Let the established batch engine
                // persist the terminal state and safely receive existing jobs.
                // Unknown routes still use the ordinary isolated HOLD.
                var unavailable = await ReadBeforeInputAsync(
                    "preflight-supply-route", () => InspectSupplyRouteAsync(plan, ct), ct);
                if (unavailable is not null &&
                    plan.FacilityName == "가죽 가공 시설" &&
                    LeatherRawMaterialPolicy.IsConfirmedShortage(unavailable))
                {
                    Ui(() => _log.Write(
                        $"[다중가공] {plan.DisplayName} 외부 원재료 부족 · " +
                        $"{unavailable.Item} {unavailable.Observed}/{unavailable.Required} · " +
                        "선행 채집/구매 없이 해당 품목만 종료 · 기존 등록 작업 유지"));
                    return; // Batch engine owns durable terminal/receipt accounting.
                }
                if (unavailable is not null)
                    throw new MaterialSupplyHoldException(unavailable);
                if (LeatherRawMaterialPolicy.EndsOnShortage(plan, recipe))
                {
                    Ui(() => _log.Write(
                        $"[다중가공] {plan.DisplayName} 생가죽 계열/타닌 가루 부족 · 자동 확보 없이 해당 품목 등록 중단"));
                    return;
                }
                if (recipe.MissingIngredients.Count == 0)
                {
                    Ui(() => _log.Write(
                        $"[다중가공] {plan.DisplayName} 시작 재료 점검 · CLI 확정 부족분 없음 · 등록 중 재검증 유지"));
                    return;
                }

                if (!MultiAlteringMaterialPreflightPolicy.CanRun(
                    batchStore?.IsResuming ?? true, occupied))
                {
                    Ui(() => _log.Write(
                        $"[다중가공] {plan.DisplayName} 선행채집 생략 · " +
                        (occupied ? "해당 시설 기존/진행 작업 존재" : "이어하기 상태") +
                        " · 기존 재귀 재료 해결 유지"));
                    return;
                }

                Ui(() =>
                {
                    _productionProgressSummary =
                        $"다중가공 {plan.DisplayName} 재료 준비 · 현재 확정 부족분 채집";
                    NoteMultiAlteringAction($"[다중가공] {plan.DisplayName} 시작 전 재료 준비");
                    _log.Write($"[다중가공] {plan.DisplayName} 품목별 선행 채집 시작 · " +
                               "다른 선택 품목의 재료는 지금 채집하지 않음");
                    UpdateStats();
                    RefreshProductionDashboard();
                });
                await resolver.PreGatherKnownShortagesAsync(
                    new[] { new MultiAlteringSupplyPreflight(plan, recipe, plan.RequiredWorks) },
                    ct);
                Ui(() => _log.Write(
                    $"[다중가공] {plan.DisplayName} 품목별 선행 채집 완료 · 등록 중 부족 재검증 유지"));
            }

            // This callback is *strictly read-only*. The scheduler also
            // checks the fresh queue before and after it and then reruns all
            // existing prepare/registration/receipt guards.
            async Task VerifyReadQuarantineAsync(AlteringPlan plan, CancellationToken ct)
            {
                var liveRecipes = await ReadBeforeInputAsync(
                    "preflight-recipes",
                    () => managerState.ReadWithRecoveryAsync(
                        LifeSkillStage.Altering, "safe-read-recheck-recipes",
                        rawAlteringData.RecipesAsync, ct), ct);
                var matches = liveRecipes.Where(x =>
                    string.Equals(x.DisplayName, plan.DisplayName,
                        StringComparison.Ordinal)).ToArray();
                if (matches.Length < plan.RecipeOrdinal ||
                    matches[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork ||
                    AlteringFacilityResolver.Resolve(matches[plan.RecipeOrdinal - 1]) !=
                        plan.FacilityName)
                    throw new InvalidOperationException(
                        "읽기 격리 해제 시 제법/시설 식별이 변경됨 · 안전 정지");
                var liveWorks = await ReadBeforeInputAsync(
                    "preflight-works",
                    () => managerState.ReadWithRecoveryAsync(
                        LifeSkillStage.Altering, "safe-read-recheck-works",
                        rawAlteringData.WorksAsync, ct), ct);
                if (liveWorks.Any(x => x.FacilityName == plan.FacilityName))
                    throw new InvalidOperationException(
                        "읽기 격리 해제 시 기존/외부 등록 대기열 발견 · 안전 정지");
            }

            void RecordReadQuarantineDiagnostic(MultiAlteringReadQuarantineSnapshot evidence)
            {
                try
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MabiAuto", "diagnostics", "altering");
                    string saved = MultiAlteringReadQuarantineAudit.Write(directory, evidence);
                    _log.Write("[다중가공][반복 오류 진단] 로컬 AutoFix 검토 자료: " +
                        Path.GetFileName(saved) + " · 게임 입력/코드 수정 권한 없음");
                }
                catch (Exception ex)
                {
                    _log.Write("[다중가공][반복 오류 진단] 로컬 저장 실패: " +
                        ex.GetType().Name + " · 격리 및 입력 안전 가드 유지");
                }
            }

            async Task<MaterialSupplyHold?> RecheckSupplyHoldAsync(
                AlteringPlan plan, MaterialSupplyHold prior, CancellationToken ct)
            {
                if (plan.FacilityName == "가죽 가공 시설")
                {
                    // A mismatch must re-read BOTH sources even if the bag is
                    // still below one ticket. Once shortage is corroborated,
                    // let the normal engine persist its per-item terminal.
                    var leatherHold = await InspectSupplyRouteAsync(plan, ct);
                    return leatherHold is not null &&
                        LeatherRawMaterialPolicy.IsConfirmedShortage(leatherHold)
                            ? null : leatherHold;
                }
                long now = await managerState.ReadWithRecoveryAsync(
                    LifeSkillStage.Altering, "food-hold-recheck",
                    inner => rawAlteringData.ItemCountAsync(prior.Item, inner), ct);
                if (now < 0)
                    throw new InvalidDataException("재료 재고 음수 · 보류 유지 및 안전 정지");
                if (now < prior.Required)
                    return prior with { Observed = now };
                // Previously missing stock is now present, but no stale UI
                // observation can directly authorize a game action.
                return await InspectSupplyRouteAsync(plan, ct);
            }

            bool queueFullAtStart = plans.Any(plan =>
                recipes.Where(x => x.DisplayName == plan.DisplayName)
                    .ElementAtOrDefault(plan.RecipeOrdinal - 1)?.Reason == "queue_full");
            var coordinator = new MultiAlteringCoordinator(
                laneState,
                FacilityLaneOwner.Main,
                restoreCompleted: batchStore.FinishedPlans,
                fillInitialVacancies: true,
                deferOtherLanesWhileQueueFull: queueFullAtStart,
                optimizeFacilityOrder: true);
            facilityBottlenecks = coordinator.Bottlenecks;
            facilityEfficiency = coordinator.Efficiency;
            facilityReadQuarantine = coordinator.ReadQuarantine;
            coordinator.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[다중가공] ", ""), Blue);
                RefreshProductionDashboard();
            });

            // The existing coordinator remains the sole owner of every receipt,
            // registration, material acquisition and on-disk journal.
            // In combined mode it lends control to Abyss ONLY at a pure idle
            // boundary, and blocks until Abyss has exited to the outside HUD.
            Func<CancellationToken, Task>? idleAbyssRound = combinedAbyss
                ? async coordinatorToken =>
                {
                    coordinatorToken.ThrowIfCancellationRequested();
                    // Leaving a facility invalidates physical onsite proof.
                    // The next normal processing operation must re-prove travel.
                    // Reuse the existing zero-Spirit-Wings guarded field-exit
                    // used by gathering. It closes only positively identified
                    // recipe/facility/hub UI and never blindly clicks unknown UI.
                    await screen.ExitToFieldAsync(coordinatorToken);
                    laneState.InvalidateOnsite("병행 어비스 이동 · 이전 가공시설 현장권한 무효화");
                    await RunCombinedAbyssRoundAsync(coordinatorToken,
                        combinedAbyssDungeonName, combinedAbyssTargetId);
                }
                : null;

            // Keep durable filesystem IO off the hotkey/UI thread. F10 only
            // cancels the run token; no callback waits for checkpoint persistence.
            await Task.Run(() => coordinator.RunAsync(
                plans,
                async (plan, slotBudget, coordinatorToken) =>
                {
                    coordinatorToken.ThrowIfCancellationRequested();
                    var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                    var automation = automations[key];
                    var workTicket = alteringWorkFence.Begin(
                        $"alter:{plan.FacilityName}/{plan.DisplayName}/{plan.RecipeOrdinal}");
                    int registrationsBefore = automation.ConfirmedRegistrationsThisRun;
                    try
                    {
                        var result = await automation.RunBatchAsync(
                            plan,
                            slotBudget,
                            coordinatorToken);
                        int registered = automation.ConfirmedRegistrationsThisRun - registrationsBefore;
                        if (registered < 0)
                            throw new InvalidOperationException(
                                "다중가공 확정 등록량이 역행했습니다. 중복 등록 없이 정지합니다.");
                        laneState.NoteRegistration(
                            plan,
                            FacilityLaneOwner.Main,
                            registered);
                        bool finished = result is AlteringRunResult.Completed or
                            AlteringRunResult.RawMaterialExhausted;
                        if (finished)
                            consumptionLedger.UnregisterProducer(plan);
                        // Invocation ended without an unhandled action failure.
                        // Receipt/output confirmation remains in existing F05/
                        // durable batch store; this does not invent queue proof.
                        alteringWorkFence.ConfirmBatchReturn(
                            workTicket, registered, finished);
                        return finished;
                    }
                    catch (DrainedMaterialSupplyHoldException hold)
                        when (hold.FacilityName == plan.FacilityName &&
                              automation.ConfirmedRegistrationsThisRun == registrationsBefore)
                    {
                        // Child receipts/lease release are confirmed by the
                        // dependency scheduler; the coordinator independently
                        // rechecks the empty lane before persisting its HOLD.
                        // No parent registration or terminal result is claimed.
                        alteringWorkFence.ConfirmBatchReturn(workTicket, 0, false);
                        throw;
                    }
                    catch (AlteringFacilityNavigationException)
                    {
                        // Navigation sent no batch registration. Earlier confirmed
                        // registrations remain counted; this is not a completed plan.
                        int registered = automation.ConfirmedRegistrationsThisRun - registrationsBefore;
                        if (registered < 0)
                            throw;
                        alteringWorkFence.ConfirmBatchReturn(workTicket, registered, false);
                        laneState.NoteRegistration(plan, FacilityLaneOwner.Main, registered);
                        Ui(() => _log.Write("[다중가공] 시설 진입 실패 · 가공 등록 미확정과 분리 · 자동 재실행 없이 정지"));
                        throw;
                    }
                    catch (Exception)
                    {
                        alteringWorkFence.MarkUncertain(workTicket, "alter_batch_unconfirmed");
                        facilityBottlenecks?.NoteUncertainBatch(plan.FacilityName);
                        managerState.RecordUncertainAction(LifeSkillStage.Altering, "alter_batch");
                        throw; // No blind replay of a possibly applied registration.
                    }
                },
                ct => managerState.ReadWithRecoveryAsync(
                    LifeSkillStage.Altering, "scheduler-works",
                    rawAlteringData.WorksAsync, ct),
                Task.Delay,
                token,
                preparePlan: PreparePlanMaterialsAsync,
                onIdleAbyssRound: idleAbyssRound,
                minimumAbyssWaitSeconds: minimumAbyssWaitSeconds,
                readRecipes: ct => managerState.ReadWithRecoveryAsync(
                    LifeSkillStage.Altering, "scheduler-recipes",
                    rawAlteringData.RecipesAsync, ct),
                recheckUnavailableRoute: RecheckSupplyHoldAsync,
                // Only the existing F9 manifest persists the hold. No side
                // car files, automatic character selection or unattended
                // F10/crash input replay. The explicit recent-run verifier
                // must have accepted this batch before the coordinator starts.
                restoreMaterialHolds: batchStore.RestoreMaterialHolds,
                persistMaterialHold: batchStore.RecordMaterialHold,
                clearMaterialHold: batchStore.ClearMaterialHold,
                verifySafePreInputRead: VerifyReadQuarantineAsync,
                reportReadQuarantine: RecordReadQuarantineDiagnostic), token);

            await Task.Run(() => { batchStore.Complete(); batchStore.Cleanup(); }, token);
            completed = true;
            int rawExhaustedCount = plans.Count(p =>
                batchStore.Session(p).MultiState == MultiAlteringItemState.RawMaterialExhausted);
            _productionCurrentQuantity = progress.Values.Sum();
            if (rawExhaustedCount > 0)
            {
                _productionProgressSummary =
                    $"다중가공 종료 · 완료 {plans.Count - rawExhaustedCount}종 · 가죽 원재료 부족 {rawExhaustedCount}종 · " +
                    $"실제 수령 {_productionCurrentQuantity:N0}/{_productionTargetQuantity:N0}";
                NoteMultiAlteringAction("가죽 원재료 부족 품목은 목표 미달 종료 · 다른 품목 처리 완료");
                _log.Write("[다중가공] 재료 소진 품목은 목표 달성으로 계산하지 않음 · 나머지 작업 완료");
                SetStatus("다중가공 종료 · 가죽 원재료 부족", Color.DarkOrange);
            }
            else
            {
                _productionCurrentQuantity = _productionTargetQuantity;
                _productionProgressSummary =
                    $"다중가공 {plans.Count}/{plans.Count}종 완료 · 시설별 품목 순차 배치 운용 완료";
                NoteMultiAlteringAction("모든 다중가공 작업 완료 · 수령 및 확인 완료");
                _log.Write("[다중가공] 전체 작업 정상 완료 · 시설별 7칸 품목 순차 배치 운용");
                SetStatus("다중가공 완료", Green);
            }

        }
        catch (OperationCanceledException)
        {
            NoteMultiAlteringAction("F10 중지 요청 · 다중가공 실행 중단");
            _log.Write("[다중가공] F10 정지 · 등록/이동 중단 · " +
                "최근 배치 기록 보존 · F9 새 작업/별도 이어하기 버튼 분리");
            SetStatus("다중가공 정지", Color.DarkOrange);
        }
        catch (Exception ex)
        {
            NoteMultiAlteringAction("다중가공 오류 · " + ex.Message);
            _runError = ex.Message;
            _logExpanded = true;
            ApplyLogVisibility();
            _log.Write("[다중가공] 오류: " + ex.Message);
            var recoveryDecision = LifeSkillRecoveryDecisionPolicy.ForIncident(ex);
            managerState?.RecordIncident(LifeSkillStage.Altering, ex);
            _log.Write("[다중가공][복구 판단] 분류=" + recoveryDecision.Category +
                " · 판단=" + recoveryDecision.Disposition +
                " · 기존 장부/이미 실행한 입력을 자동 재실행하지 않음");
            // A bounded local read-only CLI incident snapshot helps explain
            // missed lanes and masked shortages, without retrying game inputs.
            // The report intentionally excludes character/account IDs and
            // any raw packet or full runtime log payload.
            try
            {
                using var snapshotTimeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                var diagnostics = new AlteringCliData(_cli);
                string saved = await MultiAlteringIncidentSnapshot.CaptureAsync(
                    plans, ex.GetType().Name,
                    diagnostics.RecipesAsync, diagnostics.WorksAsync,
                    Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                        "MabiAuto", "diagnostics", "altering"),
                    snapshotTimeout.Token,
                    recoveryPatterns: managerState?.RecoveryPatternsSnapshot(),
                    recoveryDecision: recoveryDecision,
                    recoveryMetrics: managerState?.RecoveryOutcomeSnapshot(),
                    batchWorkOutcomes: alteringWorkFence?.OutcomeSnapshot(),
                    sharedWorkOutcomes: managerState?.WorkFence.OutcomeSnapshot(),
                    facilityBottlenecks: facilityBottlenecks?.Snapshot());
                _log.Write("[다중가공][오류 진단] 로컬 상태 저장 · " +
                    Path.GetFileName(saved) + " · 자동 재등록/수령 없음");
            }
            catch (Exception diagnosticError)
            {
                _log.Write("[다중가공][오류 진단] 상태 저장 실패 · " +
                    diagnosticError.GetType().Name + " · 원래 오류/장부 보존");
            }
            SetStatus("다중가공 오류: " + ex.Message, Color.Salmon);
            _ = SendRuntimeAlertAsync("다중가공 오류", ex.Message, true);
        }
        finally
        {
            foreach (var entry in facilityReadQuarantine?.Snapshot(plans) ??
                Array.Empty<MultiAlteringReadQuarantineSnapshot>())
                _log.Write(
                    $"[중간관리자][읽기 격리 요약] {entry.Facility} · {entry.Recipe} · " +
                    $"유형={entry.Operation} · 연속 실패 {entry.ConsecutiveFailures}회 · " +
                    $"반복={entry.Repeated} · 신규 CLI 회복 확인={entry.VerifiedRecovered} · " +
                    "불확실한 게임 입력 자동 재실행 없음");
            // These are in-memory diagnostic pass counters. A manager callback
            // returning without an error is not proof of a completed receipt.
            foreach (var lane in facilityEfficiency?.Snapshot() ??
                Array.Empty<MultiAlteringFacilityEfficiencySnapshot>())
            {
                string verifiedRate = lane.BatchCalls == 0 ? "산출 불가" :
                    ((double)lane.VerifiedProgressTurns / lane.BatchCalls * 100)
                        .ToString("0.0") + "%";
                _log.Write(
                    $"[중간관리자][시설 효율] {lane.Facility} · 관찰 {lane.ObservedPasses}주기 · " +
                    $"즉시 처리 후보 {lane.ReadyPasses}회 · 처리 중 {lane.RunningPasses}회 · " +
                    $"재료 보류 {lane.HeldPasses}회 · 준비 상태로 미선택 {lane.UnservedReadyPasses}회 · " +
                    $"실행 콜백 {lane.BatchCalls}회 · 확인된 진척 콜백 {lane.VerifiedProgressTurns}회 · " +
                    $"완료 품목 {lane.CompletedPlanTurns}회 · 검증 진척 비율 {verifiedRate} " +
                    "(영속 수령 성공률 아님 · 읽기 전용 분석)");
            }
            // Read-only evidence only. Repeated blocked episodes are hints for
            // review; neither an inferred omission nor permission to click.
            if (facilityBottlenecks is not null)
            {
                foreach (var lane in facilityBottlenecks.Snapshot()
                    .Where(x => x.QueueFullEpisodes + x.RecipeUnavailableEpisodes +
                        x.StalledWorkEvents + x.UncertainBatchAttempts > 0))
                {
                    _log.Write($"[중간관리자][시설 병목] {lane.Facility} · " +
                        $"queue_full 의심 {lane.QueueFullEpisodes}회 · " +
                        $"제법 조회 불가 {lane.RecipeUnavailableEpisodes}회 · " +
                        $"실제 CLI 정체 경고 {lane.StalledWorkEvents}회 · " +
                        $"배치 결과 미확정 {lane.UncertainBatchAttempts}회 · " +
                        $"현재 보류 {lane.CurrentlyBlocked} · " +
                        $"보류 경과 {(lane.BlockedSeconds?.ToString() ?? "미측정")}초 · " +
                        $"반복 진단 {(lane.RepeatedIssue ? "필요" : "관찰")} · " +
                        "실제 누락/수령 미확정 · 자동 재실행 없음");
                }
            }
            // Ticket outcomes summarize guarded attempts, not the durable
            // F05 receipt ledger or completed target quantities.
            foreach (var metrics in (alteringWorkFence?.OutcomeSnapshot() ??
                     Array.Empty<LifeSkillWorkOutcomeMetrics>())
                     .Concat(managerState?.WorkFence.OutcomeSnapshot() ??
                         Array.Empty<LifeSkillWorkOutcomeMetrics>()))
            {
                if (metrics.Started == 0) continue;
                string verifiedRate = metrics.ConcludedOutputVerificationRate.HasValue
                    ? (metrics.ConcludedOutputVerificationRate.Value * 100).ToString("0.0") + "%"
                    : "산출 불가";
                _log.Write($"[중간관리자][작업 결과] {metrics.Kind} · 시도 {metrics.Started} · " +
                    $"새 CLI+화면 출력 확인 {metrics.OutputVerified} · " +
                    $"F9 콜백 반환 {metrics.BatchCallbackVerified} · " +
                    $"누락 의심/미확정 {metrics.Uncertain} · 진행 미확정 {metrics.InFlight} · " +
                    $"출력 검증률 {verifiedRate} (종료된 채집/제작 시도만) · " +
                    "F9 수령 완료율은 영속 장부 기준 · 자동 재실행 없음");
            }
            _multiAlteringRunning = false;
            _multiAlteringCts?.Dispose();
            _multiAlteringCts = null;
            _activeMode = null;
            _mode.Enabled = true;
            _dungeonStoppedAt = DateTime.Now;

            try
            {
                if (completed)
                {
                    _alteringPage.ClearQueuedAlteringPlans();
                    if (batchStore is not null) await Task.Run(batchStore.AcknowledgeClearedPlan);
                }
            }
            catch (Exception ex)
            {
                _runError = ex.Message;
                _log.Write("[다중가공] 완료 정리 저장 실패 · 완료 기록 보존: " + ex.Message);
                SetStatus("다중가공 완료 정리 확인 필요", Color.DarkOrange);
            }
            finally { batchStore?.Dispose(); verifiedBatchLease?.Dispose(); }

            UpdateAbyssSelectorVisibility();
            RefreshModeStatus();
            UpdateStats();
            RefreshProductionDashboard();
        }
    }
}
