using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _multiAlteringRunning;
    private CancellationTokenSource? _multiAlteringCts;

    private async Task StartMultiAlteringAsync(IReadOnlyList<AlteringPlan> plans)
    {
        if (_multiAlteringRunning)
            return;

        bool completed = false;
        MultiAlteringBatchStore? batchStore = null;
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
        _productionProgressSummary = $"다중가공 준비 · {plans.Count}종 · 시설별 혼합 7칸 배치";
        ResetAlteringStatus(plans);
        SetStatus($"다중가공 병렬 배치 준비 · {plans.Count}종", Blue);
        _log.Write(
            $"[다중가공] 시작(F9) · 작업 {plans.Count}종 · 시설별 최대 7칸 혼합 병렬 운용 · " +
            "같은 시설 품목 라운드로빈 · 한 칸 완료마다 이동하지 않음");

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
            var identity = await CliIdentityGuard.CaptureAsync(_cli, token);
            _log.Write("[다중가공] 캐릭터 문맥 저장 · " + identity.Description);
            _alteringPage.CharacterStatus = "확인됨";

            var recipes = await rawAlteringData.RecipesAsync(token);
            var currentWorks = await rawAlteringData.WorksAsync(token);

            string sessionDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MabiAuto", "multi-altering");
            batchStore = await Task.Run(() => new MultiAlteringBatchStore(sessionDir), token);
            await Task.Run(() => batchStore.OpenAsync(plans, identity.Baseline, rawAlteringData, token), token);
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
                "[다중가공] 시작/이어하기 위치 정책 · " +
                "저장된 작업/이전 실행의 현장확정은 복원하지 않음 · " +
                "초기 위치 미확정은 원격 확정이 아님 · " +
                "첫 시설은 Fresh 이동 1회, 이후 현장 CLI 확인 시 같은 시설 Reuse");
            // One shared consumption ledger covers both main and recursive
            // intermediate registrations in this F9 run.
            var consumptionLedger = new MultiAlteringConsumptionLedger(
                rawAlteringData.ItemCountsAsync);
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

            visualAltering.Log += text => Ui(() => _log.Write(text));
            visualGathering.Log += text => Ui(() => _log.Write(text));
            screen.Log += text => Ui(() => _log.Write(text));
            gatheringScreen.Log += text => Ui(() => _log.Write(text));
            dependencyScheduler.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[중간재료 스케줄] ", ""), Blue);
                RefreshProductionDashboard();
            });
            laneState.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[시설 소유권] ", ""), Blue);
                RefreshProductionDashboard();
            });
            consumptionLedger.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[다중가공] ", ""), Blue);
                RefreshProductionDashboard();
            });
            resolver.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[재료 해결] ", ""), Blue);
                RefreshProductionDashboard();
            });

            var automations = new Dictionary<(string Facility, string Display, int Ordinal), AlteringAutomation>();
            var progress = new Dictionary<(string Facility, string Display, int Ordinal), long>();
            var materialPreflight = new List<MultiAlteringSupplyPreflight>();
            bool hasResumableSessionForPreflight = false;
            bool hasSelectedFacilityWorksForPreflight = currentWorks.Any(work =>
                plans.Any(plan => plan.FacilityName == work.FacilityName));

            for (int index = 0; index < plans.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var plan = plans[index];
                var selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (selected.Length < plan.RecipeOrdinal ||
                    selected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 시작 직전 제법 조회 결과가 선택 내용과 달라졌습니다. 목록을 새로고침하세요.");
                var selectedRecipe = selected[plan.RecipeOrdinal - 1];

                var store = batchStore.PlanStore(plan);
                var session = store.Load() ?? throw new InvalidDataException("배치 품목 기록 소실 · 안전 정지");
                bool itemCompleted = session.MultiState == MultiAlteringItemState.Completed;
                hasResumableSessionForPreflight |= batchStore.IsResuming;
                _log.Write($"[다중가공] 배치 이어하기 · {plan.DisplayName} · 등록 {session.QueuedWorks}/{session.RequiredWorks} · 상태={session.MultiState}");

                if (!itemCompleted && selectedRecipe.MissingIngredients.Count > 0)
                {
                    materialPreflight.Add(new MultiAlteringSupplyPreflight(
                        plan,
                        selectedRecipe,
                        plan.RequiredWorks));
                }

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
                        automation.CreditInternalConsumption(
                            quantity, consumerDisplayName));

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
                _log.Write(
                    $"[다중가공] 시작 상태 복원 · {plan.DisplayName} " +
                    $"{restoredConfirmed:N0}/{plan.TargetQuantity:N0} · " +
                    $"등록 {session.QueuedWorks}/{plan.RequiredWorks} · " +
                    $"ETA={(restoredEta?.ToString() ?? "계산 중")}초");

                automation.Log += text => Ui(() =>
                {
                    _log.Write(text);
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
                $"다중가공 시작 상태 복원 · {_productionCurrentQuantity:N0}/{_productionTargetQuantity:N0} · " +
                $"시설별 혼합 7칸 배치";
            UpdateStats();
            RefreshProductionDashboard();

            if (materialPreflight.Count > 0)
            {
                bool canRunMaterialPreflight = MultiAlteringMaterialPreflightPolicy.CanRun(
                    hasResumableSessionForPreflight,
                    hasSelectedFacilityWorksForPreflight);

                if (canRunMaterialPreflight)
                {
                    _productionProgressSummary =
                        $"다중가공 전체 품목 확정 부족분 통합 채집 · {materialPreflight.Count}종 점검";
                    UpdateStats();
                    RefreshProductionDashboard();
                    _log.Write(
                        $"[다중가공] 전체 품목 통합 재료 계획 시작 · " +
                        $"CLI가 현재 부족으로 확정한 {materialPreflight.Count}종만 선행 계산 · " +
                        "현재 충분해서 숨겨진 재료는 실행 중 재검증");
                    await Task.Run(() => resolver.PreGatherKnownShortagesAsync(materialPreflight, token), token);
                    _log.Write(
                        "[다중가공] 전체 품목 통합 재료 계획 완료 · 실제 등록 직전 재료 검증은 기존 로직 유지");
                }
                else
                {
                    string reason = hasResumableSessionForPreflight
                        ? "이어하기 세션 존재"
                        : "선택 시설에 기존 작업 존재";
                    _log.Write(
                        $"[다중가공] 전체 품목 통합 선행채집 생략 · {reason} · " +
                        "기존 재귀 재료 해결로 실제 부족분만 처리");
                }
            }

            var coordinator = new MultiAlteringCoordinator(
                laneState,
                FacilityLaneOwner.Main,
                restoreCompleted: batchStore.CompletedPlans);
            coordinator.Log += text => Ui(() =>
            {
                _log.Write(text);
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
                token), token);

            await Task.Run(() => { batchStore.Complete(); batchStore.Cleanup(); }, token);
            completed = true;
            _productionCurrentQuantity = _productionTargetQuantity;
            _productionProgressSummary =
                $"다중가공 {plans.Count}/{plans.Count}종 완료 · 시설별 혼합 배치 운용 완료";
            _log.Write("[다중가공] 전체 작업 정상 완료 · 시설별 7칸 혼합 배치 병렬 운용");
            SetStatus("다중가공 완료", Green);

        }
        catch (OperationCanceledException)
        {
            _log.Write(
                "[다중가공] 정지 · 신규 등록/이동 중단 · 시설별 배치 이어하기 기록 유지");
            SetStatus("다중가공 정지", Color.DarkOrange);
        }
        catch (Exception ex)
        {
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
            finally { batchStore?.Dispose(); }

            UpdateAbyssSelectorVisibility();
            RefreshModeStatus();
            UpdateStats();
            RefreshProductionDashboard();
        }
    }
}
