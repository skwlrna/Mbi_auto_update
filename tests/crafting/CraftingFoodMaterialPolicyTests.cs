using FishingAutomation;

internal static class CraftingFoodMaterialPolicyTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        static CraftingQuestDeficit Row(string name, long current, long required)
            => new(name, current, required, 700);

        static Func<string, CancellationToken, Task<long>> Stock(
            IReadOnlyDictionary<string, long> values)
            => (name, _) => Task.FromResult(values.TryGetValue(name, out var value) ? value : 0L);

        string[] shops =
        {
            "소금", "설탕", "식용유", "후추", "고기", "마늘", "레몬",
            "토마토", "아스파라거스", "완두콩", "무", "고춧가루",
            "딸기", "담백한 고기"
        };
        check(shops.All(CraftingFoodMaterialPolicy.IsShopOnly) &&
              !CraftingFoodMaterialPolicy.IsShopOnly("사과") &&
              !CraftingFoodMaterialPolicy.IsShopOnly("양배추") &&
              !CraftingFoodMaterialPolicy.IsShopOnly("밀") &&
              !CraftingFoodMaterialPolicy.IsShopOnly("우유") &&
              !CraftingFoodMaterialPolicy.IsShopOnly("콩"),
            "reviewed 14 shop-only ingredients do not block gatherable apples/cabbages/crops");

        string[] processed =
        {
            "밀가루", "치즈", "마요네즈", "면", "생크림",
            "두부", "두유", "물에 불린 콩", "밥"
        };
        check(processed.All(CraftingFoodMaterialPolicy.IsKnownProcessing) &&
              CraftingFoodMaterialPolicy.IsKnownProcessing("물에 불린 쌀") &&
              CraftingFoodMaterialPolicy.IsKnownProcessing("옷감") &&
              !CraftingFoodMaterialPolicy.IsKnownProcessing("달걀"),
            "all nine reviewed processed foods and their necessary nested intermediates cataloged");

        var direct = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("밀", 0, 15), Row("소금", 2, 5) },
            (_, _) => throw new Exception("direct shop shortage requires no extra stock read"),
            CancellationToken.None);
        check(direct is { MaterialName: "소금", Required: 3 },
            "shop shortage detected before harvesting even when the shop row is not first");

        var cheese = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("치즈", 0, 6) },
            Stock(new Dictionary<string, long> { ["소금"] = 1 }),
            CancellationToken.None);
        check(cheese is { MaterialName: "소금", Required: 1 },
            "six missing cheese require two salt through three-per-work recipe");

        var cheeseEnough = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("치즈", 0, 6) },
            Stock(new Dictionary<string, long> { ["소금"] = 2 }),
            CancellationToken.None);
        check(cheeseEnough is null, "already purchased salt allows cheese gathering and processing");

        var soybean = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("물에 불린 콩", 0, 10) },
            Stock(new Dictionary<string, long>()), CancellationToken.None);
        var flour = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("밀가루", 0, 3), Row("면", 0, 3) },
            Stock(new Dictionary<string, long>()), CancellationToken.None);
        var rice = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("밥", 0, 3) },
            Stock(new Dictionary<string, long>()), CancellationToken.None);
        check(soybean is null && flour is null && rice is null,
            "gather-only chains soybeans, flour, noodles and rice are not falsely blocked");

        var soyMilk = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("두유", 0, 3) },
            Stock(new Dictionary<string, long> { ["소금"] = 1, ["식용유"] = 0 }),
            CancellationToken.None);
        check(soyMilk is { MaterialName: "식용유", Required: 1 },
            "soy milk recursion checks both purchased salt and cooking oil");

        var tofu = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("두부", 0, 3) },
            Stock(new Dictionary<string, long> { ["소금"] = 2, ["옷감"] = 0 }),
            CancellationToken.None);
        check(tofu is null, "tofu with sufficient salt can process cloth from sheep wool");

        var mayonnaise = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("마요네즈", 0, 3) },
            Stock(new Dictionary<string, long> { ["식용유"] = 1 }),
            CancellationToken.None);
        var cream = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("생크림", 0, 3) },
            Stock(new Dictionary<string, long> { ["설탕"] = 1 }),
            CancellationToken.None);
        check(mayonnaise is { MaterialName: "식용유", Required: 1 } &&
              cream is { MaterialName: "설탕", Required: 1 },
            "mayonnaise and cream block when purchased inputs fall short");

        var sharedSalt = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("치즈", 0, 3), Row("두부", 0, 3) },
            Stock(new Dictionary<string, long> { ["소금"] = 2, ["옷감"] = 3 }),
            CancellationToken.None);
        check(sharedSalt is { MaterialName: "소금", Required: 1 },
            "shared shop stock is counted once across multiple recipe shortages");

        var noName = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
            new[] { Row("", 0, 10) },
            (_, _) => throw new Exception("unreadable name should be left to UI safety"),
            CancellationToken.None);
        check(noName is null, "unreadable material is never falsely identified as a shop item");
    }
}
