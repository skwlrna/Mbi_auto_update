using System.Text.Json;

namespace FishingAutomation;

internal sealed record AlteringIngredient(string DisplayName, long Required, long Owned);
internal sealed record AlteringRecipe(string DisplayName, bool Alterable, int ProducedPerWork,
    string? Reason, IReadOnlyList<AlteringIngredient> MissingIngredients, string? FacilityName = null);
internal sealed record AlteringWork(string DisplayName, string FacilityName, string State,
    bool IsCompleted, long RemainingSeconds);

internal static class AlteringFacilityResolver
{
    private static readonly string[] KnownFacilities =
    {
        "금속 가공 시설", "목재 가공 시설", "가죽 가공 시설",
        "옷감 가공 시설", "약품 가공 시설", "식재료 가공 시설"
    };

    private static readonly HashSet<string> FoodRecipes = new(StringComparer.Ordinal)
    {
        "마요네즈", "버터", "밀가루", "치즈", "면", "생크림", "물에 불린 콩", "두부", "두유",
        "숙성된 커다란 고기", "물에 불린 쌀", "밥", "말린 찻잎", "발효된 찻잎",
        "헤이즐넛 오일", "오트밀", "식용유"
    };

    private static readonly HashSet<string> MedicineRecipes = new(StringComparer.Ordinal)
    {
        "새록 버섯 진액", "튼튼 버섯 가루", "튼튼 버섯 진액",
        "광휘의 결정(유령 반딧불이)", "새록 버섯 포자", "튼튼 버섯 포자",
        "쑥쑥 버섯 포자", "쑥쑥 버섯 진액", "불꽃의 결정(석양 나비)", "아교",
        "숨숨꽃 가루", "깔끔 버섯 포자", "깔끔 버섯 진액", "얼음의 결정(흰얼음풍뎅이)",
        "마력 기폭제", "봉인된 분노의 파편", "봉인된 망각의 파편", "봉인된 야성의 파편",
        "생채기꽃 가루", "증폭 버섯 포자", "증폭 버섯 진액", "전기의 결정(낙엽나방)",
        "진정초 가루", "솔솔 버섯 포자", "솔솔 버섯 진액", "봉인의 결정(황혼잠자리)",
        "환영 가루"
    };

    internal static string? Resolve(AlteringRecipe recipe)
        => NormalizeFacility(recipe.FacilityName) ?? ResolveByName(recipe.DisplayName);

    internal static string? FromJson(JsonElement item, string displayName)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name is "DisplayName" or "Reason" or "MissingIngredients")
                    continue;

                bool facilityMetadata =
                    property.Name.Contains("Facility", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Category", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Group", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Station", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Alter", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("Type", StringComparison.OrdinalIgnoreCase);

                if (!facilityMetadata) continue;

                string? direct = NormalizeFacility(property.Name);
                if (direct is not null) return direct;

                string? nested = FindFacilityString(property.Value);
                if (nested is not null) return nested;
            }
        }

        return ResolveByName(displayName);
    }

    private static string? FindFacilityString(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return NormalizeFacility(value.GetString());
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    string? direct = NormalizeFacility(property.Name);
                    if (direct is not null) return direct;
                    string? nested = FindFacilityString(property.Value);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray())
                {
                    string? nested = FindFacilityString(child);
                    if (nested is not null) return nested;
                }
                break;
        }
        return null;
    }

    private static string? NormalizeFacility(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        foreach (string facility in KnownFacilities)
        {
            string title = facility.Replace(" 시설", "");
            if (value.Equals(facility, StringComparison.Ordinal) ||
                value.Equals(title, StringComparison.Ordinal) ||
                value.Contains(facility, StringComparison.Ordinal) ||
                value.Contains(title, StringComparison.Ordinal))
                return facility;
        }
        return null;
    }

    private static string? ResolveByName(string displayName)
    {
        string output = System.Text.RegularExpressions.Regex
            .Replace(displayName, @"\([^()]*\)$", "").Trim();

        // Exact food names are checked before generic "가루/결정/포자" medicine
        // patterns so names such as 밀가루 never fall into the medicine facility.
        if (FoodRecipes.Contains(output) ||
            output.StartsWith("물에 불린 ", StringComparison.Ordinal) ||
            output.StartsWith("숙성된 ", StringComparison.Ordinal) ||
            output.Contains("찻잎", StringComparison.Ordinal) ||
            output.EndsWith(" 오일", StringComparison.Ordinal))
            return "식재료 가공 시설";
        if (MedicineRecipes.Contains(displayName) || MedicineRecipes.Contains(output))
            return "약품 가공 시설";

        if (output.Contains("목재", StringComparison.Ordinal) ||
            output.Contains("원목", StringComparison.Ordinal) ||
            output.Contains("통나무", StringComparison.Ordinal))
            return "목재 가공 시설";
        if (output.Contains("괴", StringComparison.Ordinal) ||
            output.Contains("철", StringComparison.Ordinal) ||
            output.Contains("금속", StringComparison.Ordinal))
            return "금속 가공 시설";
        if (output.Contains("가죽", StringComparison.Ordinal) ||
            output.Contains("피혁", StringComparison.Ordinal))
            return "가죽 가공 시설";
        if (output.Contains("옷감", StringComparison.Ordinal) ||
            output.Contains("실크", StringComparison.Ordinal) ||
            output.Contains("식물 섬유", StringComparison.Ordinal) ||
            output.Contains("밧줄", StringComparison.Ordinal))
            return "옷감 가공 시설";

        if (output.Contains("버섯", StringComparison.Ordinal) ||
            output.Contains("결정", StringComparison.Ordinal) ||
            output.Contains("포자", StringComparison.Ordinal) ||
            output.Contains("진액", StringComparison.Ordinal) ||
            output.Contains("기폭제", StringComparison.Ordinal) ||
            output.Contains("봉인된 ", StringComparison.Ordinal) ||
            output.EndsWith("가루", StringComparison.Ordinal) ||
            output.Equals("아교", StringComparison.Ordinal) ||
            output.Equals("환영 가루", StringComparison.Ordinal))
            return "약품 가공 시설";

        return null;
    }
}

/// <summary>Typed read-only data. This module never starts, collects, or cancels work.</summary>
internal static class AlteringQueries
{
    internal static IReadOnlyList<AlteringRecipe> ParseRecipes(MabinogiCliResult response)
    {
        JsonElement root = RequireData(response);
        var recipes = new List<AlteringRecipe>();
        foreach (var item in RequireArray(root, "items").EnumerateArray())
        {
            string name = RequireString(item, "DisplayName");
            bool alterable = item.GetProperty("Alterable").GetBoolean();
            int produced = item.GetProperty("ProducedPerWork").GetInt32();
            if (produced <= 0) throw new InvalidDataException("invalid_produced_quantity");
            string? reason = item.TryGetProperty("Reason", out var reasonValue) ? reasonValue.GetString() : null;
            var missing = new List<AlteringIngredient>();
            if (item.TryGetProperty("MissingIngredients", out _))
            {
                foreach (var ingredient in RequireArray(item, "MissingIngredients").EnumerateArray())
                {
                    long required = ingredient.GetProperty("Required").GetInt64();
                    long owned = ingredient.GetProperty("Owned").GetInt64();
                    if (required < 0 || owned < 0) throw new InvalidDataException("invalid_ingredient_quantity");
                    missing.Add(new(RequireString(ingredient, "DisplayName"), required, owned));
                }
            }
            string? facility = AlteringFacilityResolver.FromJson(item, name);
            recipes.Add(new(name, alterable, produced, reason, missing, facility));
        }
        return recipes;
    }

    internal static IReadOnlyList<AlteringWork> ParseWorks(MabinogiCliResult response)
    {
        JsonElement root = RequireData(response);
        var works = new List<AlteringWork>();
        foreach (var item in RequireArray(root, "works").EnumerateArray())
        {
            string state = RequireString(item, "State");
            bool completed = item.GetProperty("IsCompleted").GetBoolean();
            long remaining = item.GetProperty("RemainingSeconds").GetInt64();
            if (state is not ("Completed" or "InProgress" or "NotStarted") || remaining < 0 ||
                completed != (state == "Completed") || (completed && remaining != 0))
                throw new InvalidDataException("invalid_work_state");
            works.Add(new(RequireString(item, "DisplayName"), RequireString(item, "FacilityName"),
                state, completed, remaining));
        }
        int expected = root.GetProperty("completedCount").GetInt32();
        if (expected != works.Count(x => x.IsCompleted)) throw new InvalidDataException("completed_count_mismatch");
        return works;
    }

    private static JsonElement RequireData(MabinogiCliResult response)
    {
        if (!response.Success || response.Data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("altering_query_failed");
        return root;
    }

    private static JsonElement RequireArray(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("invalid_array");
        return value;
    }

    private static string RequireString(JsonElement item, string name)
    {
        string? value = item.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("missing_name");
    }
}
