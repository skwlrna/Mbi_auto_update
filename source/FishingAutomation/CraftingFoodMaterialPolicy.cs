using DungeonVisionBot;
namespace FishingAutomation;

// Reviewed food-only acquisition catalog. This is intentionally an exact-name
// allow/deny map rather than guessing from substrings or "sold by NPC" alone.
// Shop-only here means "no supported life-skill gathering route"; the macro
// never buys materials. A purchased item already in inventory is usable.
internal sealed record CraftingFoodIngredient(string Name, long Amount);
internal sealed record CraftingFoodProcessingRecipe(
    long Produced, IReadOnlyList<CraftingFoodIngredient> Ingredients);
internal sealed record CraftingFoodPurchaseShortage(
    string MaterialName, long Required, long Owned, string Parent);

internal static class CraftingFoodMaterialPolicy
{
    private static readonly HashSet<string> ShopOnly = new(StringComparer.Ordinal)
    {
        "소금", "설탕", "식용유", "후추", "고기", "마늘", "레몬",
        "토마토", "아스파라거스", "완두콩", "무", "고춧가루",
        "딸기", "담백한 고기"
    };


    // Confirmed food materials that cannot be obtained by the current
    // crafting quest's ordinary field gathering. Some are MyHome-grown,
    // dungeon/raid/feast loot, fish, or shop stock (not shop-only).
    // An item already owned is always usable. Never buy or fish automatically.
    private static readonly HashSet<string> OtherUnsupportedSources = new(StringComparer.Ordinal)
    {
        "셀러리", "샐러리", "생강", "커다란 고기",
        "만찬 재료: 휘몰아치는 바람", "만찬 재료: 꺼지지 않는 불",
        "파프리카", "조각 초콜릿", "조각 초콜렛", "라벤더 꽃",
        "당근", "카레 가루",
        "브리흐네 잉어", "은붕어", "무지개 송어", "은어",
        "고등어", "연어", "메기", "농어", "금린어", "갈색송어",
        "파이크퍼치", "민물꼬치고기"
    };

    internal static bool IsUnsupportedDirect(string? name)
        => name is not null &&
           (ShopOnly.Contains(name.Trim()) || OtherUnsupportedSources.Contains(name.Trim()));

    // Publicly documented food/fabric processing recipes. The game CLI
    // is authoritative for whether a recipe can be produced at run time;
    // this table is used only to detect missing shop inputs BEFORE gathering.
    private static readonly Dictionary<string, CraftingFoodProcessingRecipe> Processing =
        new(StringComparer.Ordinal)
        {
            ["마요네즈"] = Recipe(3, ("달걀", 10), ("식용유", 2)),
            ["밀가루"] = Recipe(3, ("밀", 15)),
            ["치즈"] = Recipe(3, ("우유", 6), ("소금", 1)),
            ["면"] = Recipe(3, ("밀가루", 3), ("달걀", 5), ("물이 든 병", 1)),
            ["생크림"] = Recipe(3, ("우유", 12), ("달걀", 6), ("설탕", 2)),
            ["물에 불린 콩"] = Recipe(5, ("콩", 30), ("물이 든 병", 3)),
            ["두부"] = Recipe(3, ("물에 불린 콩", 3), ("물이 든 병", 1),
                ("소금", 2), ("옷감", 3)),
            ["두유"] = Recipe(3, ("물에 불린 콩", 3), ("물이 든 병", 3),
                ("소금", 1), ("식용유", 1)),
            ["밥"] = Recipe(3, ("물에 불린 쌀", 8), ("물이 든 병", 2)),

            // Nested prerequisite recipes for the nine reviewed foods.
            ["물에 불린 쌀"] = Recipe(5, ("쌀", 30), ("물이 든 병", 6)),
            ["옷감"] = Recipe(3, ("양털", 10)),
            // Verified higher-level food intermediates.
            ["말린 찻잎"] = Recipe(3, ("찻잎", 30)),
            ["발효된 찻잎"] = Recipe(3, ("말린 찻잎", 5), ("옷감", 1)),
            ["오트밀"] = Recipe(3, ("귀리", 30)),
            ["숙성된 커다란 고기"] = Recipe(1, ("커다란 고기", 1),
                ("허브", 6), ("마늘", 2), ("소금", 2), ("후추", 1)),
            ["옷감+"] = Recipe(3, ("옷감", 3), ("양털", 4)),
            ["헤이즐넛 오일"] = Recipe(3, ("헤이즐넛", 15), ("옷감+", 1))
        };

    internal static bool IsShopOnly(string? name)
        => name is not null && ShopOnly.Contains(name.Trim());

    internal static bool IsKnownProcessing(string? name)
        => name is not null && Processing.ContainsKey(name.Trim());

    internal static bool TryGetProcessing(string? name, out CraftingFoodProcessingRecipe recipe)
        => Processing.TryGetValue((name ?? "").Trim(), out recipe!);

    internal static async Task<CraftingFoodPurchaseShortage?> FindFirstPurchaseShortageAsync(
        IReadOnlyList<CraftingQuestDeficit> deficits,
        Func<string, CancellationToken, Task<long>> readFreshInventory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deficits);
        ArgumentNullException.ThrowIfNull(readFreshInventory);

        // The quest's Current/Required already accounts for root inventory.
        // Child ingredients are read once and then shared across all roots.
        var left = new Dictionary<string, long>(StringComparer.Ordinal);
        async Task<long> AvailableAsync(string name)
        {
            if (!left.TryGetValue(name, out long available))
            {
                available = await readFreshInventory(name, ct);
                if (available < 0)
                    throw new InvalidDataException("재료 보유량이 음수입니다: " + name);
                left.Add(name, available);
            }
            return available;
        }

        async Task<CraftingFoodPurchaseShortage?> VisitAsync(
            string name, long missing, string parent, bool isRoot, HashSet<string> visiting)
        {
            ct.ThrowIfCancellationRequested();
            if (missing <= 0) return null;
            if (string.IsNullOrWhiteSpace(name)) return null; // OCR uncertainty is handled by screen safety.
            name = name.Trim();
            if (!visiting.Add(name))
                throw new InvalidDataException("가공 재료 순환 의존성이 감지되었습니다: " + name);
            try
            {
                if (isRoot && IsUnsupportedDirect(name))
                    return new CraftingFoodPurchaseShortage(name, missing, 0, parent);

                if (!isRoot)
                {
                    // Raw harvestables do not need inventory preflight; the
                    // existing gatherer obtains them when the quest needs them.
                    if (!IsUnsupportedDirect(name) && !Processing.ContainsKey(name))
                        return null;
                    long available = await AvailableAsync(name);
                    long consumed = Math.Min(available, missing);
                    left[name] = available - consumed;
                    missing -= consumed;
                    if (missing <= 0) return null;
                    if (IsUnsupportedDirect(name))
                        return new CraftingFoodPurchaseShortage(name, missing, available, parent);
                }

                if (!Processing.TryGetValue(name, out var recipe))
                    return null; // Harvest or unknown route: do not claim it is shop-only.

                long works = checked((missing + recipe.Produced - 1) / recipe.Produced);
                foreach (var ingredient in recipe.Ingredients)
                {
                    long demand = checked(works * ingredient.Amount);
                    var shortage = await VisitAsync(ingredient.Name, demand, name, false, visiting);
                    if (shortage is not null) return shortage;
                }
                return null;
            }
            finally
            {
                visiting.Remove(name);
            }
        }

        foreach (var deficit in deficits)
        {
            ct.ThrowIfCancellationRequested();
            if (deficit.Required <= deficit.Current) continue;
            long missing = checked(deficit.Required - deficit.Current);
            var shortage = await VisitAsync(deficit.DisplayName, missing,
                deficit.DisplayName, true, new HashSet<string>(StringComparer.Ordinal));
            if (shortage is not null)
                return shortage;
        }
        return null;
    }

    private static CraftingFoodProcessingRecipe Recipe(
        long produced, params (string Name, long Amount)[] ingredients)
        => new(produced, ingredients.Select(x =>
            new CraftingFoodIngredient(x.Name, x.Amount)).ToArray());
}
