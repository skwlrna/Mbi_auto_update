using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _alteringDisplay = "시설·품목·수량 선택";

    private async Task StartAlteringAsync(
        AlteringPlan? requestedPlan = null,
        bool batchChild = false)
    {
        if (requestedPlan is null && !batchChild)
        {
            // F9 is ALWAYS a new supervisor-owned order, even for one recipe.
            // Internal dependency requests (batchChild) keep their own path.
            var queued = _alteringPage.QueuedAlteringPlans;
            var orders = queued.Count > 0
                ? queued
                : new[] { SelectedAlteringPlan() };
            await StartMultiAlteringAsync(orders);
            return;
        }

        _starting = true;
        _cancelStart = false;
        try
        {
            if (requestedPlan is null && !batchChild &&
                MultiAlteringBatchStore.ReadPendingPlans(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MabiAuto", "multi-altering")).Count > 0)
                throw new InvalidOperationException(
                    "미종료 다중가공 배치가 있습니다. 작업 목록을 새로고침해 기존 배치를 이어하세요. " +
                    "목록을 비워 새 단일가공으로 자동 재등록하지 않습니다.");
            var plan = requestedPlan ?? SelectedAlteringPlan();
            plan.Validate();

            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_alterable_items", "get_altering_works",
                "get_items", "get_gatherable_items", "get_activity", "get_inventory"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(
                _cli, requiredCommands, CancellationToken.None);
            var confirmCommands = requiredCommands.Where(x => capabilities[x].RequiresConfirm).ToArray();
            _log.Write("[자동 가공] CLI capabilities 확인 완료 · 필수 명령 " + requiredCommands.Length + "개");
            if (confirmCommands.Length > 0)
                _log.Write("[자동 가공] requiresConfirm 명령 · " +
                    string.Join(", ", confirmCommands) +
                    " · 메인 화면 시작 버튼을 사용자 승인으로 사용합니다.");

            var rawAlteringData = new AlteringCliData(_cli);
            var gatheringData = new GatheringCliData(_cli);

            var identity = await CliIdentityGuard.CaptureAsync(_cli, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _log.Write("[자동 가공] 캐릭터 문맥 저장 · " + identity.Description);
            _alteringPage.CharacterStatus = "확인됨";

            var refreshedRecipes = await rawAlteringData.RecipesAsync(CancellationToken.None);
            var refreshedSelected = refreshedRecipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
            if (refreshedSelected.Length < plan.RecipeOrdinal ||
                refreshedSelected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                throw new InvalidOperationException("시작 직전 제법 조회 결과가 선택 내용과 달라졌습니다. 목록을 새로고침하세요.");
            var refreshedRecipe = refreshedSelected[plan.RecipeOrdinal - 1];

            var sessionStore = new AlteringSessionStore();
            AlteringSessionState? saved = sessionStore.Load();
            AlteringSessionState session;
            bool resuming = false;

            if (saved is not null && saved.MatchesPlan(plan) && saved.MatchesIdentity(identity.Baseline))
            {
                session = saved;
                resuming = true;
                _log.Write(
                    $"[자동 가공] 저장 작업 이어하기 확인 · {saved.DisplayName} {saved.TargetQuantity}개 · " +
                    $"등록 {saved.QueuedWorks}/{saved.RequiredWorks} · 단계={saved.Stage}");
            }
            else
            {
                if (saved is not null)
                {
                    string reason = saved.MatchesPlan(plan)
                        ? "저장 기록의 캐릭터/계정 문맥이 현재와 다릅니다."
                        : $"미완료 저장 작업이 있습니다: {saved.DisplayName} {saved.TargetQuantity}개 · 등록 {saved.QueuedWorks}/{saved.RequiredWorks}.";
                    var answer = MessageBox.Show(
                        this,
                        reason + "\n\n저장 기록을 초기화하고 현재 선택한 새 작업을 시작할까요?",
                        "자동 가공 이어하기",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                    {
                        _log.Write("[자동 가공] 새 작업 시작 취소 · 기존 이어하기 기록 유지");
                        return;
                    }
                    sessionStore.Delete();
                }

                var currentWorks = await rawAlteringData.WorksAsync(CancellationToken.None);
                long baseline = await rawAlteringData.ItemCountAsync(plan.OutputName, CancellationToken.None);
                int initialExisting = currentWorks.Count(x =>
                    x.FacilityName == plan.FacilityName &&
                    (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
                session = AlteringSessionState.Create(plan, identity.Baseline, baseline, initialExisting);
                sessionStore.Save(session);
                _log.Write(
                    $"[자동 가공] 자동 저장 시작 · {sessionStore.Path} · 기준 보유 {baseline:N0}개 · 기존 작업 {initialExisting}건");
            }

            string materialEstimate = AlteringMaterialEstimate.Describe(plan, refreshedRecipe);
            _log.Write("[자동 가공] 시작 전 " + materialEstimate);

            long adjustedBaseline = checked(session.BaselineQuantity + session.InitialExistingMinimum);
            long currentOutput = await rawAlteringData.ItemCountAsync(plan.OutputName, CancellationToken.None);
            if (resuming && currentOutput < session.LastObservedOutputQuantity)
                throw new InvalidOperationException(
                    $"이어하기 마지막 확인 수량보다 현재 {plan.OutputName} 수량이 적습니다: 마지막 확인 {session.LastObservedOutputQuantity:N0} / 현재 {currentOutput:N0}. " +
                    "중간에 완성품이 소비된 것으로 볼 수 있어 목표 수량을 안전하게 계산할 수 없습니다.");

            _productionBaseline = adjustedBaseline;
            _productionLastMode = "가공";
            _productionDisplayName = plan.DisplayName;
            _productionTargetQuantity = plan.TargetQuantity;
            _productionCurrentQuantity = Math.Clamp(currentOutput - adjustedBaseline, 0, plan.TargetQuantity);
            _productionFacilityName = plan.ScreenTitle;
            _productionProgressSummary = resuming
                ? $"이어하기 준비 · {plan.DisplayName} {_productionCurrentQuantity:N0}/{plan.TargetQuantity:N0} · 등록 {session.QueuedWorks}/{plan.RequiredWorks}"
                : $"{plan.DisplayName} 0/{plan.TargetQuantity:N0} · 등록 0/{plan.RequiredWorks} · 시작 준비";
            ResetAlteringStatus(new[] { plan });
            SeedAlteringStatus(plan, _productionCurrentQuantity);
            RefreshProductionDashboard();

            var windows = WindowTools.EnumerateVisibleWindows();
            if (windows.Count != 1)
                throw new InvalidOperationException("마비노기 모바일 창을 하나만 열어 주세요.");
            var settings = LoadJson<AppSettings>(
                Path.Combine(AppContext.BaseDirectory, "dungeon", "config", "appsettings.json"));
            WindowTools.EnsureClientSizeAndTopRight(windows[0].Handle, 800, 1000);
            await Task.Delay(500);
            if (_cancelStart || IsDisposed) return;

            var visualAltering = new AlteringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "altering"), _cli);
            var visualGathering = new InventoryBulkGatheringScreen(
                windows[0].Handle, settings,
                Path.Combine(AppContext.BaseDirectory, "debug", "gathering"), _cli, gatheringData);
            var screen = new ZeroWingAlteringScreen(visualAltering, _cli, identity);
            var gatheringScreen = new ZeroWingGatheringScreen(visualGathering, _cli, identity);
            var resolver = new RecursiveAlteringSupplyResolver(
                rawAlteringData, gatheringData, screen, gatheringScreen);
            var automation = new AlteringAutomation(
                rawAlteringData, screen,
                supplyResolver: resolver,
                sessionStore: sessionStore,
                session: session);

            visualAltering.Log += text => Ui(() => _log.Write(text));
            visualGathering.Log += text => Ui(() => _log.Write(text));
            screen.Log += text => Ui(() => _log.Write(text));
            gatheringScreen.Log += text => Ui(() => _log.Write(text));
            resolver.Log += text =>
            {
                automation.NoteStage(text.Replace("[재료 해결] ", "재료 해결 · "));
                Ui(() =>
                {
                    _log.Write(text);
                    SetStatus(text.Replace("[재료 해결] ", ""), Blue);
                    RefreshProductionDashboard();
                });
            };

            _dungeonCts?.Dispose();
            _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "가공";
            _mode.Enabled = false;
            _alteringDisplay = $"{plan.ScreenTitle} · {plan.DisplayName} {plan.TargetQuantity}개";
            _dungeonStartedAt = resuming ? session.StartedAt.LocalDateTime : DateTime.Now;
            _dungeonStoppedAt = null;
            _dungeonCycles = session.QueuedWorks;
            _dungeonCompleted = 0;
            _runError = null;
            _inputValue.Text = _dungeonInputName = visualAltering.InputMode;
            SetStatus(resuming ? "저장 작업 이어하기 준비" : "가공 시작 준비", Blue);

            automation.Log += text => Ui(() =>
            {
                _log.Write(text);
                _dungeonCycles = automation.QueuedWorks;
                SetStatus(text.Replace("[자동 가공] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            automation.Progress += progress => Ui(() =>
            {
                UpdateAlteringStatus(plan, progress);
                _productionCurrentQuantity = progress.ConfirmedQuantity;
                _productionProgressSummary = progress.Summary(plan.DisplayName);
                _dungeonCycles = progress.QueuedWorks;
                UpdateStats();
                RefreshProductionDashboard();
            });

            _log.Write(
                "[자동 가공] 시작(F9) · " + _alteringDisplay +
                (resuming ? " · 저장 상태 이어하기" : "") +
                " · CLI 조회 전용 / 무료 화면 경로");

            _dungeonTask = Task.Run(async () =>
            {
                using (screen)
                using (gatheringScreen)
                {
                    try
                    {
                        await automation.RunAsync(plan, token);
                        Ui(() =>
                        {
                            _dungeonCompleted = automation.QueuedWorks;
                            _productionCurrentQuantity = plan.TargetQuantity;
                            _productionProgressSummary =
                                $"{plan.DisplayName} {plan.TargetQuantity:N0}/{plan.TargetQuantity:N0} · 등록 {automation.QueuedWorks}/{plan.RequiredWorks} · 완료";
                            _log.Write("[자동 가공] 목표 작업 완료 · 이어하기 기록 삭제");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Ui(() =>
                        {
                            _productionProgressSummary =
                                $"이어하기 대기 · {plan.DisplayName} · 등록 {automation.QueuedWorks}/{plan.RequiredWorks} · F9로 계속";
                            _log.Write("[자동 가공] 정지되었습니다. 진행 상태는 저장됐으며 F9로 이어갈 수 있습니다. 이미 등록된 작업은 게임에서 계속 진행될 수 있습니다.");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message;
                            _logExpanded = true;
                            ApplyLogVisibility();
                            _productionProgressSummary =
                                $"이어하기 대기 · {plan.DisplayName} · 등록 {automation.QueuedWorks}/{plan.RequiredWorks} · 오류 후 재검증 필요";
                            _log.Write("[자동 가공] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("자동 가공 오류", ex.Message, true);
                            RefreshProductionDashboard();
                        });
                    }
                    finally
                    {
                        Ui(() =>
                        {
                            _dungeonStoppedAt = DateTime.Now;
                            if (batchChild && _multiAlteringRunning)
                            {
                                // The batch coordinator owns the outer mode lifetime.
                                // Keep controls locked between jobs so a completed child
                                // cannot briefly expose another start/mode switch.
                                _activeMode = "가공";
                                _mode.Enabled = false;
                                if (_runError is not null)
                                    SetStatus("오류: " + _runError, Color.Salmon);
                                UpdateStats();
                            }
                            else
                            {
                                _activeMode = null;
                                _mode.Enabled = true;
                                UpdateAbyssSelectorVisibility();
                                RefreshModeStatus();
                                if (_runError is not null)
                                    SetStatus("오류: " + _runError, Color.Salmon);
                                UpdateStats();
                            }
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _runError = ex.Message;
            _log.Write("[자동 가공] 시작 실패: " + ex.Message);
            SetStatus("가공 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("자동 가공 시작 실패", ex.Message, true);
        }
        finally
        {
            _starting = false;
        }
    }

}
