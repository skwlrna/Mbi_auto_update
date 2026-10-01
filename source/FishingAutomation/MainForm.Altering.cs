using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _alteringDisplay = "시설·품목·수량 선택";

    private async Task StartAlteringAsync()
    {
        _starting = true; _cancelStart = false;
        try
        {
            var plan = SelectedAlteringPlan();
            plan.Validate();
            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_alterable_items", "get_altering_works",
                "get_items", "execute_altering", "complete_altering_work",
                "get_gatherable_items", "get_activity", "get_inventory", "execute_gathering", "stop_action"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(_cli, requiredCommands, CancellationToken.None);
            var confirmCommands = requiredCommands.Where(x => capabilities[x].RequiresConfirm).ToArray();
            _log.Write("[자동 가공] CLI capabilities 확인 완료 · 필수 명령 " + requiredCommands.Length + "개");
            if (confirmCommands.Length > 0)
                _log.Write("[자동 가공] requiresConfirm 명령 · " + string.Join(", ", confirmCommands) + " · 메인 화면 시작 버튼을 사용자 승인으로 사용합니다.");

            _productionBaseline = null;
            var rawAlteringData = new AlteringCliData(_cli);
            var data = new ProductionAlteringData(rawAlteringData, value => Ui(() => _productionBaseline = value));
            var gatheringData = new GatheringCliData(_cli);
            if (_cancelStart || IsDisposed) return;
            _productionLastMode = "가공";
            _productionDisplayName = plan.DisplayName;
            _productionTargetQuantity = plan.TargetQuantity;
            _productionCurrentQuantity = 0;
            _productionFacilityName = plan.ScreenTitle;
            RefreshProductionDashboard();
            var identity = await CliIdentityGuard.CaptureAsync(_cli, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _log.Write("[자동 가공] 캐릭터 문맥 저장 · " + identity.Description);
            _alteringPage.CharacterStatus = "확인됨";
            var screen = new AlteringCliScreen(_cli, identity);
            var gatheringScreen = new GatheringCliScreen(_cli, identity);
            var resolver = new RecursiveAlteringSupplyResolver(rawAlteringData, gatheringData, screen, gatheringScreen);
            var automation = new AlteringAutomation(data, screen, supplyResolver: resolver);
            screen.Log += text => Ui(() => _log.Write(text));
            gatheringScreen.Log += text => Ui(() => _log.Write(text));
            resolver.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[재료 해결] ", ""), Blue);
                RefreshProductionDashboard();
            });
            _dungeonCts?.Dispose(); _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "가공"; _mode.Enabled = false;
            _alteringDisplay = $"{plan.ScreenTitle} · {plan.DisplayName} {plan.TargetQuantity}개";
            _dungeonStartedAt = DateTime.Now; _dungeonStoppedAt = null;
            _dungeonCycles = _dungeonCompleted = 0; _runError = null;
            _inputValue.Text = _dungeonInputName = screen.InputMode;
            SetStatus("가공 시작 준비", Blue);
            automation.Log += text => Ui(() =>
            {
                _log.Write(text);
                _dungeonCycles = automation.QueuedWorks;
                // Quantity comes from inventory receipts, never queued jobs.
                SetStatus(text.Replace("[자동 가공] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            _log.Write("[자동 가공] 시작(F9) · " + _alteringDisplay);
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
                            _productionCurrentQuantity = data.Gained;
                            _log.Write("[자동 가공] 목표 작업 완료");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (OperationCanceledException)
                    { Ui(() => _log.Write("[자동 가공] 정지되었습니다. 이미 등록된 작업은 게임에서 계속 진행될 수 있습니다.")); }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message; _logExpanded = true; ApplyLogVisibility();
                            _log.Write("[자동 가공] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("자동 가공 오류", ex.Message, true);
                        });
                    }
                    finally
                    {
                        Ui(() =>
                        {
                            _dungeonStoppedAt = DateTime.Now; _activeMode = null; _mode.Enabled = true;
                            UpdateAbyssSelectorVisibility(); RefreshModeStatus();
                            if (_runError is not null) SetStatus("오류: " + _runError, Color.Salmon);
                            UpdateStats();
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _log.Write("[자동 가공] 시작 실패: " + ex.Message); SetStatus("가공 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("자동 가공 시작 실패", ex.Message, true);
        }
        finally { _starting = false; }
    }
}
