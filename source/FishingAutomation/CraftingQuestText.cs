using System.Text.RegularExpressions;

namespace FishingAutomation;

internal static class CraftingQuestText
{
    internal static string Compact(string text) => Regex.Replace(text ?? "", @"[\s•◆◇·]", "");
    // Live 800x1000 material-shortage popup remains visible while auto gathering.
    // The header is around y=680, outside the older quest-title ROI.
    internal static bool IsShortageDialogCaption(string text)
    {
        string caption = Compact(text);
        return caption.Contains("퀘스트를위해필요한아이템", StringComparison.Ordinal) &&
               caption.Contains("구하는방법", StringComparison.Ordinal);
    }

    internal static bool IsTitle(string text, string displayName)
    {
        string compact = Compact(text);
        string name = Compact(displayName);
        if (string.IsNullOrWhiteSpace(name)) return false;
        return compact.Equals(name + "제작", StringComparison.Ordinal) ||
               (compact.StartsWith(name, StringComparison.Ordinal) &&
                compact.EndsWith("제작", StringComparison.Ordinal));
    }

    internal static bool IsTitleNameOnly(string text, string displayName)
    {
        string compact = Compact(text);
        string name = Compact(displayName);
        return !string.IsNullOrWhiteSpace(name) &&
               compact.Equals(name, StringComparison.Ordinal);
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
