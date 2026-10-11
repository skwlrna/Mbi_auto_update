namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _schedulerRunning;
    private CancellationTokenSource? _schedulerCts;

    // Phase 1 of the unified supervisor uses only existing, verified execution
    // engines. Crafting is a completed phase, not a parallel actor. The optional
    // Abyss excursion is owned by the altering coordinator's idle boundary.
    // No new worker is authorized to press keys while another mode owns them.
    private async Task StartSchedulerAsync()
    {
        if (AnyRunning || _schedulerPage is null) return;

        bool runCrafting = _schedulerPage.UseCrafting;
        bool runAltering = _schedulerPage.UseAltering;
        bool runAbyss = _schedulerPage.UseAbyss;

        if (!runCrafting && !runAltering)
        {
            SetStatus("스케줄러: 제작 또는 가공을 선택하세요.", Color.DarkOrange);
            return;
        }
        if (runAbyss && !runAltering)
        {
            SetStatus("어비스 병행은 가공 대기와 함께 사용해야 합니다.", Color.DarkOrange);
            return;
        }

        // Validate both planned jobs before touching the game. Never secretly
        // substitute the first item in a search catalog.
        IReadOnlyList<AlteringPlan> alteringPlans = Array.Empty<AlteringPlan>();
        try
        {
            if (runCrafting)
                SelectedCraftingPlan().Validate();
            if (runAltering)
            {
                var queued = _alteringPage.QueuedAlteringPlans;
                alteringPlans = queued.Count > 0 ? queued : new[] { SelectedAlteringPlan() };
                foreach (var plan in alteringPlans) plan.Validate();
            }
        }
        catch (Exception ex)
        {
            _log.Write("[스케줄러] 작업 설정 확인 실패: " + ex.Message);
            SetStatus("스케줄러 설정 확인 필요", Color.DarkOrange);
            return;
        }

        _schedulerCts?.Dispose();
        _schedulerCts = new CancellationTokenSource();
        var token = _schedulerCts.Token;
        _schedulerRunning = true;
        _combinedAlteringRequested = false;
        _schedulerPage.RefreshSnapshot();
        _log.Write("[스케줄러] 시작 · " +
            (runCrafting ? "제작 → " : "") +
            (runAltering ? (runAbyss ? "다중가공 + 대기 중 어비스" : "다중가공") : "제작 단독") +
            " · F10 전체 중지");

        try
        {
            if (runCrafting)
            {
                token.ThrowIfCancellationRequested();
                _log.Write("[스케줄러] 1단계 제작 시작 · 기존 전체 음식/아이템 검색에서 선택한 품목 사용");
                _dungeonTask = null;
                await StartCraftingAsync();
                token.ThrowIfCancellationRequested();
                if (_dungeonTask is not Task craftingTask)
                    throw new InvalidOperationException("제작 작업이 시작되지 않았습니다. 후속 가공/어비스를 실행하지 않습니다.");

                await craftingTask;
                // The existing Crafting runner dispatches UI completion through
                // the UI synchronization context. Wait until it releases ownership.
                for (int i = 0; _activeMode == "제작" && i < 100; i++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(100, token);
                }
                token.ThrowIfCancellationRequested();
                if (_activeMode is not null)
                    throw new InvalidOperationException("제작 입력권 반환을 확인하지 못했습니다. 가공 단계로 넘기지 않습니다.");
                if (_runError is not null)
                    throw new InvalidOperationException("제작 오류가 발생해 다음 단계로 넘기지 않습니다: " + _runError);
                _log.Write("[스케줄러] 제작 완료 확인 · 제작 화면 입력 종료");
            }

            if (runAltering)
            {
                token.ThrowIfCancellationRequested();
                _combinedAlteringRequested = runAbyss;
                _log.Write("[스케줄러] 2단계 다중가공 시작" +
                    (runAbyss ? " · 가공 대기 중 어비스 1판씩" : ""));
                // This call waits for coordinator completion and exclusively
                // owns registration, collection, durable ledger and idle Abyss.
                await StartMultiAlteringAsync(alteringPlans);
                token.ThrowIfCancellationRequested();
                if (_runError is not null)
                    throw new InvalidOperationException("다중가공이 안전 정지했습니다: " + _runError);
            }

            token.ThrowIfCancellationRequested();
            _log.Write("[스케줄러] 선택한 단계 종료 · 새로운 작업 자동 시작 없음");
            SetStatus("스케줄러 작업 종료", Green);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _log.Write("[스케줄러] F10 정지 · 후속 단계 실행 차단 · 기존 작업 기록 보존");
            SetStatus("스케줄러 중단", Color.DarkOrange);
        }
        catch (Exception ex)
        {
            _runError = ex.Message;
            _log.Write("[스케줄러] 안전 정지: " + ex.Message);
            SetStatus("스케줄러 오류: " + ex.Message, Color.Salmon);
        }
        finally
        {
            _combinedAlteringRequested = false;
            _schedulerRunning = false;
            // A stopped child retains its own durable ledger; we only drop
            // this UI scheduler's in-memory cancellation token.
            _schedulerCts?.Dispose();
            _schedulerCts = null;
            if (!IsDisposed)
            {
                _mode.Enabled = _activeMode is null;
                _schedulerPage.RefreshSnapshot();
                UpdateStats();
            }
        }
    }
}
