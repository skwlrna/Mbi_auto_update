using System.Diagnostics;
using System.Drawing;

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

    private OcrRecognizer? _abyssEntryOcr;
    private int _abyssEntrySpaceCount;
    private long _abyssEntryLastSpaceAt;
    private static readonly Rectangle AbyssEntryButtonSafeArea = new(355, 900, 290, 96);
    private const int MaxAbyssEntrySpacePresses = 3;

    // A template can briefly miss an animated, still-open entry button.
    // Exact OCR of the lower-right word is an independent veto against combat.
    private async Task<DetectionResult> DetectAbyssEntryPanelAsync(Bitmap frame, CancellationToken ct)
    {
        var button = await _detector.DetectAsync("abyss_enter", frame, ct);
        if (button.Found && AbyssEntryButtonSafeArea.Contains(button.Bounds))
            return button;
        _abyssEntryOcr ??= new OcrRecognizer();
        var label = await _abyssEntryOcr.FindCompactLabelAsync(
            frame, AbyssEntryButtonSafeArea, "입장하기", ct);
        return label.Found && AbyssEntryButtonSafeArea.Contains(label.Bounds)
            ? label
            : DetectionResult.NotFound;
    }

    // Guard both the first press and every retry; never use a stale sighting.
    private async Task<bool> VerifyStableAbyssEntryReadyAsync(CancellationToken ct)
    {
        await Task.Delay(1200, ct);
        using var first = await CaptureGameWindowAsync(ct);
        var a = await DetectAbyssEntryPanelAsync(first, ct);
        await Task.Delay(260, ct);
        using var second = await CaptureGameWindowAsync(ct);
        var b = await DetectAbyssEntryPanelAsync(second, ct);
        bool stable = a.Found && b.Found &&
            Math.Abs(a.Center.X - b.Center.X) <= 45 &&
            Math.Abs(a.Center.Y - b.Center.Y) <= 30;
        Log?.Invoke($"[어비스 입장] 안정화 확인 · 2프레임={(stable ? "성공" : "실패")} · first={a.Bounds} second={b.Bounds}");
        return stable;
    }

    private async Task StartAbyssCombatClockAsync(CancellationToken ct)
    {
        if (_abyssCombatStartedAt.HasValue) return; // Recovery must not extend this round.
        ResetAbyssExitState();
        var wait = Stopwatch.StartNew();
        int gone = 0;
        int entryFrames = 0;
        while (wait.Elapsed < TimeSpan.FromSeconds(60))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var entry = await DetectAbyssEntryPanelAsync(frame, ct);
            if (entry.Found)
            {
                // Entry screen is authoritative even if loose visual templates
                // accidentally resemble an in-dungeon HUD or clear prompt.
                gone = 0;
                entryFrames++;
                if (entryFrames >= 2 &&
                    Environment.TickCount64 - _abyssEntryLastSpaceAt >= 2200)
                {
                    if (_abyssEntrySpaceCount >= MaxAbyssEntrySpacePresses)
                        throw new InvalidOperationException(
                            "어비스 입장하기 Space 3회 후에도 입장 화면이 남아 있어 안전 정지합니다.");
                    if (await VerifyStableAbyssEntryReadyAsync(ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        _hwnd = await ResolveRequiredGameWindowAsync(ct);
                        NativeMethods.SetForegroundWindow(_hwnd);
                        Log?.Invoke($"[어비스 입장] 버튼 유지 확인 -> Space 재입력 {_abyssEntrySpaceCount + 1}/3");
                        _input.TapScanCode(0x39, ct);
                        _abyssEntrySpaceCount++;
                        _abyssEntryLastSpaceAt = Environment.TickCount64;
                        entryFrames = 0;
                        await Task.Delay(2000, ct);
                    }
                }
            }
            else
            {
                entryFrames = 0;
                // Dungeon-selection artwork must never authorize the combat clock.
                // A loose touch/leave icon match may exist on that screen.
                var selection = await _detector.DetectAsync(
                    "abyss_dungeon_hallucination_anchorage", frame, ct);
                if (selection.Found)
                {
                    gone = 0;
                }
                else if (Environment.TickCount64 - _abyssEntryLastSpaceAt >= 2200 &&
                         await HasAbyssCombatEvidenceAsync(frame, ct))
                {
                    if (++gone >= 3)
                    {
                        _abyssCombatStartedAt = Environment.TickCount64;
                        Log?.Invoke("[어비스 입장] 선택/입장 화면 없음 + 던전 내부 증거 3프레임 -> 전투 타이머 시작");
                        return;
                    }
                }
                else
                {
                    gone = 0;
                }
            }
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        throw new InvalidOperationException(
            "60초 내 어비스 입장 화면 이탈과 실제 던전 내부 3프레임을 확인하지 못해 전투 타이머를 시작하지 않습니다.");
    }

    private async Task<bool> HandleAbyssLateClearAsync(Bitmap frame, CancellationToken ct)
    {
        // Check the touch prompt before every exit input, including the confirmation.
        // A visible prompt alone must veto an exit even if the title template misses.
        // A touch-like combat overlay is not proof of a clear. Confirm the
        // independent title AND touch prompt across two fresh frames below.
        // In particular never bypass the paired-evidence policy here.

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
            _abyssClearPairedEvidenceFrames = 0;
            ResetAbyssExitState(true);
            AbyssTransitionTo(AbyssFlowState.ClearConfirmed, "10분 경계에서 실제 결과 화면 확인");
            AbyssTransitionTo(AbyssFlowState.ResultConfirmed, "기존 결과 처리로 연결");
            return true;
        }
        return false;
    }

    // The first timeout action is an in-dungeon HUD control, not a result
    // screen button. Two independent captures must agree before clicking.
    // A finished reward screen takes priority even after 10 minutes.
    private async Task<bool> ClickAbyssTimeoutLeaveControlAsync(
        int waitSeconds, CancellationToken ct)
    {
        var wait = Stopwatch.StartNew();
        DetectionResult previous = DetectionResult.NotFound;
        while (wait.Elapsed < TimeSpan.FromSeconds(waitSeconds))
        {
            ct.ThrowIfCancellationRequested();
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            NativeMethods.SetForegroundWindow(_hwnd);
            using var frame = await CaptureGameWindowAsync(ct);
            if ((await DetectAbyssEntryPanelAsync(frame, ct)).Found)
                throw new InvalidOperationException(
                    "10분 경계에서 입장하기 화면 확인 -> 던전 퇴장 입력 금지");

            if (await ConfirmAbyssCompletedResultAsync(frame, ct))
            {
                MarkAbyssCompletedResult("10분 경계에서 완료 보상 화면 직접 확인");
                return false;
            }
            if (await HandleAbyssLateClearAsync(frame, ct)) return false;
            if (AbyssClearPairedEvidencePending)
            {
                previous = DetectionResult.NotFound;
                Log?.Invoke("[어비스 타임아웃] 클리어 후보 확인 중 -> 퇴장 입력 보류");
                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                continue;
            }

            var target = await _detector.DetectAsync("abyss_leave_dungeon", frame, ct);
            if (target.Found && previous.Found &&
                target.Bounds.IntersectsWith(previous.Bounds))
            {
                ct.ThrowIfCancellationRequested();
                _input.ClickClientPoint(_hwnd, target.Center);
                _abyssExitInProgress = true;
                DiagnosticObserveClick("abyss_timeout_leave", target.Center);
                Log?.Invoke($"[어비스 타임아웃] 던전 퇴장 아이콘 2프레임 확인 -> 클릭 @ {target.Center}");
                await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
                return true;
            }
            previous = target;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        throw new InvalidOperationException(
            "10분 초과 후 던전 퇴장 아이콘 2프레임을 확인하지 못해 안전 정지합니다.");
    }

    private async Task<bool> IsAbyssTimeoutExitDialogVisibleAsync(
        CancellationToken ct)
    {
        using var frame = await CaptureGameWindowAsync(ct);
        return (await _detector.DetectAsync("abyss_exit_confirm_title", frame, ct)).Found;
    }

    private async Task ExitAbyssAfterCombatTimeoutAsync(ScenarioStep step, CancellationToken ct)
    {
        if (!_abyssCombatStartedAt.HasValue ||
            Environment.TickCount64 - _abyssCombatStartedAt.Value < 600_000)
            throw new InvalidOperationException("전투 10분 초과가 확인되지 않아 퇴장을 차단합니다.");

        if (string.IsNullOrWhiteSpace(step.TimeoutClickTarget) ||
            step.TimeoutClickTarget != "abyss_leave_dungeon" ||
            step.TimeoutFollowupClickTarget != "abyss_exit")
            throw new InvalidOperationException("예상하지 못한 강제 퇴장 구성. 안전 정지합니다.");

        try
        {
            // Late completion is always handled before a forced exit.
            using (var before = await CaptureGameWindowAsync(ct))
            {
                if (await ConfirmAbyssCompletedResultAsync(before, ct))
                {
                    MarkAbyssCompletedResult("10분 종료 직전 완료 결과 재확인");
                    return;
                }
            }

            if (!await ClickAbyssTimeoutLeaveControlAsync(
                step.TimeoutClickTargetWaitSeconds, ct)) return;
            if (!await WaitForAbyssTimeoutExitDialogAsync(
                step.TimeoutFollowupWaitSeconds, ct)) return;

            // A late result can already be underneath the darkened exit
            // dialog. Cancel ONCE, expose the underlying screen, then inspect.
            // Never send Space just because '나가기' is visible somewhere.
            ct.ThrowIfCancellationRequested();
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            NativeMethods.SetForegroundWindow(_hwnd);
            Log?.Invoke("[어비스 타임아웃] 확인창 2프레임 확인 -> ESC 1회로 결과 화면 재점검");
            _input.TapScanCode(0x01, ct);
            await Task.Delay(650, ct);
            if (await IsAbyssTimeoutExitDialogVisibleAsync(ct))
                throw new InvalidOperationException(
                    "퇴장 확인창 ESC 후에도 표시됨 · Space/추가 입력 없이 안전 정지");

            using (var afterCancel = await CaptureGameWindowAsync(ct))
            {
                if (await ConfirmAbyssCompletedResultAsync(afterCancel, ct))
                {
                    MarkAbyssCompletedResult("퇴장 확인창 ESC 취소 후 완료 보상 화면 확인");
                    return;
                }

                // A failed or ambiguous result recognition must never cause
                // an irreversible exit from a completed dungeon.
                if (!await HasAbyssCombatEvidenceAsync(afterCancel, ct))
                    throw new InvalidOperationException(
                        "ESC 후 완료 결과/전투 화면 구분 불가 · 추가 퇴장 입력 없이 안전 정지");
            }
            await Task.Delay(300, ct);
            using (var freshCombat = await CaptureGameWindowAsync(ct))
            {
                if (await ConfirmAbyssCompletedResultAsync(freshCombat, ct))
                {
                    MarkAbyssCompletedResult("퇴장 재시도 직전 완료 결과 재확인");
                    return;
                }
                if (!await HasAbyssCombatEvidenceAsync(freshCombat, ct))
                    throw new InvalidOperationException(
                        "ESC 후 던전 내부 증거 2프레임 불충분 · 무조건 퇴장 금지");
            }

            // Only verified incomplete combat may reopen a confirmation once.
            if (!await ClickAbyssTimeoutLeaveControlAsync(
                step.TimeoutClickTargetWaitSeconds, ct)) return;
            if (!await WaitForAbyssTimeoutExitDialogAsync(
                step.TimeoutFollowupWaitSeconds, ct)) return;
            ct.ThrowIfCancellationRequested();
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            NativeMethods.SetForegroundWindow(_hwnd);
            Log?.Invoke("[어비스 타임아웃] 미완료 전투 + 퇴장 확인창 2프레임 -> Space 1회");
            _input.TapScanCode(0x39, ct);
            var confirmWait = Stopwatch.StartNew();
            int disappeared = 0;
            while (confirmWait.Elapsed < TimeSpan.FromSeconds(8))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(350, ct);
                if (await IsAbyssTimeoutExitDialogVisibleAsync(ct))
                {
                    disappeared = 0;
                    continue;
                }
                if (++disappeared >= 2) break;
            }
            if (disappeared < 2)
                throw new InvalidOperationException(
                    "확인창 Space 1회 후 8초간 팝업 유지 · Space 반복 없이 안전 정지");

            await WaitForAbyssHomeAfterNormalExitAsync(ct);
            ResetAbyssExitState();
            _abyssCombatStartedAt = null;
            Log?.Invoke($"[어비스] 미완료 던전 강제 퇴장 및 밖 확인 -> {step.TimeoutRestartDelaySeconds}초 후 재시작");
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
