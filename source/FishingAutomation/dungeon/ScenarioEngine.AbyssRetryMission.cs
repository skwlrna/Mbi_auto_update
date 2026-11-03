using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // The 2026-11-02 14:10 retry incident shows a full-width green "확인"
    // below "경쟁 미션 완료", not the usual three result buttons.
    // This is a post-retry interstitial: it must not be mistaken for combat,
    // re-clicked as retry, or handled by the scheduled-exit-only mission path.
    private async Task<bool> TryAcknowledgeRetryCompetitionMissionAsync(
        Bitmap initialFrame, CancellationToken ct)
    {
        if (!IsAbyss || !AbyssMissionCompletePolicy.HasVisualEvidence(initialFrame))
            return false;

        _abyssResultOcr ??= new OcrRecognizer();
        async Task<(DetectionResult Title, DetectionResult Confirm)> EvidenceAsync(Bitmap frame)
        {
            var title = await _abyssResultOcr.FindCompactLabelAsync(
                frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.TitleRoi, frame.Size),
                "경쟁 미션 완료", ct);
            var confirm = title.Found
                ? await _abyssResultOcr.FindCompactLabelAsync(
                    frame, ScaleAbyssResultRoi(AbyssMissionCompletePolicy.ConfirmLabelRoi, frame.Size),
                    "확인", ct)
                : DetectionResult.NotFound;
            return (title, confirm);
        }

        var first = await EvidenceAsync(initialFrame);
        bool exactOcr = first.Title.Found && first.Confirm.Found;
        // Windows OCR can miss the glow in the real title. Require the
        // independent crest/title/gift/confirm structure instead of relaxing
        // to any green button or an unrecognized dialogue.
        if (!exactOcr &&
            !AbyssMissionCompletePolicy.HasMissionTextVisualEvidence(initialFrame))
            return false;

        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using var fresh = await CaptureGameWindowAsync(ct);
        if (!AbyssMissionCompletePolicy.HasVisualEvidence(fresh))
            return false;
        if (exactOcr)
        {
            var second = await EvidenceAsync(fresh);
            if (!AbyssMissionCompletePolicy.IsStableTextEvidence(
                    first.Title.Bounds, second.Title.Bounds,
                    first.Confirm.Bounds, second.Confirm.Bounds,
                    initialFrame.Size, fresh.Size, giftHeading: false))
                return false;
        }
        else if (!AbyssMissionCompletePolicy.HasStableMissionVisualFallback(
                     initialFrame, fresh))
            return false;

        ct.ThrowIfCancellationRequested();
        var click = AbyssMissionCompletePolicy.ConfirmCenter(fresh.Size);
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        VerifyAbyssClickClientGeometry(_hwnd, fresh.Size);
        NativeMethods.SetForegroundWindow(_hwnd);
        DiagnosticObserveClick("abyss_retry_competition_confirm", click);
        _input.ClickClientPoint(_hwnd, click);
        Log?.Invoke("[어비스] 다시 하기 후 경쟁 미션 완료 · 2프레임 검증 · 확인 1회 클릭");
        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
        return true;
    }
}
