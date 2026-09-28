using System.Diagnostics;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private long? _abyssCombatStartedAt;
    private bool _abyssExitInProgress;

    private void ResetAbyssExitState(bool clearConfirmed = false)
    {
        _abyssExitInProgress = false;
        Log?.Invoke(clearConfirmed ? "[어비스 퇴장 상태 초기화] 클리어 확인" : "[어비스 퇴장 상태 초기화] 정상 진행");
    }

    private async Task StartAbyssCombatClockAsync(CancellationToken ct)
    {
        if (_abyssCombatStartedAt.HasValue) return; // Recovery must not extend this round.
        ResetAbyssExitState();
        var wait = Stopwatch.StartNew();
        int gone = 0;
        while (wait.Elapsed < TimeSpan.FromSeconds(60))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            if ((await _detector.DetectAsync("abyss_enter", frame, ct)).Found)
                gone = 0;
            else if (await HasAbyssCombatEvidenceAsync(frame, ct))
            {
                if (++gone < 3) { await Task.Delay(250, ct); continue; }
                // Same screen-transition evidence used after the existing retry action.
                // Entry-button waiting time is not charged to the combat limit.
                _abyssCombatStartedAt = Environment.TickCount64;
                return;
            }
            else gone = 0;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        throw new InvalidOperationException("어비스 입장 화면 이탈을 확인하지 못해 전투 타이머를 시작하지 않습니다.");
    }

    private async Task<bool> HandleAbyssLateClearAsync(Bitmap frame, CancellationToken ct)
    {
        // Check the touch prompt before every exit input, including the confirmation.
        // A visible prompt alone must veto an exit even if the title template misses.
        var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
        if (touch.Found)
        {
            _abyssClearTitleFallbackConsecutive = 0;
            ResetAbyssExitState(true);
            AbyssTransitionTo(AbyssFlowState.ClearConfirmed, "10분 경계에서 화면 터치 문구 확인");
            await AdvanceAbyssClearScreenAsync(touch, ct);
            return true;
        }

        // The first strong-title frame only arms the fallback and blocks exit input.
        // The second consecutive strong-title frame returns the safe touch rectangle.
        var confirmedClear = await DetectAbyssConfirmedClearAsync(frame, ct);
        if (confirmedClear.Found)
        {
            ResetAbyssExitState(true);
            Log?.Invoke("[어비스 타임아웃] 클리어 타이틀 fallback 확정 -> 강제 퇴장 취소");
            await AdvanceAbyssClearScreenAsync(confirmedClear, ct);
            return true;
        }

        var result = await DetectAbyssResultRetryAsync(frame, ct);
        if (result.Found)
        {
            _abyssClearTitleFallbackConsecutive = 0;
            ResetAbyssExitState(true);
            AbyssTransitionTo(AbyssFlowState.ClearConfirmed, "10분 경계에서 실제 결과 화면 확인");
            AbyssTransitionTo(AbyssFlowState.ResultConfirmed, "기존 결과 처리로 연결");
            return true;
        }
        return false;
    }

    private async Task ExitAbyssAfterCombatTimeoutAsync(ScenarioStep step, CancellationToken ct)
    {
        if (!_abyssCombatStartedAt.HasValue ||
            Environment.TickCount64 - _abyssCombatStartedAt.Value < 600_000)
            throw new InvalidOperationException("전투 10분 초과가 확인되지 않아 퇴장을 차단합니다.");

        try
        {
            foreach (var action in new[] {
                (Target: step.TimeoutClickTarget, Seconds: step.TimeoutClickTargetWaitSeconds),
                (Target: step.TimeoutFollowupClickTarget, Seconds: step.TimeoutFollowupWaitSeconds) })
            {
                if (string.IsNullOrWhiteSpace(action.Target))
                    throw new InvalidOperationException("어비스 퇴장 대상 설정이 없습니다.");
                var wait = Stopwatch.StartNew();
                bool clicked = false;
                DetectionResult previous = DetectionResult.NotFound;
                while (wait.Elapsed < TimeSpan.FromSeconds(action.Seconds))
                {
                    ct.ThrowIfCancellationRequested();
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    using var frame = await CaptureGameWindowAsync(ct);
                    if (await HandleAbyssLateClearAsync(frame, ct)) return;

                    // One strong clear-title frame is enough to veto this frame's exit input.
                    // The next fresh frame must confirm 2/2 before the safe clear touch is sent.
                    if (AbyssClearTitleFallbackPending)
                    {
                        previous = DetectionResult.NotFound;
                        Log?.Invoke("[어비스 타임아웃] 클리어 타이틀 후보 확인 중 -> 퇴장 입력 보류");
                        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                        continue;
                    }

                    var target = await _detector.DetectAsync(action.Target, frame, ct);
                    if (target.Found && previous.Found && target.Bounds.IntersectsWith(previous.Bounds))
                    {
                        // Detection and the input share this fresh frame. No monitor inputs intervene.
                        ct.ThrowIfCancellationRequested();
                        _input.ClickClientPoint(_hwnd, target.Center);
                        if (!_abyssExitInProgress)
                        {
                            _abyssExitInProgress = true;
                            Log?.Invoke("[어비스 타임아웃] 퇴장 입력 시작");
                        }
                        clicked = true;
                        await Task.Delay(_settings.ClickSettleMs, ct);
                        break;
                    }
                    previous = target;
                    await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                }
                if (!clicked)
                    throw new InvalidOperationException("10분 초과 후 어비스 퇴장 버튼을 확인하지 못해 정지합니다.");
            }
            await WaitForAbyssHomeAfterNormalExitAsync(ct);
            ResetAbyssExitState();
            _abyssCombatStartedAt = null;
            Log?.Invoke($"[어비스] 강제 퇴장 완료 및 밖 확인 -> {step.TimeoutRestartDelaySeconds}초 대기 후 처음부터 재시작");
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, step.TimeoutRestartDelaySeconds)), ct);
            throw new RestartCycleException();
        }
        finally
        {
            _abyssExitInProgress = false;
            Log?.Invoke("[어비스 퇴장 절차 종료]");
        }
    }
}
