using System.Drawing;

namespace FishingAutomation;

internal static class CraftingHubLayout
{
    // User-confirmed 800x1000 crafting hub:
    // row 1: 무기 / 방어구 / 장신구
    // row 2: 도구 / 아이템 / 음식
    //
    // Category OCR is helpful but not reliable enough to be the only gate: the
    // V2.0.4 live failure showed the correct hub with a clearly visible 아이템
    // title that Windows OCR still failed to return. Navigation clicks are
    // therefore authorized by a stable 제작 hub header first, then use exact
    // category OCR when available and a verified fixed card-center fallback when
    // it is not.
    internal static Rectangle HubHeaderArea
        => new(20, 22, 190, 82);

    // User-confirmed 800x1000 crafting list/search geometry.
    // These are navigation coordinates only. OCR is kept as a screen-state
    // verifier/fallback, not as the primary source of click coordinates.
    internal static Rectangle ProductListHeaderArea
        => new(15, 20, 300, 100);

    internal static Rectangle ProductFilterArea
        => new(35, 75, 220, 120);

    internal static Rectangle ProductSearchDialogArea
        => new(65, 350, 675, 360);

    internal static Rectangle ProductSearchResultArea
        => new(35, 390, 730, 535);

    internal static Point ProductSearchIconPoint
        => new(55, 135);

    internal static Point ProductSearchInputPoint
        => new(400, 425);

    internal static Point ProductFirstResultPoint
        => new(400, 460);

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

    internal static Point CategoryClickPoint(CraftingCategory category)
        => category switch
        {
            CraftingCategory.Item => new Point(400, 632),
            CraftingCategory.Food => new Point(640, 632),
            _ => Point.Empty
        };

    internal static bool IsSafeSearchGeometry()
        => ProductFilterArea.Contains(ProductSearchIconPoint) &&
           ProductSearchDialogArea.Contains(ProductSearchInputPoint) &&
           ProductSearchResultArea.Contains(ProductFirstResultPoint);

    internal static bool IsStableTitle(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
            return false;

        var a = Center(first);
        var b = Center(second);
        return Math.Abs(a.X - b.X) <= 14 &&
               Math.Abs(a.Y - b.Y) <= 14 &&
               Math.Abs(first.Width - second.Width) <= 18 &&
               Math.Abs(first.Height - second.Height) <= 14;
    }

    internal static bool IsSafeFallbackPoint(CraftingCategory category)
    {
        var card = CategoryCardArea(category);
        var point = CategoryClickPoint(category);
        return !card.IsEmpty && !point.IsEmpty && card.Contains(point);
    }

    private static Point Center(Rectangle rect)
        => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
}
