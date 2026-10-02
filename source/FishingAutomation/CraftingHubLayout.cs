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
        => new(140, 760, 520, 235);

    internal static Rectangle ProductSearchResultArea
        => new(35, 390, 730, 535);

    internal static Point ProductSearchIconPoint
        => new(30, 118);

    internal static Point ProductSearchInputPoint
        => new(400, 862);

    internal static Point ProductFirstResultPoint
        => new(218, 613);

    internal const double SearchDialogOpenChangeRatio = 0.08;
    internal const double SearchResultChangeRatio = 0.06;
    internal const int ProductListSettleDelayMs = 1200;

    // Live 800x1000 crafting detail sheet controls.
    internal static Rectangle ProductDetailPanelArea
        => new(145, 465, 510, 530);

    internal static Point CraftCountMinusPoint
        => new(275, 835);

    internal static Point CraftCountCenterPoint
        => new(400, 835);

    internal static Point CraftCountPlusPoint
        => new(525, 835);

    internal static Point CraftQuestButtonPoint
        => new(307, 950);

    internal static Point CraftGoButtonPoint
        => new(507, 950);

    internal static Rectangle CraftActionButtonArea
        => new(385, 910, 245, 80);

    // Craft result popup uses a centered modal. Keep these intentionally broad:
    // completion text can shift slightly with product/result layout.
    internal static Rectangle CraftCompletionHeaderArea
        => new(80, 35, 640, 310);

    internal static Rectangle CraftCompletionConfirmArea
        => new(110, 650, 580, 345);

    internal const int CraftDetailSettleDelayMs = 100;
    internal const int CraftReadyPollDelayMs = 150;

    // Live 800x1000 right-side quest list: newly created production quest is
    // pinned as the first entry directly below the 퀘스트 header.
    internal static Point ProductionQuestTopPoint
        => new(742, 310);

    internal static Rectangle ProductionQuestPopupArea
        => new(70, 130, 670, 520);

    // Live V3.0.10 missing-material popup:
    // rows are centered around y=710/777/843, not in the upper quest ROI.
    internal static Rectangle ProductionQuestMaterialArea
        => new(160, 640, 480, 270);

    // Live V3.0.11 acquisition popup after clicking a missing material.
    internal static Rectangle AcquisitionMethodHeaderArea
        => new(135, 505, 530, 115);

    internal static Rectangle AcquisitionMethodRecommendedRowArea
        => new(155, 610, 500, 95);

    internal static Rectangle AcquisitionMethodListArea
        => new(150, 600, 510, 190);

    internal static Point AcquisitionMethodRecommendedPoint
        => new(400, 650);

    internal const int AcquisitionMethodSettleDelayMs = 250;

    internal const int ProductionQuestListSettleDelayMs = 100;
    internal const int ProductionQuestOpenDelayMs = 300;

    internal static bool IsSafeCraftDetailGeometry()
        => ProductDetailPanelArea.Contains(CraftCountMinusPoint) &&
           ProductDetailPanelArea.Contains(CraftCountCenterPoint) &&
           ProductDetailPanelArea.Contains(CraftCountPlusPoint) &&
           ProductDetailPanelArea.Contains(CraftQuestButtonPoint) &&
           ProductDetailPanelArea.Contains(CraftGoButtonPoint) &&
           CraftActionButtonArea.Contains(CraftGoButtonPoint) &&
           ProductionQuestTopPoint.X is >= 500 and < 800 &&
           ProductionQuestTopPoint.Y is >= 140 and < 740 &&
           AcquisitionMethodHeaderArea.Contains(new Point(175, 560)) &&
           AcquisitionMethodRecommendedRowArea.Contains(AcquisitionMethodRecommendedPoint);


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
