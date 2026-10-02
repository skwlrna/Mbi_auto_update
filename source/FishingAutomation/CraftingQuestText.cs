using System.Text.RegularExpressions;

namespace FishingAutomation;

internal static class CraftingQuestText
{
    private static string Compact(string text) => Regex.Replace(text, @"[\s•◆◇·]", "");
    internal static bool IsTitle(string text, string displayName)
        => Compact(text).Equals(Compact(displayName) + "제작", StringComparison.Ordinal);
    internal static bool IsDirectStage(string text)
        => Compact(text).Equals("바로제작진행", StringComparison.Ordinal);
    internal static bool IsStationStage(string text)
    {
        string compact = Compact(text);
        return compact.Contains("제작대", StringComparison.Ordinal) &&
            Regex.IsMatch(compact, @"제작\d+/\d+");
    }
}
