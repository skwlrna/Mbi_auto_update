using System.Drawing;

namespace FishingAutomation;

internal static class LifeSkillListLayout
{
    internal static readonly Point SafeDragStart = new(580, 760);
    internal static readonly Point SafeDragEnd = new(580, 350);

    // Actual 2026-10-21 failure: 일상 채집 showed 물 instead of the
    // user-confirmed first row 둥지. A downward touch gesture moves the list
    // BACK towards its beginning; normal searching swipes the other way.
    // Only this confirmed target may use the guarded top reset.
    internal const int MaxNestTopResetSwipes = 4;
    internal static bool NeedsNestTopReset(string category, string targetName)
        => category == "일상 채집" && targetName == "둥지";

    // Shared 800x1000 one-page life-skill list card centers.
    // Harvest screenshot confirms rows 1-5. Wool uses the same card geometry and
    // the user confirmed all six wool rows fit on one page.
    private static readonly int[] OnePageRowY = { 488, 576, 665, 753, 842, 930 };

    internal static bool TryFixedRowPoint(
        string category,
        string targetName,
        out Point point)
    {
        int index = category switch
        {
            "추수" => targetName switch
            {
                "밀" => 0,
                "옥수수" => 1,
                "콩" => 2,
                "쌀" => 3,
                "귀리" => 4,
                _ => -1
            },
            "양털 깎기" => targetName switch
            {
                "양털" => 0,
                "상급 양털" => 1,
                "상급 양털+" => 2,
                "최상급 양털" => 3,
                "최상급 양털+" => 4,
                "특급 양털" => 5,
                _ => -1
            },
            _ => -1
        };

        if (index < 0)
        {
            point = Point.Empty;
            return false;
        }

        point = new Point(400, OnePageRowY[index]);
        return true;
    }

    internal static bool NeverScroll(string category)
        => category is "추수" or "양털 깎기";
}
