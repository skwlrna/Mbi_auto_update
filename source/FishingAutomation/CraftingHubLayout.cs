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
        => new(10, 75, 245, 120);

    internal static Rectangle ProductSearchDialogArea
        => new(65, 350, 675, 360);

    internal static Rectangle ProductSearchResultArea
        => new(35, 390, 730, 535);

    internal static Point ProductSearchIconPoint
        => new(30, 118);

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

    internal const double SearchDialogOpenMinChange = 0.10;

    internal static double SearchDialogChangeRatio(Bitmap before, Bitmap after)
    {
        Rectangle bounds = Rectangle.Intersect(
            ProductSearchDialogArea,
            new Rectangle(Point.Empty, before.Size));
        bounds = Rectangle.Intersect(
            bounds,
            new Rectangle(Point.Empty, after.Size));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return 0d;

        const int step = 6;
        const int changedThreshold = 72;
        long sampled = 0;
        long changed = 0;

        for (int y = bounds.Top; y < bounds.Bottom; y += step)
        {
            for (int x = bounds.Left; x < bounds.Right; x += step)
            {
                Color a = before.GetPixel(x, y);
                Color b = after.GetPixel(x, y);
                int delta =
                    Math.Abs(a.R - b.R) +
                    Math.Abs(a.G - b.G) +
                    Math.Abs(a.B - b.B);
                sampled++;
                if (delta >= changedThreshold)
                    changed++;
            }
        }

        return sampled == 0 ? 0d : changed / (double)sampled;
    }

    internal static bool SearchDialogChanged(Bitmap before, Bitmap after)
        => SearchDialogChangeRatio(before, after) >= SearchDialogOpenMinChange;

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
