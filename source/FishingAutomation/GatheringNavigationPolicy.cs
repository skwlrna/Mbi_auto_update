using System.Drawing;

namespace FishingAutomation;

internal static class GatheringNavigationPolicy
{
    internal static bool IsStableFirstRow(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
            return false;

        var overlap = Rectangle.Intersect(first, second);
        if (overlap.Width <= 0 || overlap.Height <= 0) return false;

        long firstArea = (long)first.Width * first.Height;
        long secondArea = (long)second.Width * second.Height;
        long overlapArea = (long)overlap.Width * overlap.Height;
        double overlapRatio = overlapArea / (double)Math.Min(firstArea, secondArea);

        int firstCenterX = first.Left + first.Width / 2;
        int firstCenterY = first.Top + first.Height / 2;
        int secondCenterX = second.Left + second.Width / 2;
        int secondCenterY = second.Top + second.Height / 2;

        return overlapRatio >= 0.82 &&
               Math.Abs(firstCenterX - secondCenterX) <= 18 &&
               Math.Abs(firstCenterY - secondCenterY) <= 14;
    }

    internal static bool IsSameLifeSkillRow(Rectangle expected, Rectangle fresh)
    {
        if (expected.Width <= 0 || expected.Height <= 0 || fresh.Width <= 0 || fresh.Height <= 0)
            return false;

        // A fresh OCR box can shift or resize enough to stop overlapping even when
        // the exact same label remains on the same list row. The neighboring rows
        // are much farther apart, so use bounded vertical row identity here.
        int expectedCenterY = expected.Top + expected.Height / 2;
        int freshCenterY = fresh.Top + fresh.Height / 2;
        return Math.Abs(expectedCenterY - freshCenterY) <= 24;
    }
}
