using System.Text.RegularExpressions;

namespace FishingAutomation;

internal static class CraftingQuestText
{
    internal static string Compact(string text) => Regex.Replace(text ?? "", @"[\s•◆◇·]", "");
    internal static bool IsTitle(string text, string displayName)
    {
        string compact = Compact(text);
        string name = Compact(displayName);
        if (string.IsNullOrWhiteSpace(name)) return false;
        return compact.Equals(name + "제작", StringComparison.Ordinal) ||
               (compact.Contains(name, StringComparison.Ordinal) &&
                compact.Contains("제작", StringComparison.Ordinal));
    }

    internal static bool IsTitleNameOnly(string text, string displayName)
    {
        string compact = Compact(text);
        string name = Compact(displayName);
        return !string.IsNullOrWhiteSpace(name) &&
               compact.Contains(name, StringComparison.Ordinal);
    }

    internal static bool IsQuestProgress(string text)
    {
        string compact = Compact(text);
        return Regex.IsMatch(compact, @"^.+준비\d+/\d+$") ||
               Regex.IsMatch(compact, @"^.+제작대에서제작\d+/\d+$");
    }
    internal static bool IsDirectStage(string text)
        => Compact(text).Equals("바로제작진행", StringComparison.Ordinal);
    internal static bool IsStationStage(string text)
    {
        string compact = Compact(text);
        return Regex.IsMatch(compact, @"^.+제작대에서제작(?:\d+/\d+)?$");
    }
}
