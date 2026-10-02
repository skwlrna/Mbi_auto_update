namespace FishingAutomation;

internal static class AcquisitionMethodPolicy
{
    // Only positively identified life-skill rows may start free gathering.
    internal static bool IsLifeSkill(string? text)
    {
        string compact = (text ?? "").Replace(" ", "");
        string[] blocked = { "던전", "전리품", "레이드", "임무", "구매", "상점", "날개" };
        if (blocked.Any(x => compact.Contains(x, StringComparison.Ordinal))) return false;
        string[] skills = { "생활스킬", "채집", "벌목", "채광", "낚시" };
        return skills.Any(x => compact.Contains(x, StringComparison.Ordinal));
    }

    internal static bool IsRecommended(string? text)
        => (text ?? "").Contains("추천", StringComparison.Ordinal);
}
