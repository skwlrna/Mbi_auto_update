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
        bool exactOcr = first.Heading.Found && first.Confirm.Found;
        bool visualCandidate = !exactOcr &&
            AbyssMissionCompletePolicy.HasMissionTextVisualEvidence(initialFrame);
        if (!exactOcr && !visualCandidate)
        {
            Log?.Invoke("[병행] 협동 미션 완료 화면 후보 · 정확 OCR 및 독립 텍스트 영역 영상 증거 부족 -> 입력 보류");
            return false;
        }

        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using var fresh = await CaptureGameWindowAsync(ct);
        if (!AbyssMissionCompletePolicy.HasVisualEvidence(fresh))
            return false;

        if (exactOcr)
        {
            var second = await EvidenceAsync(fresh);
            if (first.Gift != second.Gift ||
                !AbyssMissionCompletePolicy.IsStableTextEvidence(
                    first.Heading.Bounds, second.Heading.Bounds,
                    first.Confirm.Bounds, second.Confirm.Bounds,
                    initialFrame.Size, fresh.Size, first.Gift))
                return false;
        }
        else if (!AbyssMissionCompletePolicy.HasStableMissionVisualFallback(
                     initialFrame, fresh))
        {
            // Two independent frames must retain title, gift heading, confirm
            // glyphs, crest and continuous green bar. Never click a result
            // screen or one-frame false positive on OCR failure.
            return false;
        }

        // Never use OCR text bounds as a click target. Only fixed center of
        // one verified full-width bar; re-check live client geometry/F10.
        var click = AbyssMissionCompletePolicy.ConfirmCenter(fresh.Size);
        ct.ThrowIfCancellationRequested();
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        VerifyAbyssClickClientGeometry(_hwnd, fresh.Size);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke(exactOcr
            ? "[병행] 협동 미션 완료/미션 선물 + 확인 OCR 2프레임 일치 · 확인 1회 @ " + click
            : "[병행] 실제 협동 미션 완료 OCR 누락 · 독립 제목/선물/확인 영상 + 초록 버튼 2프레임 일치 · 확인 1회 @ " + click);
        DiagnosticObserveClick("abyss_mission_complete_confirm", click);
        _input.ClickClientPoint(_hwnd, click);
        _scheduledMissionConfirmationClicked = true;
        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
        return true;
    }
}
