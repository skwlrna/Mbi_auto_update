using System.Text.Json;

namespace FishingAutomation;

internal enum CraftingCategory
{
    Unknown,
    Food,
    Item
}

internal sealed record CraftingIngredient(string DisplayName, long Required, long Owned);

internal sealed record CraftableItem(
    string DisplayName,
    bool Craftable,
    int ProducedPerCraft,
    string? Reason,
    IReadOnlyList<CraftingIngredient> MissingIngredients,
    CraftingCategory Category);

internal static class CraftingQueries
{
    internal static IReadOnlyList<CraftableItem> ParseCatalog(MabinogiCliResult response)
    {
        if (!response.Success || response.Data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("제작 품목 조회에 실패했습니다.");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("제작 품목 목록 형식이 올바르지 않습니다.");

        var result = new List<CraftableItem>();
        foreach (var row in items.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("제작 품목 행 형식이 올바르지 않습니다.");

            string name = RequiredString(row, "DisplayName");

            bool craftable = OptionalBool(row, "Craftable") ?? OptionalBool(row, "Available") ?? true;
            int produced = OptionalInt(row, "ProducedPerCraft") ?? OptionalInt(row, "Produced") ?? 1;
            if (produced <= 0 || produced > 1_000_000)
                throw new InvalidDataException("제작 생산 수량이 올바르지 않습니다.");

            string? reason = OptionalString(row, "Reason");
            var missing = new List<CraftingIngredient>();
            if (row.TryGetProperty("MissingIngredients", out var ingredients))
            {
                if (ingredients.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("제작 재료 목록 형식이 올바르지 않습니다.");
                foreach (var ingredient in ingredients.EnumerateArray())
                {
                    long required = OptionalLong(ingredient, "Required") ?? 0;
                    long owned = OptionalLong(ingredient, "Owned") ?? 0;
                    if (required < 0 || owned < 0)
                        throw new InvalidDataException("제작 재료 수량이 올바르지 않습니다.");
                    missing.Add(new(RequiredString(ingredient, "DisplayName"), required, owned));
                }
            }

            result.Add(new(name, craftable, produced, reason, missing, InferCategory(row)));
        }
        return result;
    }

    internal static CraftableItem Exact(IReadOnlyList<CraftableItem> items, string displayName)
    {
        var exact = items.Where(x => string.Equals(x.DisplayName, displayName, StringComparison.Ordinal)).ToArray();
        return exact.Length switch
        {
            1 => exact[0],
            0 => throw new InvalidOperationException($"제작 품목을 정확히 찾지 못했습니다: {displayName}"),
            _ => throw new InvalidOperationException($"동일 이름 제작 품목이 여러 개라 자동 선택하지 않습니다: {displayName}")
        };
    }

    internal static IEnumerable<CraftableItem> ForUiCategory(IEnumerable<CraftableItem> items, CraftingCategory category)
    {
        var snapshot = items.ToArray();
        if (snapshot.Length == 0)
            return Array.Empty<CraftableItem>();

        int known = snapshot.Count(x => x.Category != CraftingCategory.Unknown);

        // V3.0.18 hid Unknown rows entirely. The live game CLI currently returns
        // catalogs where category metadata can be absent for most/all rows, which
        // made both tabs empty. If reliable metadata covers less than 25% of the
        // catalog, classify only the Unknown rows with a deterministic name/recipe
        // fallback. Each row still belongs to exactly one tab, never both.
        bool sparseMetadata = known * 4 < snapshot.Length;
        return snapshot.Where(x =>
            x.Category == category ||
            (x.Category == CraftingCategory.Unknown &&
             sparseMetadata &&
             FallbackCategory(x) == category));
    }

    internal static CraftingCategory ResolveUiCategory(CraftableItem item)
        => item.Category != CraftingCategory.Unknown
            ? item.Category
            : FallbackCategory(item);

    internal static (int Food, int Item, int Unknown, bool UsesFallback) CategoryCoverage(
        IEnumerable<CraftableItem> items)
    {
        var snapshot = items.ToArray();
        int food = snapshot.Count(x => x.Category == CraftingCategory.Food);
        int item = snapshot.Count(x => x.Category == CraftingCategory.Item);
        int unknown = snapshot.Length - food - item;
        return (food, item, unknown, snapshot.Length > 0 && (food + item) * 4 < snapshot.Length);
    }

    private static CraftingCategory FallbackCategory(CraftableItem item)
    {
        string name = item.DisplayName.Replace(" ", "", StringComparison.Ordinal);
        string[] foodTokens =
        {
            "볶음", "샐러드", "구이", "튀김", "수프", "스프", "찌개", "국", "탕",
            "밥", "죽", "덮밥", "김밥", "초밥", "국수", "라면", "파스타", "면요리",
            "스테이크", "꼬치", "오믈렛", "샌드위치", "버거", "빵", "케이크", "쿠키",
            "파이", "푸딩", "아이스크림", "초콜릿", "사탕", "캔디", "젤리", "잼",
            "주스", "음료", "차", "커피", "요리"
        };
        if (foodTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return CraftingCategory.Food;

        // A non-craftable food recipe often exposes only its missing ingredients.
        // Require at least two strong cooking ingredients so item recipes using one
        // herb/bottle are not accidentally moved to the food tab.
        string[] foodIngredients =
        {
            "감자", "양파", "양배추", "당근", "토마토", "밀", "쌀", "달걀", "계란",
            "고기", "생선", "우유", "버터", "치즈", "소금", "설탕", "후추", "버섯"
        };
        int ingredientClues = item.MissingIngredients.Count(ingredient =>
            foodIngredients.Any(token =>
                ingredient.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)));
        if (ingredientClues >= 2)
            return CraftingCategory.Food;

        return CraftingCategory.Item;
    }

    internal static int RequiredCrafts(long targetQuantity, int producedPerCraft)
    {
        if (targetQuantity < 1 || targetQuantity > 1_000_000 || producedPerCraft < 1)
            throw new InvalidDataException("제작 목표 수량이 올바르지 않습니다.");
        return checked((int)((targetQuantity + producedPerCraft - 1L) / producedPerCraft));
    }

    internal static int NextBatchCrafts(int remainingCrafts)
    {
        if (remainingCrafts < 1)
            return 0;
        return Math.Min(10, remainingCrafts);
    }

    private static CraftingCategory InferCategory(JsonElement row)
    {
        // Prefer explicit category/facility/type fields when the CLI exposes them.
        foreach (var property in row.EnumerateObject())
        {
            if (!LooksLikeCategoryProperty(property.Name))
                continue;
            var category = ClassifyCategoryText(Strings(property.Value));
            if (category != CraftingCategory.Unknown)
                return category;
        }

        // Live CLI builds have changed field names before. Scan every string value
        // as a compatibility fallback so a category hint under a new property name
        // does not turn the whole catalog into Unknown.
        var anyValueCategory = ClassifyCategoryText(Strings(row));
        if (anyValueCategory != CraftingCategory.Unknown)
            return anyValueCategory;

        return CraftingCategory.Unknown;
    }

    private static CraftingCategory ClassifyCategoryText(IEnumerable<string> values)
    {
        foreach (string text in values)
        {
            if (text.Contains("음식", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("요리", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("food", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("cooking", StringComparison.OrdinalIgnoreCase))
                return CraftingCategory.Food;
            if (text.Contains("아이템", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("약품", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("다목적", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("캠프", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("item", StringComparison.OrdinalIgnoreCase))
                return CraftingCategory.Item;
        }
        return CraftingCategory.Unknown;
    }

    private static bool LooksLikeCategoryProperty(string name)
        => name.Contains("Category", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Facility", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Station", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Group", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Type", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Craft", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Strings(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                if (!string.IsNullOrWhiteSpace(value.GetString()))
                    yield return value.GetString()!;
                yield break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                    foreach (string text in Strings(property.Value))
                        yield return text;
                yield break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray())
                    foreach (string text in Strings(child))
                        yield return text;
                yield break;
        }
    }

    private static string RequiredString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"제작 데이터의 {name} 값이 없습니다.");
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool? OptionalBool(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;

    private static int? OptionalInt(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.TryGetInt32(out int result) ? result : null;

    private static long? OptionalLong(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.TryGetInt64(out long result) ? result : null;
}
