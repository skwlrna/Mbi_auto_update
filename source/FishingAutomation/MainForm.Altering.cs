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
            var data = new AlteringCliData(_cli);
            var recipes = await data.RecipesAsync(CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            using var dialog = new AlteringSettingsDialog(recipes);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Plan is not AlteringPlan plan || _cancelStart) return;
            var windows = WindowTools.EnumerateVisibleWindows();
            if (windows.Count != 1) throw new InvalidOperationException("마비노기 모바일 창을 하나만 열어 주세요.");
            var settings = LoadJson<AppSettings>(Path.Combine(AppContext.BaseDirectory, "dungeon", "config", "appsettings.json"));
            WindowTools.EnsureClientSizeAndTopRight(windows[0].Handle, 800, 1000);
            await Task.Delay(500);
            if (_cancelStart || IsDisposed) return;
            var screen = new AlteringScreen(windows[0].Handle, settings, Path.Combine(AppContext.BaseDirectory, "debug", "altering"));
            var automation = new AlteringAutomation(data, screen);
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
                _log.Write(text); _dungeonCycles = automation.QueuedWorks;
                SetStatus(text.Replace("[자동 가공] ", ""), Blue); UpdateStats();
            });
            _log.Write("[자동 가공] 시작(F9) · " + _alteringDisplay);
            _dungeonTask = Task.Run(async () =>
            {
                using (screen)
                {
                    try
                    {
                        await automation.RunAsync(plan, token);
                        Ui(() => { _dungeonCompleted = automation.QueuedWorks; _log.Write("[자동 가공] 목표 작업 완료"); });
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
