using System.Diagnostics;
using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // V0175: one exclusive capture loop owns reconnect input until a known screen returns.
    private bool _networkReconnectHandling;
    private bool _networkRestartRequested;
    private int _networkResumeStep;
    private bool _networkReturnedOutside;
    private int _networkRecoveryCount;
    private long _networkRecoveryWindow;

    private async Task<Bitmap> CaptureGameWindowRawAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        var frame = _capture.CaptureClient(_hwnd);
        try { _input.ObserveFrame(_hwnd, frame.Size); return frame; }
        catch { frame.Dispose(); throw; }
    }

    private async Task<bool> IsNetworkRetryPopupAsync(Bitmap frame, CancellationToken ct)
    {
        return (await _detector.DetectAsync("network_unstable_title", frame, ct)).Found &&
            (await _detector.DetectAsync("network_retry_button", frame, ct)).Found;
    }

    private async Task<bool> HasAbyssCombatEvidenceAsync(Bitmap frame, CancellationToken ct)
    {
        if ((await _detector.DetectAsync("abyss_enter", frame, ct)).Found ||
            await DetectAbyssOutsideWorkflowAsync(frame, ct)) return false;
        // Positive in-dungeon evidence, not absence of the entry/result dialog.
        return (await _detector.DetectAsync("abyss_leave_dungeon", frame, ct)).Found ||
            (await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct)).Found ||
            (await _detector.DetectAsync("abyss_touch_screen", frame, ct)).Found;
    }

    private async Task<int> ClassifyNetworkReturnAsync(Bitmap frame, CancellationToken ct)
    {
        if ((await DetectAbyssResultRetryAsync(frame, ct)).Found)
            return FindDungeonStepIndex("abyss_result_retry");
        if (await HasAbyssCombatEvidenceAsync(frame, ct)) return AbyssCombatStepIndex;
        if (await DetectAbyssOutsideWorkflowAsync(frame, ct)) return 0;
        if ((await _detector.DetectAsync("abyss_enter", frame, ct)).Found)
            return FindDungeonStepIndex("abyss_enter");
        return -1;
    }

    private void CompleteNetworkRestartRequest(string context)
    {
        _networkRestartRequested = false;
        _networkReconnectHandling = false;
        _resumeStepIndex = _networkResumeStep;
        _abyssExitInProgress = false;
        _abyssClearTitleFallbackConsecutive = 0;
        if (_networkReturnedOutside) _abyssCombatStartedAt = null;
        lock (_abyssFlowStateLock) _abyssFlowState = AbyssFlowState.Unknown;
        // Result counting remains latched if reconnect returned to the same result screen.
        if (_networkReturnedOutside) _abyssLootCountedForCurrentResult = false;
        Log?.Invoke($"[네트워크 복구] {context} -> 화면 확인 후 {_resumeStepIndex + 1}단계 재개, 기존 판 타이머 보존={_abyssCombatStartedAt.HasValue}");
    }

    private async Task HandleNetworkReconnectIfNeededAsync(Bitmap frame, CancellationToken ct)
    {
        if (!IsAbyss || _networkReconnectHandling || _networkRestartRequested) return;
        if (!await IsNetworkRetryPopupAsync(frame, ct)) return;
        _networkReconnectHandling = true;
        try
        {
            await Task.Delay(180, ct);
            using (var confirm = await CaptureGameWindowRawAsync(ct))
                if (!await IsNetworkRetryPopupAsync(confirm, ct)) return;

            long now = Environment.TickCount64;
            if (_networkRecoveryWindow == 0 || now - _networkRecoveryWindow >= 300_000)
            { _networkRecoveryWindow = now; _networkRecoveryCount = 0; }
            if (++_networkRecoveryCount > 3)
                throw new OperationCanceledException("5분 내 재접속 3회 초과 -> 반복 입력 없이 정지");

            DiagnosticPersistFailure("network_reconnect");
            _input.TapScanCode(0x39);
            var wait = Stopwatch.StartNew();
            int presses = 1, stable = 0, previousStep = -1;
            while (wait.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(500, ct);
                using var check = await CaptureGameWindowRawAsync(ct);
                if (await IsNetworkRetryPopupAsync(check, ct))
                {
                    stable = 0; previousStep = -1;
                    if (presses < 2 && wait.Elapsed >= TimeSpan.FromSeconds(5))
                    {
                        await Task.Delay(180, ct);
                        using var confirm = await CaptureGameWindowRawAsync(ct);
                        if (await IsNetworkRetryPopupAsync(confirm, ct))
                        { _input.TapScanCode(0x39); presses++; }
                    }
                    continue;
                }
                int step = await ClassifyNetworkReturnAsync(check, ct);
                stable = step >= 0 && step == previousStep ? stable + 1 : step >= 0 ? 1 : 0;
                previousStep = step;
                if (stable < 3) continue;
                _networkResumeStep = step;
                // Entry or field proves that the old dungeon is over; blank/loading never does.
                _networkReturnedOutside = step == 0 || step == FindDungeonStepIndex("abyss_enter");
                _networkRestartRequested = true;
                throw new RestartCycleException();
            }
            DiagnosticPersistFailure("network_return_unknown");
            throw new OperationCanceledException("재접속 후 60초 안에 실제 게임 화면을 확정하지 못해 정지합니다.");
        }
        finally { _networkReconnectHandling = false; }
    }
}
