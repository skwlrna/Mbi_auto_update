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
            var data = new GatheringCliData(_cli);
            var catalog=await data.CatalogAsync(CancellationToken.None);
            var recipes=AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync());
            if (_cancelStart || IsDisposed) return;
            using var dialog = new GatheringSettingsDialog(catalog,recipes);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Plan is not GatheringPlan plan || _cancelStart) return;
            if (_cancelStart || IsDisposed) return;
            var screen = new GatheringCliScreen(_cli);
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
                _log.Write(text); _dungeonCycles = checked((int)Math.Min(automation.Gained,int.MaxValue));
                SetStatus(text.Replace("[자동채집] ", ""), Blue); UpdateStats();
            });
            _log.Write("[자동채집] 시작(F9) · " + _gatheringDisplay);
            _dungeonTask = Task.Run(async () =>
            {
                using (screen)
                {
                    try
                    {
                        await automation.RunAsync(plan, token);
                        Ui(() => { _dungeonCompleted = checked((int)Math.Min(automation.Gained,int.MaxValue)); _log.Write("[자동채집] 목표 작업 완료"); });
                    }
                    catch (OperationCanceledException)
                    { Ui(() => _log.Write("[자동채집] 정지되었습니다. 게임의 채집/이동 정지 여부를 확인하세요.")); }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message; _logExpanded = true; ApplyLogVisibility();
                            _log.Write("[자동채집] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("자동채집 오류", ex.Message, true);
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
            _log.Write("[자동채집] 시작 실패: " + ex.Message); SetStatus("채집 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("자동채집 시작 실패", ex.Message, true);
        }
        finally { _starting = false; }
    }
}
