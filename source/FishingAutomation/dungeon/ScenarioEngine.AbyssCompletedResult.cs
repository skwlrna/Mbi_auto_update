using System.Diagnostics;
using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // The 2026-11-13 incident screenshot shows these TWO independent
    // finished-rewards labels even while a confirmation modal darkens them.
    // Never use either text alone to authorize a click.
    private static readonly Rectangle AbyssFinishedTimeRoi = new(320, 222, 270, 80);
    private static readonly Rectangle AbyssFinishedLootRoi = new(295, 450, 265, 100);
    private static readonly Rectangle AbyssTimeoutConfirmExitRoi = new(392, 863, 238, 100);

    private async Task<bool> HasAbyssCompletedResultEvidenceAsync(Bitmap frame, CancellationToken ct)
    {
        if (!IsAbyss || AbyssMissionCompletePolicy.HasVisualEvidence(frame))
            return false;
        // V3.3.19: never classify the still-visible '던전 클리어!' and
        // '화면을 터치해 주세요' overlay as a ready result. This guard
        // must run BEFORE the green row and alternative rewards OCR paths.
        var pendingTitle = await _detector.DetectAsync(
            "abyss_dungeon_clear_visual", frame, ct);
        var pendingTouch = await _detector.DetectAsync(
            "abyss_touch_screen", frame, ct);
        var touchArea = ScaleAbyssResultRoi(
            AbyssClearTouchRecoverySafeRoi, frame.Size);
        if (pendingTitle.Found && pendingTouch.Found &&
            touchArea.Contains(pendingTouch.Bounds))
        {
            Log?.Invoke("[어비스 완료결과] 클리어 제목+화면 터치가 아직 표시됨 -> 결과 단계 승격 보류");
            return false;
        }

        // Existing three-green-button proof is still authoritative.
        if (TryDetectAbyssResultButtonRow(frame, out _))
        {
            // Before accepting even a strong result row, make sure no
            // pending exit-confirmation dialog is covering the game.
            return !(await _detector.DetectAsync("abyss_exit_confirm_title", frame, ct)).Found;
        }

        // Separate anchored OCR labels support the alternate rewards layout
        // where the standard three-green-button detector cannot see the row.
        _abyssResultOcr ??= new OcrRecognizer();
        var timeArea = ScaleAbyssResultRoi(AbyssFinishedTimeRoi, frame.Size);
        var lootArea = ScaleAbyssResultRoi(AbyssFinishedLootRoi, frame.Size);
        var time = await _abyssResultOcr.FindCompactLabelAsync(
            frame, timeArea, "전투 시간", ct);
        if (!time.Found || !timeArea.Contains(time.Bounds))
            return false;
        var loot = await _abyssResultOcr.FindCompactLabelAsync(
            frame, lootArea, "발견한 전리품", ct);
        if (!loot.Found || !lootArea.Contains(loot.Bounds))
            return false;
        return !(await _detector.DetectAsync("abyss_exit_confirm_title", frame, ct)).Found;
    }

    // Recognition authorizes navigation to the normal result-processing state,
    // NOT a blind tap. The next result step retains all button/geometry guards.
    private async Task<bool> ConfirmAbyssCompletedResultAsync(
        Bitmap firstFrame, CancellationToken ct)
    {
        if (!await HasAbyssCompletedResultEvidenceAsync(firstFrame, ct))
            return false;
        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using var fresh = await CaptureGameWindowAsync(ct);
        bool confirmed = await HasAbyssCompletedResultEvidenceAsync(fresh, ct);
        if (confirmed)
            Log?.Invoke("[어비스 완료결과] 클리어 오버레이와 무관하게 실제 보상/결과 화면 2프레임 확인");
        return confirmed;
    }

    private void MarkAbyssCompletedResult(string reason)
    {
        _abyssClearPairedEvidenceFrames = 0;
        ResetAbyssExitState(true);
        AbyssTransitionTo(AbyssFlowState.ResultConfirmed, reason);
        Log?.Invoke("[어비스 완료결과] 강제 퇴장 취소 -> 기존 다시 하기/가공 필요 시 나가기 단계로 전달");
    }

    // Only a two-frame verified modal (title AND right-hand label) may receive
    // either ESC or Space; '나가기' elsewhere in the game is never sufficient.
    private async Task<bool> WaitForAbyssTimeoutExitDialogAsync(
        int timeoutSeconds, CancellationToken ct)
    {
        var wait = Stopwatch.StartNew();
        DetectionResult oldTitle = DetectionResult.NotFound;
        DetectionResult oldExit = DetectionResult.NotFound;
        _abyssResultOcr ??= new OcrRecognizer();
        while (wait.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            // Check the modal FIRST: dimmed underlying rewards text must
            // never authorize a transition without dismissing this dialog.
            var title = await _detector.DetectAsync("abyss_exit_confirm_title", frame, ct);
            var exitArea = ScaleAbyssResultRoi(AbyssTimeoutConfirmExitRoi, frame.Size);
            var exitLabel = title.Found
                ? await _abyssResultOcr.FindCompactLabelAsync(frame, exitArea, "나가기", ct)
                : DetectionResult.NotFound;
            if (title.Found && (!exitLabel.Found || !exitArea.Contains(exitLabel.Bounds)))
            {
                // The existing green exit.png is a legitimate OCR fallback
                // ONLY inside the modal's right-hand button rectangle.
                var templated = await _detector.DetectAsync("abyss_exit", frame, ct);
                exitLabel = templated.Found && exitArea.Contains(templated.Bounds)
                    ? templated
                    : DetectionResult.NotFound;
            }
            bool valid = title.Found && exitLabel.Found &&
                exitArea.Contains(exitLabel.Bounds);
            if (valid && oldTitle.Found && oldExit.Found &&
                oldTitle.Bounds.IntersectsWith(title.Bounds) &&
                oldExit.Bounds.IntersectsWith(exitLabel.Bounds))
            {
                Log?.Invoke("[어비스 강제퇴장] 던전 퇴장 확인창 제목 + 오른쪽 나가기 2프레임 확인");
                return true;
            }
            oldTitle = valid ? title : DetectionResult.NotFound;
            oldExit = valid ? exitLabel : DetectionResult.NotFound;
            if (!title.Found && await ConfirmAbyssCompletedResultAsync(frame, ct))
            {
                MarkAbyssCompletedResult("강제퇴장 확인창 대기 중 정상 완료 결과 확인");
                return false;
            }
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        throw new InvalidOperationException(
            "강제 퇴장 확인창의 제목/오른쪽 나가기 2프레임 검증 실패 · 임의 나가기 클릭 금지");
    }
}
