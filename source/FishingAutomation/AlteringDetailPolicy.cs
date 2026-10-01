namespace FishingAutomation;

internal static class AlteringDetailPolicy
{
    internal static bool IsConfirmed(bool titleMatched, bool materialsVisible, bool freeActionVisible, bool paidActionVisible)
        => titleMatched || (materialsVisible && (freeActionVisible || paidActionVisible));
}
