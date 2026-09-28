using System.Drawing;
namespace DungeonVisionBot;
internal static class AbyssResultLayout
{
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
