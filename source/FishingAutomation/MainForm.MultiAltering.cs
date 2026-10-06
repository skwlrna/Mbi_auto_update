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

            string sessionDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MabiAuto", "multi-altering");
            Directory.CreateDirectory(sessionDir);

            var dependencyScheduler = new MultiAlteringDependencyScheduler(
                rawAlteringData,
                screen,
                identity.Baseline,
                sessionDir);
            var resolver = new RecursiveAlteringSupplyResolver(
                rawAlteringData,
                gatheringData,
                screen,
                gatheringScreen,
                dependencyScheduler: dependencyScheduler);
            var consumptionLedger = new MultiAlteringConsumptionLedger(
                rawAlteringData.ItemCountsAsync);

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
            var stores = new List<AlteringSessionStore>();
            var progress = new Dictionary<(string Facility, string Display, int Ordinal), long>();

            AlteringSessionState? TryMigrateLegacySession(
                AlteringPlan plan,
                AlteringSessionStore stableStore)
            {
                var matches = new List<(AlteringSessionStore Store, AlteringSessionState State)>();
                foreach (string path in Directory.EnumerateFiles(sessionDir, "*.json"))
                {
                    if (!AlteringSessionStore.IsLegacyMultiPath(path))
                        continue;

                    var legacyStore = new AlteringSessionStore(path);
                    var legacy = legacyStore.Load();
                    if (legacy is not null &&
                        legacy.MatchesPlan(plan) &&
                        legacy.MatchesIdentity(identity.Baseline))
                        matches.Add((legacyStore, legacy));
                }

                if (matches.Count > 1)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName}의 이전 다중가공 이어하기 기록이 여러 개라 자동 이관하지 않습니다.");

                if (matches.Count == 0)
                    return null;

                stableStore.Save(matches[0].State);
                matches[0].Store.Delete();
                _log.Write(
                    $"[다중가공] 이전 순번 세션을 품목 고유키로 이관 · {plan.DisplayName} · " +
                    $"등록 {matches[0].State.QueuedWorks}/{matches[0].State.RequiredWorks}");
                return matches[0].State;
            }

            for (int index = 0; index < plans.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var plan = plans[index];
                var selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (selected.Length < plan.RecipeOrdinal ||
                    selected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 시작 직전 제법 조회 결과가 선택 내용과 달라졌습니다. 목록을 새로고침하세요.");

                var store = new AlteringSessionStore(
                    AlteringSessionStore.MultiPlanPath(sessionDir, plan));
                stores.Add(store);
                var saved = store.Load();
                if (saved is null ||
                    !saved.MatchesPlan(plan) ||
                    !saved.MatchesIdentity(identity.Baseline))
                {
                    saved = TryMigrateLegacySession(plan, store) ?? saved;
                }
                AlteringSessionState session;

                if (saved is not null &&
                    saved.MatchesPlan(plan) &&
                    saved.MatchesIdentity(identity.Baseline))
                {
                    session = saved;
                    _log.Write(
                        $"[다중가공] 배치 이어하기 · {plan.DisplayName} · 등록 {saved.QueuedWorks}/{saved.RequiredWorks} · 단계={saved.Stage}");
                }
                else
                {
                    if (saved is not null)
                    {
                        store.Delete();
                        _log.Write(
                            $"[다중가공] 이전 배치 기록 불일치 · {plan.DisplayName} 슬롯 기록 새로 생성");
                    }

                    long baseline = await rawAlteringData.ItemCountAsync(plan.OutputName, token);
                    int initialExisting = currentWorks.Count(x =>
                        x.FacilityName == plan.FacilityName &&
                        (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
                    session = AlteringSessionState.Create(
                        plan, identity.Baseline, baseline, initialExisting);
                    store.Save(session);
                    _log.Write(
                        $"[다중가공] 배치 저장 시작 · {plan.DisplayName} · 기준 보유 {baseline:N0}개 · 기존 작업 {initialExisting}건");
                }

                var automation = new AlteringAutomation(
                    rawAlteringData,
                    screen,
                    supplyResolver: resolver,
                    sessionStore: store,
                    session: session,
                    internalConsumptionObserver: consumptionLedger);

                var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                automations.Add(key, automation);
                consumptionLedger.RegisterProducer(
                    plan,
                    (quantity, consumerDisplayName) =>
                        automation.CreditInternalConsumption(
                            quantity, consumerDisplayName));

                long currentOutputForStatus =
                    await rawAlteringData.ItemCountAsync(plan.OutputName, token);
                long restoredConfirmed = Math.Clamp(
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
                long? restoredEta = AlteringEtaEstimator.Estimate(
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

            var coordinator = new MultiAlteringCoordinator();
            coordinator.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[다중가공] ", ""), Blue);
                RefreshProductionDashboard();
            });

            await coordinator.RunAsync(
                plans,
                async (plan, slotBudget, coordinatorToken) =>
                {
                    coordinatorToken.ThrowIfCancellationRequested();
                    var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
                    var result = await automations[key].RunBatchAsync(
                        plan,
                        slotBudget,
                        coordinatorToken);
                    bool planCompleted = result == AlteringRunResult.Completed;
                    if (planCompleted)
                        consumptionLedger.UnregisterProducer(plan);
                    return planCompleted;
                },
                rawAlteringData.WorksAsync,
                Task.Delay,
                token);

            completed = true;
            _productionCurrentQuantity = _productionTargetQuantity;
            _productionProgressSummary =
                $"다중가공 {plans.Count}/{plans.Count}종 완료 · 시설별 혼합 배치 운용 완료";
            _log.Write("[다중가공] 전체 작업 정상 완료 · 시설별 7칸 혼합 배치 병렬 운용");
            SetStatus("다중가공 완료", Green);

            foreach (var store in stores)
                store.Delete();
            foreach (string legacyPath in Directory.EnumerateFiles(sessionDir, "*.json")
                         .Where(AlteringSessionStore.IsLegacyMultiPath))
                new AlteringSessionStore(legacyPath).Delete();
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

            if (completed)
                _alteringPage.ClearQueuedAlteringPlans();

            UpdateAbyssSelectorVisibility();
            RefreshModeStatus();
            UpdateStats();
            RefreshProductionDashboard();
        }
    }
}
