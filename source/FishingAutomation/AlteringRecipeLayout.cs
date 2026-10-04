using System.Drawing;

namespace FishingAutomation;

internal static class AlteringRecipeLayout
{
    internal static readonly Point ProcessingSearchIconPoint = new(39, 117);
    internal static readonly Rectangle ProcessingSearchDialogArea = new(140, 760, 520, 235);
    internal static readonly Point ProcessingSearchInputPoint = new(400, 862);
    internal static readonly Rectangle ProcessingSearchResultArea = new(35, 350, 730, 550);
    internal static readonly Point ProcessingSearchFirstResultPoint = new(219, 412);
    internal const double ProcessingSearchOpenChangeRatio = 0.08;
    internal const double ProcessingSearchResultChangeRatio = 0.06;

    private static readonly HashSet<string> FixedFacilities = new(StringComparer.Ordinal)
    {
        "식재료 가공 시설",
        "금속 가공 시설",
        "목재 가공 시설",
        "가죽 가공 시설",
        "옷감 가공 시설"
    };

    private static readonly IReadOnlyDictionary<(string Facility, string Name, int Ordinal), Point> FixedCenters =
        new Dictionary<(string Facility, string Name, int Ordinal), Point>
        {
            // 식재료 · 사용자 현장 스크린샷 기준 4열 고정.
            [("식재료 가공 시설", "마요네즈", 1)] = new(219, 382),
            [("식재료 가공 시설", "밀가루", 1)] = new(340, 382),
            [("식재료 가공 시설", "치즈", 1)] = new(461, 382),
            [("식재료 가공 시설", "면", 1)] = new(582, 382),
            [("식재료 가공 시설", "생크림", 1)] = new(219, 553),
            [("식재료 가공 시설", "물에 불린 콩", 1)] = new(340, 553),
            [("식재료 가공 시설", "두부", 1)] = new(461, 553),
            [("식재료 가공 시설", "두유", 1)] = new(582, 553),
            [("식재료 가공 시설", "숙성된 커다란 고기", 1)] = new(219, 724),
            [("식재료 가공 시설", "물에 불린 쌀", 1)] = new(340, 724),
            [("식재료 가공 시설", "밥", 1)] = new(461, 724),
            [("식재료 가공 시설", "말린 찻잎", 1)] = new(582, 724),
            [("식재료 가공 시설", "발효된 찻잎", 1)] = new(219, 896),
            [("식재료 가공 시설", "헤이즐넛 오일", 1)] = new(340, 896),
            [("식재료 가공 시설", "오트밀", 1)] = new(461, 896),

            // 금속 · 은합금괴는 화면에 동일 이름 제법이 2개.
            [("금속 가공 시설", "철괴(광석)", 1)] = new(219, 412),
            [("금속 가공 시설", "철괴(철 광석)", 1)] = new(340, 412),
            [("금속 가공 시설", "강철괴", 1)] = new(461, 412),
            [("금속 가공 시설", "합금강괴", 1)] = new(582, 412),
            [("금속 가공 시설", "타르", 1)] = new(219, 584),
            [("금속 가공 시설", "특수강괴", 1)] = new(340, 584),
            [("금속 가공 시설", "은합금괴", 1)] = new(461, 584),
            [("금속 가공 시설", "윤철괴", 1)] = new(582, 584),
            [("금속 가공 시설", "은합금괴", 2)] = new(219, 756),
            [("금속 가공 시설", "백금강괴", 1)] = new(340, 756),

            // 목재 · 최상급 목재는 동일 이름 제법이 2개.
            [("목재 가공 시설", "목재", 1)] = new(219, 412),
            [("목재 가공 시설", "목재+", 1)] = new(340, 412),
            [("목재 가공 시설", "상급 목재", 1)] = new(461, 412),
            [("목재 가공 시설", "상급 목재+", 1)] = new(582, 412),
            [("목재 가공 시설", "부드러운 목재", 1)] = new(219, 584),
            [("목재 가공 시설", "단단한 목재", 1)] = new(340, 584),
            [("목재 가공 시설", "최상급 목재", 1)] = new(461, 584),
            [("목재 가공 시설", "최상급 목재+", 1)] = new(582, 584),
            [("목재 가공 시설", "구름결 막대", 1)] = new(219, 756),
            [("목재 가공 시설", "최상급 목재", 2)] = new(340, 756),
            [("목재 가공 시설", "특급 목재", 1)] = new(461, 756),

            // 가죽 · 최상급 가죽은 동일 이름 제법이 2개.
            [("가죽 가공 시설", "가죽", 1)] = new(219, 412),
            [("가죽 가공 시설", "가죽+", 1)] = new(340, 412),
            [("가죽 가공 시설", "상급 가죽", 1)] = new(461, 412),
            [("가죽 가공 시설", "상급 가죽+", 1)] = new(582, 412),
            [("가죽 가공 시설", "최상급 가죽", 1)] = new(219, 584),
            [("가죽 가공 시설", "최상급 가죽+", 1)] = new(340, 584),
            [("가죽 가공 시설", "최상급 가죽", 2)] = new(461, 584),
            [("가죽 가공 시설", "특급 가죽", 1)] = new(582, 584),

            // 옷감 · 최상급 옷감은 동일 이름 제법이 2개.
            [("옷감 가공 시설", "옷감", 1)] = new(219, 412),
            [("옷감 가공 시설", "실크", 1)] = new(340, 412),
            [("옷감 가공 시설", "옷감+", 1)] = new(461, 412),
            [("옷감 가공 시설", "상급 옷감", 1)] = new(582, 412),
            [("옷감 가공 시설", "두꺼운 옷감", 1)] = new(219, 584),
            [("옷감 가공 시설", "상급 실크", 1)] = new(340, 584),
            [("옷감 가공 시설", "상급 옷감+", 1)] = new(461, 584),
            [("옷감 가공 시설", "식물 섬유", 1)] = new(582, 584),
            [("옷감 가공 시설", "밧줄", 1)] = new(219, 756),
            [("옷감 가공 시설", "최상급 옷감", 1)] = new(340, 756),
            [("옷감 가공 시설", "최상급 실크", 1)] = new(461, 756),
            [("옷감 가공 시설", "튼튼한 밧줄", 1)] = new(582, 756),
            [("옷감 가공 시설", "최상급 옷감+", 1)] = new(219, 928),
            [("옷감 가공 시설", "최상급 옷감", 2)] = new(340, 928),
            [("옷감 가공 시설", "특급 옷감", 1)] = new(461, 928),
            [("옷감 가공 시설", "특급 실크", 1)] = new(582, 928),
        };

    internal static bool IsFixedFacility(string facilityName)
        => FixedFacilities.Contains(facilityName);

    internal static bool TryGetFixedCenter(AlteringPlan plan, out Point center)
    {
        if (FixedCenters.TryGetValue(
                (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal),
                out center))
            return true;

        // Qualified CLI recipes can show only the base output on screen.
        // Only collapse the name when there is exactly one recipe so a duplicate
        // ordinal is never guessed.
        return plan.RecipeCount == 1 &&
               !string.Equals(plan.OutputName, plan.DisplayName, StringComparison.Ordinal) &&
               FixedCenters.TryGetValue(
                   (plan.FacilityName, plan.OutputName, 1),
                   out center);
    }

    internal static bool IsSafeFixedCenter(Point center)
        => center.X is >= 150 and <= 650 &&
           center.Y is >= 330 and <= 950;

    internal static bool IsSafeMedicineSearchGeometry()
        => ProcessingSearchIconPoint.X is >= 10 and <= 90 &&
           ProcessingSearchIconPoint.Y is >= 85 and <= 150 &&
           ProcessingSearchDialogArea.Contains(ProcessingSearchInputPoint) &&
           ProcessingSearchResultArea.Contains(ProcessingSearchFirstResultPoint);
}
