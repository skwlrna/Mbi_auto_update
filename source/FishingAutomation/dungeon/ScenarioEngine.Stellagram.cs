using System.Diagnostics;
using System.Drawing;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // One ESC per verified result exit, never one per polling iteration.
    private bool _postExitStellagramEscAttempted;
    private static readonly Rectangle StellagramTopTitleRoi = new(35, 34, 280, 72);
    private static readonly Rectangle StellagramBestTitleRoi = new(275, 150, 265, 85);

    // Two separate 800x1000 text regions observed in the 2026-11-12 incident.
    private async Task<bool> IsPostExitStellagramAsync(Bitmap frame, CancellationToken ct)
    {
        _abyssResultOcr ??= new OcrRecognizer();
        var topRoi = ScaleAbyssResultRoi(StellagramTopTitleRoi, frame.Size);
        var bestRoi = ScaleAbyssResultRoi(StellagramBestTitleRoi, frame.Size);
        if (topRoi.IsEmpty || bestRoi.IsEmpty) return false;

        var top = await _abyssResultOcr.FindCompactLabelAsync(
            frame, topRoi, "오늘의 스텔라 픽", ct);
        if (!top.Found || !topRoi.Contains(top.Bounds)) return false;
        var best = await _abyssResultOcr.FindCompactLabelAsync(
            frame, bestRoi, "베스트 스텔라그램", ct);
        return best.Found && bestRoi.Contains(best.Bounds);
    }

    // Only after a verified Abyss result-exit. Full-screen but unrelated panels
    // must never receive ESC from this handler.
    private async Task<bool> TryDismissPostExitStellagramAsync(
        Bitmap initialFrame, CancellationToken ct)
    {
        if (!IsAbyss || (_scheduledResultExit && !_scheduledExitClicked))
            return false;
        if (!await IsPostExitStellagramAsync(initialFrame, ct))
            return false;

        if (_postExitStellagramEscAttempted)
            throw new InvalidOperationException(
                "스텔라그램 ESC 이후에도 동일한 팝업이 표시되어 재입력 없이 안전 정지합니다.");

        await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        using (var fresh = await CaptureGameWindowAsync(ct))
            if (!await IsPostExitStellagramAsync(fresh, ct))
                return false; // One ambiguous frame is never ESC authorization.

        ct.ThrowIfCancellationRequested();
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke("[어비스 퇴장 후] 오늘의 스텔라 픽 + 베스트 스텔라그램 2프레임 확인 -> ESC 1회");
        _postExitStellagramEscAttempted = true;
        _input.TapScanCode(0x01, ct);

        var wait = Stopwatch.StartNew();
        int absentFrames = 0;
        while (wait.Elapsed < TimeSpan.FromSeconds(8))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(350, ct);
            using var after = await CaptureGameWindowAsync(ct);
            if (await IsPostExitStellagramAsync(after, ct))
            {
                absentFrames = 0;
                continue;
            }
            if (++absentFrames >= 2)
            {
                Log?.Invoke("[어비스 퇴장 후] 스텔라그램 ESC 이후 팝업 미감지 2프레임 -> 필드 HUD 확인 재개");
                return true;
            }
        }
        throw new InvalidOperationException(
            "스텔라그램 ESC 1회 후 8초 내 팝업 닫힘을 확인하지 못했습니다. 추가 클릭 없이 안전 정지합니다.");
    }
}
