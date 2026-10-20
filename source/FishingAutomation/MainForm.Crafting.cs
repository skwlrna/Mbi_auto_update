using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _craftingDisplay = "분류·품목·수량 선택";

    private async Task StartCraftingAsync()
    {
        _starting = true;
        _cancelStart = false;
        try
        {
            var selectedPlans = SelectedCraftingPlans();
            foreach (var selectedPlan in selectedPlans) selectedPlan.Validate();
            var plan = selectedPlans[0];

            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_craftable_items", "get_items",
                "get_gatherable_items", "get_alterable_items", "get_altering_works",
                "get_activity", "get_inventory"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(
                _cli, requiredCommands, CancellationToken.None);
            if (capabilities.TryGetValue("execute_crafting", out var craftingCapability))
                _log.Write("[제작] execute_crafting 설명/비용 조회만 수행 · " +
                    (craftingCapability.Description ?? "설명 없음") + " · " +
                    (craftingCapability.Note ?? "비용 안내 Note 없음") + " · " +
                    (craftingCapability.Metadata ?? "비용 메타데이터 없음") + " · 실제 실행 차단 유지");
            else
                _log.Write("[제작] execute_crafting capability 없음 · 화면 제작 경로 사용");
            _log.Write("[제작] CLI capabilities 확인 완료 · 조회 전용 명령 " + requiredCommands.Length + "개");

            var craftingData = new CraftingCliData(_cli);
            var gatheringData = new GatheringCliData(_cli);
            var queue = new List<CraftingPlan>();
            foreach (var selectedPlan in selectedPlans)
            {
                var exact = await craftingData.ExactAsync(
                    selectedPlan.DisplayName, CancellationToken.None);
                var validated = selectedPlan with { ProducedPerCraft = exact.ProducedPerCraft };
                validated.Validate();
                queue.Add(validated);
            }
            plan = queue[0];

            var identity = await CliIdentityGuard.CaptureAsync(_cli, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _log.Write("[제작] 캐릭터 문맥 저장 · " + identity.Description);

            var windows = WindowTools.EnumerateVisibleWindows();
            if (windows.Count != 1)
                throw new InvalidOperationException("마비노기 모바일 창을 하나만 열어 주세요.");
            var settings = LoadJson<AppSettings>(
                Path.Combine(AppContext.BaseDirectory, "dungeon", "config", "appsettings.json"));
            WindowTools.EnsureClientSizeAndTopRight(windows[0].Handle, 800, 1000);
            await Task.Delay(500);
            if (_cancelStart || IsDisposed) return;

            var rawAlteringData = new AlteringCliData(_cli);
            var rawAltering = new AlteringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "altering"), _cli);
            var rawGathering = new InventoryBulkGatheringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "gathering"), _cli, gatheringData);
            var rawCrafting = new CraftingScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "crafting"), _cli);

            var alteringScreen = new ZeroWingAlteringScreen(rawAltering, _cli, identity);
            var gatheringScreen = new ZeroWingGatheringScreen(rawGathering, _cli, identity);
            var craftingScreen = new ZeroWingCraftingScreen(rawCrafting, _cli, identity);

            var managerState = new LifeSkillManagerState();
            managerState.Log += text => Ui(() => _log.Write(text));
            var resolver = new RecursiveAlteringSupplyResolver(
                rawAlteringData, gatheringData, alteringScreen, gatheringScreen,
                managerState: managerState);
            var automation = new CraftingAutomation(
                craftingData, gatheringData, craftingScreen, resolver,
                altering: rawAlteringData, managerState: managerState);

            rawAltering.Log += text => Ui(() => _log.Write(text));
            rawGathering.Log += text => Ui(() => _log.Write(text));
            alteringScreen.Log += text => Ui(() => _log.Write(text));
            gatheringScreen.Log += text => Ui(() => _log.Write(text));
            craftingScreen.Log += text => Ui(() => _log.Write(text));
            resolver.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[재료 해결] ", ""), Blue);
                RefreshProductionDashboard();
            });

            _productionBaseline = await craftingData.ItemCountAsync(plan.DisplayName, CancellationToken.None);
            _productionLastMode = "제작";
            _productionDisplayName = plan.DisplayName;
            _productionTargetQuantity = plan.TargetQuantity;
            _productionCurrentQuantity = 0;
            _productionFacilityName = plan.Category == CraftingCategory.Food ? "음식 제작" : "아이템 제작";
            _productionProgressSummary = "제작 퀘스트 준비";
            RefreshProductionDashboard();

            _dungeonCts?.Dispose();
            _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "제작";
            _mode.Enabled = false;
            _craftingDisplay = queue.Count > 1
                ? $"순차 제작 {queue.Count}종 · 첫 품목 {plan.DisplayName} {plan.TargetQuantity}개"
                : $"{(plan.Category == CraftingCategory.Food ? "음식" : "아이템")} · " +
                  $"{plan.DisplayName} {plan.TargetQuantity}개";
            _dungeonStartedAt = DateTime.Now;
            _dungeonStoppedAt = null;
            _dungeonCycles = 0;
            _dungeonCompleted = 0;
            _runError = null;
            _inputValue.Text = _dungeonInputName = craftingScreen.InputMode;
            SetStatus("제작 시작 준비", Blue);

            automation.Log += text => Ui(() =>
            {
                _log.Write(text);
                _productionCurrentQuantity = automation.Gained;
                SetStatus(text.Replace("[제작] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            CraftingPlan activePlan = plan;
            int completedCraftsBefore = 0;
            automation.Progress += progress =>
            {
                string progressName = activePlan.DisplayName;
                int completedTotal = completedCraftsBefore + progress.CompletedCrafts;
                Ui(() =>
                {
                    _productionCurrentQuantity = progress.ConfirmedQuantity;
                    _productionProgressSummary = progress.Summary(progressName);
                    _dungeonCycles = completedTotal;
                    UpdateStats();
                    RefreshProductionDashboard();
                });
            };

            _log.Write("[제작] 시작(F9) · " + _craftingDisplay +
                " · 최대 10회 퀘스트 / 직접 재료 퀘스트 채집 / 중간재 자동 가공 / 정령의 날개 0개");

            _dungeonTask = Task.Run(async () =>
            {
                using (craftingScreen)
                using (alteringScreen)
                using (gatheringScreen)
                {
                    try
                    {
                        for (int index = 0; index < queue.Count; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            var current = queue[index];
                            activePlan = current;
                            long itemBaseline = await craftingData.ItemCountAsync(
                                current.DisplayName, token);
                            int step = index + 1;
                            Ui(() =>
                            {
                                _productionBaseline = itemBaseline;
                                _productionDisplayName = current.DisplayName;
                                _productionTargetQuantity = current.TargetQuantity;
                                _productionCurrentQuantity = 0;
                                _productionFacilityName = current.Category == CraftingCategory.Food
                                    ? "음식 제작" : "아이템 제작";
                                _productionProgressSummary = $"순차 {step}/{queue.Count} · 제작 준비";
                                _log.Write($"[제작][순차] {step}/{queue.Count} 시작 · " +
                                    $"{current.DisplayName} {current.TargetQuantity}개");
                                RefreshProductionDashboard();
                            });

                            // Each plan must reach its verified target before
                            // the next plan is allowed to touch the game.
                            await automation.RunAsync(current, token);
                            completedCraftsBefore += automation.CompletedCrafts;
                            int confirmedCrafts = completedCraftsBefore;
                            long confirmedGain = automation.Gained;
                            Ui(() =>
                            {
                                _dungeonCompleted = confirmedCrafts;
                                _productionCurrentQuantity = confirmedGain;
                                _productionProgressSummary =
                                    $"{current.DisplayName} 목표 완료 · +{confirmedGain:N0}개";
                                _craftingPage.MarkCraftingCompleted(current);
                                _log.Write($"[제작][순차] {step}/{queue.Count} 완료 확인 · " +
                                    $"{current.DisplayName} +{confirmedGain}개");
                                RefreshProductionDashboard();
                            });
                        }
                        Ui(() =>
                        {
                            _craftingPage.ClearCompletedQueue();
                            _log.Write("[제작] 전체 순차 제작 목표 작업 완료");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (OperationCanceledException ex)
                    {
                        Ui(() =>
                        {
                            if (token.IsCancellationRequested)
                                _log.Write("[제작] 정지되었습니다. · 정지 요청 수신");
                            else
                            {
                                _runError = ex.Message;
                                _logExpanded = true;
                                ApplyLogVisibility();
                                _log.Write("[제작] 안전 정지: " + ex.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message;
                            _logExpanded = true;
                            ApplyLogVisibility();
                            _log.Write("[제작] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("제작 오류", ex.Message, true);
                        });
                    }
                    finally
                    {
                        Ui(() =>
                        {
                            _dungeonStoppedAt = DateTime.Now;
                            _activeMode = null;
                            _mode.Enabled = true;
                            UpdateAbyssSelectorVisibility();
                            RefreshModeStatus();
                            if (_runError is not null)
                                SetStatus("오류: " + _runError, Color.Salmon);
                            UpdateStats();
                            RefreshProductionDashboard();
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _log.Write("[제작] 시작 실패: " + ex.Message);
            SetStatus("제작 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("제작 시작 실패", ex.Message, true);
        }
        finally
        {
            _starting = false;
        }
    }
}
