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
        SetStatus($"다중가공 준비 · {plans.Count}종", Blue);
        _log.Write($"[다중가공] 시작(F9) · 작업 {plans.Count}종 · 기존 단일가공 엔진 순차 재사용");

        var coordinator = new MultiAlteringCoordinator();
        coordinator.Log += text => Ui(() =>
        {
            _log.Write(text);
            SetStatus(text.Replace("[다중가공] ", ""), Blue);
            RefreshProductionDashboard();
        });

        try
        {
            await coordinator.RunAsync(
                plans,
                async (plan, coordinatorToken) =>
                {
                    coordinatorToken.ThrowIfCancellationRequested();

                    Task? previousTask = _dungeonTask;
                    await StartAlteringAsync(plan, batchChild: true);

                    if (_cancelStart || coordinatorToken.IsCancellationRequested)
                        throw new OperationCanceledException(coordinatorToken);

                    Task? childTask = _dungeonTask;
                    if (childTask is null || ReferenceEquals(childTask, previousTask))
                        throw new InvalidOperationException(
                            $"{plan.DisplayName} 다중가공 하위 작업을 시작하지 못했습니다.");

                    await childTask;

                    if (coordinatorToken.IsCancellationRequested ||
                        _dungeonCts?.IsCancellationRequested == true)
                        throw new OperationCanceledException(coordinatorToken);

                    if (_runError is not null)
                        throw new InvalidOperationException(
                            $"{plan.DisplayName} 작업 중 오류: {_runError}");

                    // The child completed successfully and deleted its single-plan
                    // resume checkpoint before the coordinator advances.
                    _activeMode = "가공";
                    _mode.Enabled = false;
                    _runError = null;
                },
                token);

            completed = true;
            _productionProgressSummary = $"다중가공 {plans.Count}/{plans.Count}종 완료";
            _log.Write("[다중가공] 전체 작업 정상 완료");
            SetStatus("다중가공 완료", Green);
        }
        catch (OperationCanceledException)
        {
            _log.Write("[다중가공] 정지 · 다음 품목은 시작하지 않음 · 현재 단일가공 이어하기 기록 유지");
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
