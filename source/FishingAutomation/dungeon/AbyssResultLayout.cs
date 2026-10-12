using System.Drawing;

namespace DungeonVisionBot;

internal static class AbyssResultLayout
{
    // Only for the scheduler's *left* "나가기" OCR. Keep the ordinary Abyss
    // middle-button retry and all existing 3-green-button visual ROIs untouched.
    //
    // In the user's portrait result capture, the left label begins around
    // normalized x=120 on an 800-wide canvas; the former x=160 OCR crop could
    // remove "나" and never verify a safe exit. The expanded region stays
    // strictly left of the middle retry button's canonical x=310.
    internal static readonly Rectangle ScheduledExitLabelRoi = new(35, 900, 260, 92);

    internal static bool HasStableScheduledExitLabel(
        Rectangle firstText, Rectangle secondText, Rectangle firstRoi, Rectangle secondRoi)
    {
        if (firstText.Width <= 0 || firstText.Height <= 0 ||
            secondText.Width <= 0 || secondText.Height <= 0 ||
            firstRoi.Width <= 0 || firstRoi.Height <= 0 ||
            secondRoi.Width <= 0 || secondRoi.Height <= 0)
            return false;

        // Two separately captured frames must contain the entire same-word
        // rectangle in their respective *left button* crops. Using the second
        // crop to validate the old frame was a tautology, not a stability test.
        return firstRoi.Contains(firstText) &&
               secondRoi.Contains(secondText) &&
               firstText.IntersectsWith(secondText);
    }

    // Canonical 800x1000 *game-client* button centers. These are candidate
    // centers derived from the provided portrait result image, not from any OCR
    // bounding box. Keep them separate from the broader three-button visual
    // verification ROIs, which have deliberately not changed.
    internal static readonly Point ExitFixedCenter800 = new(165, 930);
    internal static readonly Point RetryFixedCenter800 = new(400, 930);

    internal static Point FixedButtonCenter(bool exit, Size capturedClientSize)
    {
        if (capturedClientSize.Width <= 0 || capturedClientSize.Height <= 0)
            throw new InvalidOperationException("고정 버튼 클릭: 유효한 게임 클라이언트 크기가 아닙니다.");

        double aspect = capturedClientSize.Width / (double)capturedClientSize.Height;
        if (aspect < 0.74 || aspect > 0.92)
            throw new InvalidOperationException("고정 버튼 클릭: 검증 범위를 벗어난 게임 창 비율입니다.");

        Point canonical = exit ? ExitFixedCenter800 : RetryFixedCenter800;
        return new Point(
            (int)Math.Round(canonical.X * capturedClientSize.Width / 800.0),
            (int)Math.Round(canonical.Y * capturedClientSize.Height / 1000.0));
    }

    public static bool IsConfirmed(Rectangle exit, Rectangle retry, Rectangle other)
    {
        if (exit.Width <= 0 || exit.Height <= 0 || retry.Width <= 0 || retry.Height <= 0 || other.Width <= 0 || other.Height <= 0)
            return false;
        bool ordered = exit.Right < retry.Left && retry.Right < other.Left;
        int rowTop = Math.Max(exit.Top, Math.Max(retry.Top, other.Top));
        int rowBottom = Math.Min(exit.Bottom, Math.Min(retry.Bottom, other.Bottom));
        return ordered && rowTop < rowBottom;
    }
}


/// <summary>
/// The scheduler must not treat an ordinary RunAsync return, a network reset,
/// or a completed Smart Recovery as proof that it may touch processing.
/// Only an independently verified outside HUD can release that input lease.
/// </summary>
internal static class ScheduledAbyssReturnPolicy
{
    internal static void RequireVerifiedOutside(
        bool scheduledResultExit,
        bool outsideHudVerified,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (scheduledResultExit && !outsideHudVerified)
            throw new InvalidOperationException(
                "병행 어비스 종료/재접속/복구 후 던전 밖 HUD 확인 기록이 없습니다. " +
                "가공 입력권 반환 금지 · 현재 게임 화면 확인 후 이어하기가 필요합니다.");
    }
}
