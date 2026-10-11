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
