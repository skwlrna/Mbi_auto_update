using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _multiAlteringRunning;
    private CancellationTokenSource? _multiAlteringCts;
    private bool _resumeUiBusy;

    private async Task StartMultiAlteringAsync(
        IReadOnlyList<AlteringPlan> plans, string? resumeDirectory = null,
        string? expectedResumeBatchId = null)
    {
        if (_multiAlteringRunning)
            return;

        bool isResume = resumeDirectory is not null;
        bool completed = false;
        MultiAlteringBatchStore? batchStore = null;
        MultiAlteringBatchStore? verifiedBatchLease = null;
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

            var recipes = await rawAlteringData.RecipesAsync(token);
            var currentWorks = await rawAlteringData.WorksAsync(token);

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
                    latest, identity.Baseline, rawAlteringData, token);
                sessionDir = latest.Directory;
                batchStore = await Task.Run(() => new MultiAlteringBatchStore(sessionDir), token);
                await batchStore.OpenAsync(
                    plans, identity.Baseline, rawAlteringData, token,
                    allowSingleCharacter: false);
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
            using var screen = new ZeroWingAlteringScreen(visualAltering, _cli, identity);
            using var gatheringScreen = new ZeroWingGatheringScreen(visualGathering, _cli, identity);

            _inputValue.Text = _dungeonInputName = visualAltering.InputMode;

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
                beforeReceipt: batchStore.BeginReceiptAsync,
                afterReceipt: ConfirmBatchReceiptAsync);
            var resolver = new RecursiveAlteringSupplyResolver(
                rawAlteringData,
                gatheringData,
                screen,
                gatheringScreen,
                dependencyScheduler: dependencyScheduler,
                laneState: laneState);

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
                    beforeReceipt: batchStore.BeginReceiptAsync,
                    afterReceipt: ConfirmBatchReceiptAsync);

                var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                automations.Add(key, automation);
                if (!itemCompleted) consumptionLedger.RegisterProducer(
                    plan,
                    (quantity, consumerDisplayName) =>
                        automation.RefreshDurableConsumption());

                long currentOutputForStatus =
                    itemCompleted ? 0 : await rawAlteringData.ItemCountAsync(plan.OutputName, token);
                long restoredConfirmed = itemCompleted ? plan.TargetQuantity : Math.Clamp(
                    currentOutputForStatus +
                        session.CreditedInternalConsumptionQuantity -
                        session.BaselineQuantity -
                        session.InitialExistingMinimum,
                    0,
                    plan.TargetQuantity);
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

            // Materials are prepared only when the next recipe becomes active.
            // CLI reports known shortages only: still recheck on registration.
            async Task PreparePlanMaterialsAsync(AlteringPlan plan, CancellationToken ct)
            {
                var freshRecipes = await rawAlteringData.RecipesAsync(ct);
                var matching = freshRecipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (matching.Length < plan.RecipeOrdinal ||
                    matching[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 재료 점검 직전 제법이 변경되었습니다. 목록을 새로고침하세요.");

                var recipe = matching[plan.RecipeOrdinal - 1];
                var liveWorks = await rawAlteringData.WorksAsync(ct);
                bool occupied = liveWorks.Any(x => x.FacilityName == plan.FacilityName);
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

            var coordinator = new MultiAlteringCoordinator(
                laneState,
                FacilityLaneOwner.Main,
                restoreCompleted: batchStore.CompletedPlans,
                fillInitialVacancies: true);
            coordinator.Log += text => Ui(() =>
            {
                _log.Write(text);
                NoteMultiAlteringAction(text);
                SetStatus(text.Replace("[다중가공] ", ""), Blue);
                RefreshProductionDashboard();
            });

            // Keep durable filesystem IO off the hotkey/UI thread. F10 only
            // cancels the run token; no callback waits for checkpoint persistence.
            await Task.Run(() => coordinator.RunAsync(
                plans,
                async (plan, slotBudget, coordinatorToken) =>
                {
                    coordinatorToken.ThrowIfCancellationRequested();
                    var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                    var automation = automations[key];
                    int registrationsBefore = automation.ConfirmedRegistrationsThisRun;
                    var result = await automation.RunBatchAsync(
                        plan,
                        slotBudget,
                        coordinatorToken);
                    int registered = automation.ConfirmedRegistrationsThisRun - registrationsBefore;
                    laneState.NoteRegistration(
                        plan,
                        FacilityLaneOwner.Main,
                        registered);
                    bool planCompleted = result == AlteringRunResult.Completed;
                    if (planCompleted)
                        consumptionLedger.UnregisterProducer(plan);
                    return planCompleted;
                },
                rawAlteringData.WorksAsync,
                Task.Delay,
                token,
                preparePlan: PreparePlanMaterialsAsync), token);

            await Task.Run(() => { batchStore.Complete(); batchStore.Cleanup(); }, token);
            completed = true;
            _productionCurrentQuantity = _productionTargetQuantity;
            _productionProgressSummary =
                $"다중가공 {plans.Count}/{plans.Count}종 완료 · 시설별 품목 순차 배치 운용 완료";
            NoteMultiAlteringAction("모든 다중가공 작업 완료 · 수령 및 확인 완료");
            _log.Write("[다중가공] 전체 작업 정상 완료 · 시설별 7칸 품목 순차 배치 운용");
            SetStatus("다중가공 완료", Green);

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
            SetStatus("다중가공 오류: " + ex.Message, Color.Salmon);
            _ = SendRuntimeAlertAsync("다중가공 오류", ex.Message, true);
        }
        finally
        {
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
