namespace FishingAutomation;

internal static class AcquisitionMethodPolicy
{
    // Only positively identified life-skill rows may start free gathering.
    internal static bool IsLifeSkill(string? text)
    {
        string compact = (text ?? "").Replace(" ", "");
        string[] blocked = { "던전", "전리품", "레이드", "임무", "구매", "상점", "날개" };
        if (blocked.Any(x => compact.Contains(x, StringComparison.Ordinal))) return false;
        string[] skills = { "생활스킬", "채집", "일상채집", "나무베기", "벌목", "광석캐기", "채광", "약초채집", "양털깎기", "추수", "호미질", "곤충채집", "낚시" };
        return skills.Any(x => compact.Contains(x, StringComparison.Ordinal));
    }

    internal static bool IsRecommended(string? text)
        => (text ?? "").Contains("추천", StringComparison.Ordinal);
}
