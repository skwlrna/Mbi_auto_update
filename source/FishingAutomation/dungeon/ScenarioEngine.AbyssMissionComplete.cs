using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private async Task<bool> TryAcknowledgeScheduledMissionCompleteAsync(
        Bitmap initialFrame, CancellationToken ct)
    {
        if (!_scheduledResultExit || !_scheduledExitClicked ||
            _scheduledMissionConfirmationClicked ||
            !AbyssMissionCompletePolicy.HasVisualEvidence(initialFrame))
            return false;

        _abyssResultOcr ??= new OcrRecognizer();
        async Task<(DetectionResult Heading, bool Gift, DetectionResult Confirm)>
            EvidenceAsync(Bitmap frame)
        {
            var heading = await _abyssResultOcr.FindCompactLabelAsync(
                frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.TitleRoi, frame.Size),
                "협동 미션 완료", ct);
            bool gift = !heading.Found;
            if (gift)
                heading = await _abyssResultOcr.FindCompactLabelAsync(
                    frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.GiftRoi, frame.Size),
                    "미션 선물", ct);
            var confirm = heading.Found
                ? await _abyssResultOcr.FindCompactLabelAsync(
                    frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.ConfirmLabelRoi, frame.Size),
                    "확인", ct)
                : DetectionResult.NotFound;
            return (heading, gift, confirm);
        }

        var first = await EvidenceAsync(initialFrame);
        if (!first.Heading.Found || !first.Confirm.Found)
        {
            Log?.Invoke("[병행] 협동 미션 완료 화면 색상 후보 · 정확한 제목/확인 OCR 미확인 -> 입력 보류");
            return false;
        }

        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using var fresh = await CaptureGameWindowAsync(ct);
        if (!AbyssMissionCompletePolicy.HasVisualEvidence(fresh))
            return false;
        var second = await EvidenceAsync(fresh);
        if (first.Gift != second.Gift ||
            !AbyssMissionCompletePolicy.IsStableTextEvidence(
                first.Heading.Bounds, second.Heading.Bounds,
                first.Confirm.Bounds, second.Confirm.Bounds,
                initialFrame.Size, fresh.Size, first.Gift))
            return false;

        // Never use OCR text bounds as a click target. Only fixed center of
        // one verified full-width bar; re-check live client geometry/F10.
        var click = AbyssMissionCompletePolicy.ConfirmCenter(fresh.Size);
        ct.ThrowIfCancellationRequested();
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        VerifyAbyssClickClientGeometry(_hwnd, fresh.Size);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke("[병행] 협동 미션 완료/미션 선물 + 확인 2프레임 일치 · " +
                    "단일 초록 버튼 검증 -> 확인 1회 클릭 @ " + click);
        DiagnosticObserveClick("abyss_mission_complete_confirm", click);
        _input.ClickClientPoint(_hwnd, click);
        _scheduledMissionConfirmationClicked = true;
        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
        return true;
    }
}
