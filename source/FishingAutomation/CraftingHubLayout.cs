using System.Drawing;

namespace FishingAutomation;

internal static class CraftingHubLayout
{
    // User-confirmed 800x1000 crafting hub:
    // row 1: 무기 / 방어구 / 장신구
    // row 2: 도구 / 아이템 / 음식
    // Only the large title band is accepted for OCR authorization so the
    // descriptive sentence below (for example "아이템을 만든다") cannot create
    // a second false match.
    internal static Rectangle CategoryTitleArea(CraftingCategory category)
        => category switch
        {
            CraftingCategory.Item => new Rectangle(320, 585, 180, 105),
            CraftingCategory.Food => new Rectangle(555, 585, 200, 105),
            _ => Rectangle.Empty
        };

    internal static Rectangle CategoryCardArea(CraftingCategory category)
        => category switch
        {
            CraftingCategory.Item => new Rectangle(282, 500, 236, 305),
            CraftingCategory.Food => new Rectangle(524, 500, 236, 305),
            _ => Rectangle.Empty
        };

    internal static bool IsStableTitle(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
            return false;

        var a = new Point(first.Left + first.Width / 2, first.Top + first.Height / 2);
        var b = new Point(second.Left + second.Width / 2, second.Top + second.Height / 2);
        return Math.Abs(a.X - b.X) <= 14 &&
               Math.Abs(a.Y - b.Y) <= 14 &&
               Math.Abs(first.Width - second.Width) <= 18 &&
               Math.Abs(first.Height - second.Height) <= 14;
    }
}
