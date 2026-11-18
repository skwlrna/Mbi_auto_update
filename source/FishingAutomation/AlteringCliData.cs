namespace FishingAutomation;

internal sealed class AlteringCliData : IAlteringData
{
    private readonly MabinogiMobileCli _cli;
    internal AlteringCliData(MabinogiMobileCli cli) => _cli = cli;
    public async Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
    {
        var recipes = AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync(ct));
        var ingredientNames = recipes
            .Where(NeedsLeatherStockCheck)
            .SelectMany(recipe => recipe.MissingIngredients)
            .Select(ingredient => ingredient.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ingredientNames.Length == 0)
            return recipes;

        // Crafting consumes stock from the bag and both warehouses.
        // Only reconcile an explicit material veto for the leather facility.
        var stock = await ItemCountsAsync(ingredientNames, ct);
        return recipes.Select(recipe =>
        {
            if (!NeedsLeatherStockCheck(recipe))
                return recipe;

            var missing = recipe.MissingIngredients
                .Select(ingredient => ingredient with { Owned = stock[ingredient.DisplayName] })
                .Where(ingredient => ingredient.Owned < ingredient.Required)
                .ToArray();
            return recipe with
            {
                MissingIngredients = missing,
                Alterable = missing.Length == 0,
                Reason = missing.Length == 0 ? null : recipe.Reason
            };
        }).ToArray();
    }

    private static bool NeedsLeatherStockCheck(AlteringRecipe recipe)
        => !recipe.Alterable &&
           recipe.Reason == "not_enough_ingredient" &&
           recipe.MissingIngredients.Count > 0 &&
           recipe.MissingIngredients.All(ingredient => ingredient.Required > 0) &&
           AlteringFacilityResolver.Resolve(recipe) == "가죽 가공 시설";

    public async Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
        => AlteringQueries.ParseWorks(await _cli.GetAlteringWorksAsync(ct));
    public async Task<long> ItemCountAsync(string name, CancellationToken ct)
    {
        var counts = await ItemCountsAsync(new[] { name }, ct);
        return counts[name];
    }

    internal async Task<IReadOnlyDictionary<string, long>> ItemCountsAsync(
        IReadOnlyList<string> names,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0)
            return new Dictionary<string, long>(StringComparer.Ordinal);

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("인벤토리 품목 이름이 비어 있습니다.");
            if (aliases.Values.Contains(name, StringComparer.Ordinal))
                continue;
            aliases[$"output{i}"] = name;
        }

        var response = await _cli.GetItemsAsync(ct);
        if (!response.Success || response.Data is null)
            throw new InvalidDataException("인벤토리 조회 실패");

        var totals = InventoryLootTracker.Parse(response.Data.Value, aliases)
            ?? throw new InvalidDataException("인벤토리 수량 파싱 실패");

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pair in aliases)
            result[pair.Value] = totals[pair.Key];
        return result;
    }
}
