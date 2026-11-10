namespace FishingAutomation;

// Direct inputs per ONE craft, not expanded raw materials or output quantity.
// External reference data reviewed 2026-11-10; not a live game observation.
internal sealed record CraftingFoodRecipe(
    string DisplayName,
    IReadOnlyList<CraftingFoodIngredient> Ingredients,
    IReadOnlyList<string> Sources)
{
    internal IReadOnlyList<CraftingFoodIngredient> ForBatch(int crafts)
    {
        if (crafts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(crafts), "제작 횟수는 1~10회입니다.");
        return Array.AsReadOnly(Ingredients.Select(x =>
            new CraftingFoodIngredient(x.Name, checked(x.Amount * crafts))).ToArray());
    }
}

internal static class CraftingFoodRecipeCatalog
{
    internal const string ReviewedOn = "2026-11-10";
    private const string Wiki = "https://d.namu.moe/w/마비노기 모바일/생활 스킬";
    private static readonly IReadOnlyDictionary<string, CraftingFoodRecipe> Recipes = Build();

    // Conflicting published quantities are deliberately unavailable to callers.
    internal static IReadOnlyDictionary<string, string> Unresolved { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["매콤 파프리카 스튜"] = "밥 3/4개 출처 불일치 · 게임 상세 확인 필요",
                ["파이크퍼치 필레 리소토"] = "치즈 3/12개 출처 불일치 · 게임 상세 확인 필요"
            });

    internal static IEnumerable<CraftingFoodRecipe> All => Recipes.Values;

    internal static bool TryGet(string? displayName, out CraftingFoodRecipe recipe)
    {
        string key = displayName switch
        {
            "달걀 프라이" => "달걀프라이",
            "토마토 리조토" => "토마토 리소토",
            _ => displayName ?? ""
        };
        return Recipes.TryGetValue(key, out recipe!);
    }

    private static IReadOnlyDictionary<string, CraftingFoodRecipe> Build()
    {
        var rows = new Dictionary<string, CraftingFoodRecipe>(StringComparer.Ordinal);
        void Add(string name, string[] sources, params (string Name, long Amount)[] inputs)
            => rows.Add(name, new(name,
                Array.AsReadOnly(inputs.Select(x => new CraftingFoodIngredient(x.Name, x.Amount)).ToArray()),
                Array.AsReadOnly(sources)));

        Add("달걀프라이", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-달걀-프라이-356" },
            ("달걀", 1), ("소금", 1));
        Add("삶은 달걀", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-삶은-달걀-357" },
            ("달걀", 1), ("물이 든 병", 1));
        Add("옥수수차", new[] { Wiki },
            ("옥수수", 16), ("물이 든 병", 4));
        Add("녹차", new[] { Wiki },
            ("말린 찻잎", 8), ("물이 든 병", 4));
        Add("홍차", new[] { Wiki },
            ("발효된 찻잎", 8), ("물이 든 병", 4), ("옷감", 2));
        Add("바삭한 초코 카나페", new[] { Wiki },
            ("오트밀", 5), ("조각 초콜릿", 3), ("파프리카", 3), ("치즈", 3), ("달걀", 1));
        Add("마요네즈 고기볶음", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-마요네즈-고기볶음-359" },
            ("고기", 4), ("양배추", 1), ("마요네즈", 1));
        Add("포크 인 밀크", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-포크-인-밀크-360" },
            ("고기", 8), ("우유", 4), ("마늘", 2), ("허브", 2));
        Add("치즈 퐁뒤", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-치즈-퐁뒤-361" },
            ("고기", 8), ("치즈", 2), ("감자", 4), ("소금", 2));
        Add("조개찜", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-조개찜-362" },
            ("조개", 10), ("물이 든 병", 5), ("레몬", 4), ("허브", 3));
        Add("크림소스 스테이크", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-크림소스-스테이크-363", "https://mobile.mabi.zip/keurimsoseuseuteikeu" },
            ("고기", 10), ("생크림", 2), ("마늘", 2), ("후추", 3));
        Add("샤와르마", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-샤와르마-364" },
            ("담백한 고기", 10), ("콩", 12), ("밀가루", 3), ("양배추", 5), ("레몬", 2));
        Add("미트 파르미자나", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-미트-파르미자나-365" },
            ("담백한 고기", 10), ("치즈", 3), ("토마토", 1), ("후추", 1), ("소금", 1));
        Add("카레라이스", new[] { Wiki, "https://mobile.mabi.zip/kareraiseu" },
            ("고기", 10), ("밥", 3), ("당근", 3), ("감자", 8), ("카레 가루", 2));
        Add("미트 포트파이", new[] { Wiki, "https://mobile.mabi.zip/miteupoteupai" },
            ("담백한 고기", 10), ("완두콩", 2), ("파스닙", 6), ("밀가루", 4), ("우유", 4));
        Add("포레스트 스테이크", new[] { Wiki },
            ("커다란 고기", 2), ("개암 버섯", 10), ("마늘", 6), ("생강", 2), ("소금", 2));
        Add("감자 샐러드", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-감자-샐러드-367" },
            ("감자", 4), ("양배추", 2), ("마요네즈", 1));
        Add("야채볶음", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-야채볶음-368" },
            ("감자", 8), ("양파", 3), ("양배추", 6), ("허브", 2));
        Add("리코타 치즈 샐러드", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-리코타-치즈-샐러드-369" },
            ("양배추", 4), ("치즈", 2), ("마요네즈", 1));
        Add("감자수프", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-감자수프-370" },
            ("감자", 12), ("우유", 5), ("허브", 3), ("후추", 6));
        Add("알리오 올리오", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-알리오-올리오-371" },
            ("마늘", 7), ("면", 2), ("양파", 2), ("후추", 2));
        Add("두부 스테이크", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-두부-스테이크-372" },
            ("두부", 5), ("파스닙", 6), ("마늘", 4), ("후추", 3), ("식용유", 2));
        Add("두부 국수", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-두부-국수-373" },
            ("두부", 5), ("물이 든 병", 5), ("양파", 5), ("무", 1), ("소금", 3));
        Add("토마토 리소토", new[] { Wiki },
            ("밥", 4), ("토마토", 2), ("양파", 3), ("치즈", 8), ("소금", 3));
        Add("호박 수프", new[] { Wiki, "https://mobile.mabi.zip/hobaksupeu" },
            ("호박", 10), ("감자", 6), ("양파", 6), ("생강", 2), ("생크림", 5));
        Add("오트밀 영양죽", new[] { Wiki },
            ("오트밀", 5), ("물에 불린 콩", 4), ("사과", 8), ("당근", 5), ("소금", 2));
        Add("가든 샐러드", new[] { Wiki },
            ("양배추", 8), ("파스닙", 10), ("셀러리", 2), ("마요네즈", 4), ("허브", 10));
        Add("사과 샐러드", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-사과-샐러드-375" },
            ("사과", 4), ("양배추", 2), ("마요네즈", 1));
        Add("콘치즈", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-콘치즈-376" },
            ("옥수수", 10), ("치즈", 2), ("허브", 2), ("설탕", 3));
        Add("사과 수플레", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-사과-수플레-377" },
            ("사과", 8), ("밀가루", 2), ("달걀", 4), ("설탕", 4));
        Add("얼음 딸기주스", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-얼음-딸기주스-378" },
            ("얼음", 10), ("딸기", 6), ("허브", 3), ("설탕", 4));
        Add("사과 생크림케이크", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-사과-생크림케이크-379" },
            ("사과", 12), ("생크림", 2), ("밀가루", 4), ("설탕", 9));
        Add("두유 빙수", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-두유-빙수-380" },
            ("두유", 4), ("얼음", 10), ("레몬", 2), ("설탕", 6), ("허브", 3));
        Add("두유 파스닙 케이크", new[] { Wiki, "https://mobibook.site/crafting#crafting-item-두유-파스닙-케이크-381" },
            ("두유", 4), ("파스닙", 6), ("생크림", 2), ("밀가루", 4), ("설탕", 9));
        Add("녹차 크레이프 케이크", new[] { Wiki, "https://mobile.mabi.zip/nokchakeureipeukeikeu" },
            ("말린 찻잎", 10), ("딸기", 6), ("생크림", 6), ("밀가루", 4), ("설탕", 6));
        Add("호박 파이", new[] { Wiki, "https://mobile.mabi.zip/hobakpai" },
            ("호박", 10), ("생강", 2), ("생크림", 3), ("밀가루", 4), ("설탕", 2));
        Add("달콤쌉쌀 푸딩", new[] { Wiki },
            ("헤이즐넛", 10), ("두유", 6), ("말린 찻잎", 4), ("딸기", 8), ("설탕", 7));
        Add("넛츠 초코 비스킷", new[] { Wiki },
            ("헤이즐넛", 10), ("밀가루", 8), ("조각 초콜릿", 5), ("달걀", 4), ("생크림", 6));
        Add("부야베스", new[] { Wiki, "https://mobile.mabi.zip/buyabeseu" },
            ("무지개 송어", 4), ("은어", 4), ("토마토", 6), ("조개", 5), ("양파", 2), ("마늘", 4));
        Add("고등어와 연어 스테이크", new[] { Wiki, "https://mobile.mabi.zip/godeungeowayeoneoseuteikeu" },
            ("고등어", 5), ("연어", 3), ("마요네즈", 2), ("아스파라거스", 4), ("소금", 2), ("후추", 3));
        Add("메기 피시 앤 칩스", new[] { Wiki },
            ("메기", 6), ("감자", 6), ("밀가루", 3), ("완두콩", 4), ("레몬", 2), ("소금", 3));
        Add("농어 매운탕", new[] { Wiki, "https://mobile.mabi.zip/nongeomaeuntang" },
            ("농어", 6), ("무", 4), ("양파", 6), ("물이 든 병", 5), ("고춧가루", 4), ("후추", 2));
        Add("대만찬: 꺼지지 않는 불의 뵈프 부르기뇽", new[] { Wiki, "https://mobile.mabi.zip/daemanchankkeojijianhneunbuluiboepeubureuginyong" },
            ("만찬 재료: 꺼지지 않는 불", 4), ("숙성된 커다란 고기", 8), ("묵직한 감자", 24), ("토마토", 16), ("양파", 16), ("파스닙", 16), ("소금", 4));
        Add("금은 매운탕", new[] { Wiki, "https://mobile.mabi.zip/geumeunmaeuntang" },
            ("금린어", 4), ("은붕어", 4), ("무", 6), ("물이 든 병", 5), ("양파", 10), ("고춧가루", 4));
        Add("송어 파피요트", new[] { Wiki },
            ("갈색송어", 6), ("아스파라거스", 4), ("마늘", 6), ("레몬", 5), ("소금", 2), ("후추", 4));
        Add("대만찬: 거센 바람의 페어리 플로스", new[] { Wiki },
            ("만찬 재료: 휘몰아치는 바람", 4), ("구름결 막대", 8), ("설탕", 24), ("증폭 버섯 진액", 16), ("솔솔 버섯 진액", 16), ("응축된 바람", 16), ("마력 기폭제", 4));
        Add("토마토 크림 생선 커넬", new[] { Wiki },
            ("민물꼬치고기", 4), ("생크림", 4), ("토마토", 6), ("달걀", 12), ("셀러리", 4), ("후추", 3));
        Add("풍미 가득 스테이크", new[] { Wiki },
            ("숙성된 커다란 고기", 4), ("헤이즐넛 오일", 6), ("셀러리", 6), ("개암 버섯", 12), ("소금", 4), ("라벤더 꽃", 2));
        Add("구운 고기", new[] { "https://mobibook.site/crafting#crafting-item-구운-고기-358" },
            ("고기", 4), ("허브", 2), ("소금", 1));
        Add("통감자구이", new[] { "https://mobibook.site/crafting#crafting-item-통감자구이-366" },
            ("감자", 4), ("허브", 2), ("설탕", 2));
        Add("사과 주스", new[] { "https://mobibook.site/crafting#crafting-item-사과-주스-374" },
            ("사과", 4), ("허브", 2), ("물이 든 병", 1), ("설탕", 2));
        Add("흰살생선 뫼니에르", new[] { "https://mobile.mabi.zip/huinsalsaengseonmoeniereu" },
            ("브리흐네 잉어", 5), ("은붕어", 3), ("감자", 5), ("밀가루", 3), ("레몬", 5), ("소금", 3));

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, CraftingFoodRecipe>(rows);
    }
}
