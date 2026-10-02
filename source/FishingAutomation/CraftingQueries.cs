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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in items.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("제작 품목 행 형식이 올바르지 않습니다.");

            string name = RequiredString(row, "DisplayName");
            if (!seen.Add(name))
                continue;

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
        => items.Where(x => x.Category == category || x.Category == CraftingCategory.Unknown);

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
        foreach (var property in row.EnumerateObject())
        {
            if (!LooksLikeCategoryProperty(property.Name))
                continue;
            foreach (string text in Strings(property.Value))
            {
                if (text.Contains("음식", StringComparison.OrdinalIgnoreCase) ||
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
