namespace FishingAutomation;

internal sealed record LivingSkillGatheringSource(
    string Category,
    string TargetName,
    string TemplateKey,
    bool BulkSupported = true);

internal static class LivingSkillGatheringCatalog
{
    // Insect gathering is intentionally quest-only. These confirmed insect names
    // are never exposed to standalone/bulk navigation; crafting may still acquire
    // them through its quest -> 구하는 방법 -> 추천 route.
    private static readonly HashSet<string> QuestOnlyMaterials = new(StringComparer.Ordinal)
    {
        "유령 반딧불이",
        "석양 나비",
        "흰얼음풍뎅이",
        "낙엽나방",
        "황혼잠자리"
    };

    private static readonly Dictionary<string, LivingSkillGatheringSource> Exact =
        new(StringComparer.Ordinal)
        {
            // 일상 채집
            ["달걀"] = Source("일상 채집", "둥지"),
            ["거미줄"] = Source("일상 채집", "거미줄"),
            ["물이 든 병"] = Source("일상 채집", "우물"),
            ["우유"] = Source("일상 채집", "젖소"),
            ["사과"] = Source("일상 채집", "사과 나무"),
            ["찻잎"] = Source("일상 채집", "차나무"),
            ["고급 거미줄"] = Source("일상 채집", "거미줄 뭉치"),
            ["헤이즐넛"] = Source("일상 채집", "헤이즐넛"),
            ["특급 거미줄"] = Source("일상 채집", "얽힌 거미줄"),

            // 나무 베기
            ["나무 진액"] = Source("나무 베기", "뾰족 나무"),
            ["통나무"] = Source("나무 베기", "굵은 나무"),
            ["상급 통나무"] = Source("나무 베기", "쓸 만한 나무"),
            ["상급 통나무+"] = Source("나무 베기", "갑옷 나무"),
            ["최상급 통나무"] = Source("나무 베기", "어스름 나무"),
            ["최상급 통나무+"] = Source("나무 베기", "벼락 나무"),
            ["특급 통나무"] = Source("나무 베기", "흰 껍질 나무"),

            // 광석 캐기
            ["철 광석"] = Source("광석 캐기", "철 광맥"),
            ["얼음"] = Source("광석 캐기", "얼음"),
            ["석탄"] = Source("광석 캐기", "석탄 광맥"),
            ["동 광석"] = Source("광석 캐기", "동 광맥"),
            ["백동 광석"] = Source("광석 캐기", "백동 광맥"),
            ["은 광석"] = Source("광석 캐기", "은 광맥"),
            ["운철 광석"] = Source("광석 캐기", "운철 광맥"),
            ["백금 광석"] = Source("광석 캐기", "백금 광맥"),

            // 양털 깎기
            ["양털"] = Source("양털 깎기", "양"),
            ["상급 양털"] = Source("양털 깎기", "곱슬 양"),
            ["상급 양털+"] = Source("양털 깎기", "먹구름 양"),
            ["최상급 양털"] = Source("양털 깎기", "먹구름 양"),
            ["최상급 양털+"] = Source("양털 깎기", "구름털 양"),
            ["특급 양털"] = Source("양털 깎기", "복슬 양"),

            // 추수
            ["밀"] = Source("추수", "밀"),
            ["옥수수"] = Source("추수", "옥수수"),
            ["콩"] = Source("추수", "콩"),
            ["쌀"] = Source("추수", "쌀"),
            ["귀리"] = Source("추수", "귀리"),

            // 호미질
            ["감자"] = Source("호미질", "감자"),
            ["양파"] = Source("호미질", "양파"),
            ["조개"] = Source("호미질", "조개"),
            ["파스닙"] = Source("호미질", "파스닙"),
            ["양배추"] = Source("호미질", "양배추"),
            ["호박"] = Source("호미질", "호박"),
            ["개암 버섯"] = Source("호미질", "개암 버섯"),

            // 약초 채집의 확정 목록. 일반 약초는 이름 그대로 선택한다.
            ["허브"] = Source("약초 채집", "허브"),
            ["블러디 허브"] = Source("약초 채집", "블러디 허브"),
            ["화살꽃"] = Source("약초 채집", "화살꽃"),
            ["마나 허브"] = Source("약초 채집", "마나 허브"),
            ["새록 버섯"] = Source("약초 채집", "새록 버섯"),
            ["튼튼 버섯"] = Source("약초 채집", "튼튼 버섯"),
            ["끈기 풀"] = Source("약초 채집", "끈기 풀"),
            ["쑥쑥 버섯"] = Source("약초 채집", "쑥쑥 버섯"),
            ["숨숨꽃"] = Source("약초 채집", "숨숨꽃"),
            ["깔끔 버섯"] = Source("약초 채집", "깔끔 버섯"),
            ["생채기꽃"] = Source("약초 채집", "생채기꽃"),
            ["증폭 버섯"] = Source("약초 채집", "증폭 버섯"),
            ["진정초"] = Source("약초 채집", "진정초"),
            ["끈적 풀"] = Source("약초 채집", "끈적 풀"),
            ["솔솔 버섯"] = Source("약초 채집", "솔솔 버섯"),
            ["산뜻 버섯"] = Source("약초 채집", "산뜻 버섯"),
        };

    internal static bool TryResolveBulk(string materialName, out LivingSkillGatheringSource source)
    {
        materialName = (materialName ?? "").Trim();
        if (Exact.TryGetValue(materialName, out source!))
            return source.BulkSupported;

        const string sporeSuffix = " 포자";
        if (materialName.EndsWith(sporeSuffix, StringComparison.Ordinal))
        {
            string mushroom = materialName[..^sporeSuffix.Length].TrimEnd();
            if (mushroom.EndsWith("버섯", StringComparison.Ordinal))
            {
                // 개암 버섯은 호미질, 나머지 현재 확인된 포자 버섯은 약초 채집.
                string category = string.Equals(mushroom, "개암 버섯", StringComparison.Ordinal)
                    ? "호미질"
                    : "약초 채집";
                source = Source(category, mushroom);
                return true;
            }
        }

        source = null!;
        return false;
    }

    internal static bool IsQuestOnlyCategory(string category)
        => string.Equals(category, "곤충채집", StringComparison.Ordinal);

    internal static bool IsQuestOnlyMaterial(string materialName)
        => QuestOnlyMaterials.Contains((materialName ?? "").Trim());

    private static LivingSkillGatheringSource Source(string category, string target)
        => new(category, target, TemplateKey(category, target));

    private static string TemplateKey(string category, string target)
        => string.Concat(category, "-", target)
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace("+", "plus", StringComparison.Ordinal);
}
