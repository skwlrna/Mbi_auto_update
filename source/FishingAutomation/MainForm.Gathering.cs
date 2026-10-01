using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _gatheringDisplay = "품목·수량 선택";

    private async Task StartGatheringAsync()
    {
        _starting = true; _cancelStart = false;
        try
        {
            var plan = SelectedGatheringPlan();
            plan.Validate();
            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_gatherable_items", "get_activity",
                "get_inventory", "get_items", "execute_gathering", "stop_action"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(_cli, requiredCommands, CancellationToken.None);
            var confirmCommands = requiredCommands.Where(x => capabilities[x].RequiresConfirm).ToArray();
            _log.Write("[자동 채집] CLI capabilities 확인 완료 · 필수 명령 " + requiredCommands.Length + "개");
            _log.Write("[자동 채집][CLI 전체 명령] " +
                string.Join(", ", capabilities.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            if (confirmCommands.Length > 0)
                _log.Write("[자동 채집] requiresConfirm 명령 · " + string.Join(", ", confirmCommands) + " · 메인 화면 시작 버튼을 사용자 승인으로 사용합니다.");

            var filtered = await _cli.GetGatherableItemsAsync(plan.DisplayName, CancellationToken.None);
            if (filtered.Success && filtered.Data is System.Text.Json.JsonElement root &&
                root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                root.TryGetProperty("items", out var items) &&
                items.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var fields = new SortedSet<string>(StringComparer.Ordinal);
                int exact = 0;
                foreach (var row in items.EnumerateArray())
                {
                    if (row.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    foreach (var property in row.EnumerateObject()) fields.Add(property.Name);
                    if (row.TryGetProperty("DisplayName", out var displayName) &&
                        displayName.ValueKind == System.Text.Json.JsonValueKind.String &&
                        displayName.GetString() == plan.DisplayName)
                        exact++;
                }
                _log.Write($"[자동 채집][CLI 직접검색] query={plan.DisplayName} · items={items.GetArrayLength()} · itemFields=[{string.Join(",", fields)}] · exact={exact}");
            }
            else
            {
                _log.Write($"[자동 채집][CLI 직접검색] query={plan.DisplayName} · 실패 · state={filtered.State} · error={filtered.Error ?? "none"}");
            }

            var data = new GatheringCliData(_cli);
            if (_cancelStart || IsDisposed) return;
            _productionLastMode = "채집";
            _productionDisplayName = plan.DisplayName;
            _productionTargetQuantity = plan.TargetQuantity;
            _productionCurrentQuantity = 0;
            _productionFacilityName = "자동 채집 경로";
            RefreshProductionDashboard();
            var identity = await CliIdentityGuard.CaptureAsync(_cli, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _log.Write("[자동 채집] 캐릭터 문맥 저장 · " + identity.Description);
            _gatheringPage.CharacterStatus = "확인됨";
            var screen = new GatheringCliScreen(_cli, identity);
            var automation = new GatheringAutomation(data, screen);
            _dungeonCts?.Dispose(); _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "채집"; _mode.Enabled = false;
            _gatheringDisplay = $"{plan.DisplayName} {plan.TargetQuantity}개";
            _dungeonStartedAt = DateTime.Now; _dungeonStoppedAt = null;
            _dungeonCycles = _dungeonCompleted = 0; _runError = null;
            _inputValue.Text = _dungeonInputName = screen.InputMode;
            SetStatus("채집 시작 준비", Blue);
            screen.Log += text=>Ui(()=>_log.Write(text));
            automation.Log += text => Ui(() =>
            {
                _log.Write(text);
                _dungeonCycles = checked((int)Math.Min(automation.Gained,int.MaxValue));
                _productionCurrentQuantity = automation.Gained;
                SetStatus(text.Replace("[자동 채집] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            _log.Write("[자동 채집] 시작(F9) · " + _gatheringDisplay);
            _dungeonTask = Task.Run(async () =>
            {
                using (screen)
                {
                    try
                    {
                        await automation.RunAsync(plan, token);
                        Ui(() =>
                        {
                            _dungeonCompleted = checked((int)Math.Min(automation.Gained,int.MaxValue));
                            _productionCurrentQuantity = automation.Gained;
                            _log.Write("[자동 채집] 목표 작업 완료");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (OperationCanceledException)
                    { Ui(() => _log.Write("[자동 채집] 정지되었습니다. 게임의 채집/이동 정지 여부를 확인하세요.")); }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message; _logExpanded = true; ApplyLogVisibility();
                            _log.Write("[자동 채집] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("자동 채집 오류", ex.Message, true);
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
            _log.Write("[자동 채집] 시작 실패: " + ex.Message); SetStatus("채집 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("자동 채집 시작 실패", ex.Message, true);
        }
        finally { _starting = false; }
    }
}
