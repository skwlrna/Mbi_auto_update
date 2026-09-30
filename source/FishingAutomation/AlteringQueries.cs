using System.Text.Json;

namespace FishingAutomation;

internal sealed record AlteringIngredient(string DisplayName, long Required, long Owned);
internal sealed record AlteringRecipe(string DisplayName, bool Alterable, int ProducedPerWork,
    string? Reason, IReadOnlyList<AlteringIngredient> MissingIngredients);
internal sealed record AlteringWork(string DisplayName, string FacilityName, string State,
    bool IsCompleted, long RemainingSeconds);

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
            recipes.Add(new(name, alterable, produced, reason, missing));
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
