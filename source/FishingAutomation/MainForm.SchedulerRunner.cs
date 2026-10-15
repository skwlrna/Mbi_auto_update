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
        if (_schedulerPage is null)
        {
            _log.Write("[스케줄러] 시작 차단: UI 초기화가 완료되지 않았습니다.");
            SetStatus("스케줄러 UI 초기화 필요", Color.DarkOrange);
            return;
        }
        if (AnyRunning)
        {
            string reason = "시작 차단: 다른 작업/준비가 진행 중입니다. F10으로 정지한 뒤 다시 시도하세요.";
            _log.Write("[스케줄러] " + reason);
            _schedulerPage.ReportStartFeedback(reason);
            SetStatus(reason, Color.DarkOrange);
            return;
        }

        bool runCrafting = _schedulerPage.UseCrafting;
        bool runAltering = _schedulerPage.UseAltering;
        bool runAbyss = _schedulerPage.UseAbyss;

        if (!runCrafting && !runAltering)
        {
            const string reason = "시작 차단: 작업 계획에서 제작 또는 가공을 선택하세요.";
            _log.Write("[스케줄러] " + reason);
            _schedulerPage.ReportStartFeedback(reason);
            SetStatus(reason, Color.DarkOrange);
            return;
        }
        if (runAbyss && !runAltering)
        {
            const string reason = "시작 차단: 어비스 병행은 가공 작업을 함께 선택해야 합니다.";
            _log.Write("[스케줄러] " + reason);
            _schedulerPage.ReportStartFeedback(reason);
            SetStatus(reason, Color.DarkOrange);
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
                // A saved batch may be resumed even when the current UI queue
                // is empty. Validate the selected *execution* plan after choice.
                alteringPlans = queued.Count > 0 ? queued : Array.Empty<AlteringPlan>();
            }
        }
        catch (Exception ex)
        {
            string reason = "시작 차단: " + ex.Message;
            _log.Write("[스케줄러] 작업 설정 확인 실패: " + ex.Message);
            _schedulerPage.ReportStartFeedback(reason);
            SetStatus(reason, Color.DarkOrange);
            return;
        }

        // Defensive guard for a second start arriving through a different UI entry.
        // No game input, ledger mutation or worker start may occur on rejection.
        if (!_schedulerMonitor.TryStart(runCrafting, runAltering, runAbyss))
        {
            const string reason = "시작 차단: 스케줄러가 이미 실행 중입니다.";
            _log.Write("[스케줄러] " + reason);
            _schedulerPage.ReportStartFeedback(reason);
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

        string schedulerOutcome = "cancelled";
        try
        {
            SchedulerAlteringResumeSelection? resume = null;
            if (runAltering)
            {
                _schedulerMonitor.Advance("resume_selection");
                // Decide BEFORE any crafting input: the saved batch is never
                // silently replaced by a fresh F9 ledger.
                var choice = await ChooseSchedulerMultiAlteringAsync(
                    alteringPlans, runCrafting, token);
                token.ThrowIfCancellationRequested();
                if (choice.Cancelled)
                {
                    _log.Write("[스케줄러] 가공 실행 선택 취소 · 제작/가공/어비스 입력 없음");
                    SetStatus("스케줄러 시작 취소", Color.DarkOrange);
                    return;
                }
                resume = choice.Resume;
                if (resume is not null)
                    alteringPlans = resume.Plans;
                else if (alteringPlans.Count == 0)
                    alteringPlans = new[] { SelectedAlteringPlan() };
                foreach (var plan in alteringPlans) plan.Validate();
                if (resume is not null)
                    _schedulerMonitor.MarkVerifiedBoundary("altering_resume_user_verified");
                _log.Write(resume is null
                    ? "[스케줄러] 신규 다중가공 선택 · 기존 장부 자동 복원 없음"
                    : "[스케줄러] 저장 다중가공 이어하기 선택 · 배치 " + resume.BatchId);
            }

            if (runCrafting)
            {
                token.ThrowIfCancellationRequested();
                _schedulerMonitor.Advance("crafting");
                _log.Write("[스케줄러] 1단계 제작 시작 · 기존 전체 음식/아이템 검색에서 선택한 품목 사용");
                _dungeonTask = null;
                await StartCraftingAsync();
                token.ThrowIfCancellationRequested();
                if (_dungeonTask is not Task craftingTask)
                    throw new InvalidOperationException("제작 작업이 시작되지 않았습니다. 후속 가공/어비스를 실행하지 않습니다.");

                await craftingTask;
                _schedulerMonitor.Advance("handoff");
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
                _schedulerMonitor.MarkVerifiedBoundary("crafting_input_released");
                _log.Write("[스케줄러] 제작 완료 확인 · 제작 화면 입력 종료");
            }

            if (runAltering)
            {
                token.ThrowIfCancellationRequested();
                _combinedAlteringRequested = runAbyss;
                _schedulerMonitor.Advance("altering");
                _log.Write("[스케줄러] 2단계 다중가공 시작" +
                    (runAbyss ? " · 가공 대기 중 어비스 1판씩" : ""));
                // This call waits for coordinator completion and exclusively
                // owns registration, collection, durable ledger and idle Abyss.
                await StartMultiAlteringAsync(
                    alteringPlans, resume?.Directory, resume?.BatchId,
                    resume?.ConfirmedSingleCharacter ?? false);
                token.ThrowIfCancellationRequested();
                if (_runError is not null)
                    throw new InvalidOperationException("다중가공이 안전 정지했습니다: " + _runError);
            }

            token.ThrowIfCancellationRequested();
            schedulerOutcome = "completed";
            _log.Write("[스케줄러] 선택한 단계 종료 · 새로운 작업 자동 시작 없음");
            SetStatus("스케줄러 작업 종료", Green);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            schedulerOutcome = "cancelled";
            _log.Write("[스케줄러] F10 정지 · 후속 단계 실행 차단 · 기존 작업 기록 보존");
            SetStatus("스케줄러 중단", Color.DarkOrange);
        }
        catch (Exception ex)
        {
            schedulerOutcome = "failed";
            _runError = ex.Message;
            _log.Write("[스케줄러] 안전 정지: " + ex.Message);
            // Also update the visible scheduler feedback. Missing fresh plans
            // are now checked after the resume choice, inside this try block.
            _schedulerPage.ReportStartFeedback("시작 차단: " + ex.Message);
            SetStatus("스케줄러 오류: " + ex.Message, Color.Salmon);
        }
        finally
        {
            _schedulerMonitor.Finish(schedulerOutcome);
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
