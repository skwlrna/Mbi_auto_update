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
    private bool _retryCompetitionConfirmationClicked;

    // 2026-11-02 14:10 actual retry incident: after pressing 다시 하기 the
    // game can show "경쟁 미션 완료" with ONE full-width green 확인 button.
    // This is NOT the three-button result and is not recruitment/loading.
    private async Task<bool> TryAcknowledgeRetryCompetitionMissionCompleteAsync(
        Bitmap initialFrame, CancellationToken ct)
    {
        var flow = GetAbyssFlowState();
        if (!IsAbyss || !_inventoryNextRoundClicked ||
            _retryCompetitionConfirmationClicked ||
            flow is not (AbyssFlowState.Reentering or AbyssFlowState.Recruiting) ||
            !AbyssMissionCompletePolicy.HasVisualEvidence(initialFrame))
            return false;

        _abyssResultOcr ??= new OcrRecognizer();
        async Task<(DetectionResult Heading, DetectionResult Confirm)> EvidenceAsync(Bitmap frame)
        {
            var heading = await _abyssResultOcr.FindCompactLabelAsync(
                frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.TitleRoi, frame.Size),
                "경쟁 미션 완료", ct);
            var confirm = heading.Found
                ? await _abyssResultOcr.FindCompactLabelAsync(
                    frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.ConfirmLabelRoi, frame.Size),
                    "확인", ct)
                : DetectionResult.NotFound;
            return (heading, confirm);
        }

        var first = await EvidenceAsync(initialFrame);
        bool exactOcr = first.Heading.Found && first.Confirm.Found;
        if (!exactOcr &&
            !AbyssMissionCompletePolicy.HasMissionTextVisualEvidence(initialFrame))
        {
            Log?.Invoke("[어비스] 경쟁 미션 완료 후보 · 정확 OCR/독립 시각 증거 부족 · 입력 없음");
            return false;
        }

        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using var fresh = await CaptureGameWindowAsync(ct);
        if (!AbyssMissionCompletePolicy.HasVisualEvidence(fresh))
            return false;
        if (exactOcr)
        {
            var second = await EvidenceAsync(fresh);
            if (!second.Heading.Found || !second.Confirm.Found ||
                !AbyssMissionCompletePolicy.IsStableTextEvidence(
                    first.Heading.Bounds, second.Heading.Bounds,
                    first.Confirm.Bounds, second.Confirm.Bounds,
                    initialFrame.Size, fresh.Size, giftHeading: false))
                return false;
        }
        else if (!AbyssMissionCompletePolicy.HasStableMissionVisualFallback(initialFrame, fresh))
            return false;

        var click = AbyssMissionCompletePolicy.ConfirmCenter(fresh.Size);
        ct.ThrowIfCancellationRequested();
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        VerifyAbyssClickClientGeometry(_hwnd, fresh.Size);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke(exactOcr
            ? "[어비스] 경쟁 미션 완료+확인 정확 OCR 2프레임 · 확인 1회 @ " + click
            : "[어비스] 경쟁 미션 완료 OCR 누락 · 기존 미션 제목/선물/확인/문장 영상 2프레임 · 확인 1회 @ " + click);
        DiagnosticObserveClick("abyss_retry_competition_confirm", click);
        _input.ClickClientPoint(_hwnd, click);
        _retryCompetitionConfirmationClicked = true;
        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);

        // No duplicate clicks even if the panel lingers. Verify it leaves
        // before considering a recruitment state or another game input.
        for (int pass = 0; pass < 5; pass++)
        {
            ct.ThrowIfCancellationRequested();
            using var after = await CaptureGameWindowAsync(ct);
            if (!AbyssMissionCompletePolicy.HasVisualEvidence(after))
            {
                Log?.Invoke("[어비스] 경쟁 미션 완료 확인 후 화면 이탈 · 모집/입장 상태 다시 관찰");
                return true;
            }
            await Task.Delay(350, ct);
        }
        throw new InvalidOperationException(
            "경쟁 미션 완료 확인 버튼을 한 번 눌렀으나 화면 이탈을 확인하지 못했습니다. 중복 클릭 없이 정지합니다.");
    }

}
