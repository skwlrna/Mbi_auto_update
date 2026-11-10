using FishingAutomation;

internal static class CraftingFoodRecipeCatalogTests
{
    internal static void Run(Action<bool, string> check)
    {
        var recipes = CraftingFoodRecipeCatalog.All.ToArray();
        check(recipes.Length == 52 && recipes.All(x =>
            CraftingQueries.IsVerifiedFoodRecipe(x.DisplayName) &&
            x.Ingredients.Count > 0 && x.Sources.Count > 0 &&
            x.Ingredients.All(i => i.Amount > 0 && !string.IsNullOrWhiteSpace(i.Name)) &&
            x.Ingredients.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() == x.Ingredients.Count),
            "registered food recipes have supported identities, positive unique direct inputs and sources");
        check(CraftingFoodRecipeCatalog.TryGet("호박 수프", out var soup),
            "pumpkin soup is registered independently of CLI shortages");
        var batch = soup.ForBatch(10).ToDictionary(x => x.Name, x => x.Amount);
        check(batch.Count == 5 && batch["호박"] == 100 && batch["감자"] == 60 &&
              batch["양파"] == 60 && batch["생강"] == 20 && batch["생크림"] == 50,
            "ten-craft soup retains ALL inputs including sufficient ginger and cream");
        check(soup.Ingredients.Single(x => x.Name == "호박").Amount == 10 &&
              !batch.ContainsKey("설탕") && !batch.ContainsKey("우유"),
            "batch scaling cannot mutate one-craft recipe or replace cream with raw processing inputs");
        check(CraftingFoodRecipeCatalog.TryGet("가든 샐러드", out var garden) &&
              garden.ForBatch(10).Single(x => x.Name == "셀러리").Amount == 20 &&
              garden.ForBatch(10).Single(x => x.Name == "마요네즈").Amount == 40,
            "garden salad includes unsupported and processed inputs regardless of inventory");
        check(CraftingFoodRecipeCatalog.TryGet("달걀 프라이", out var egg) &&
              CraftingFoodRecipeCatalog.TryGet("달걀프라이", out var canonicalEgg) &&
              ReferenceEquals(egg, canonicalEgg) &&
              CraftingFoodRecipeCatalog.TryGet("토마토 리조토", out var risotto) &&
              risotto.DisplayName == "토마토 리소토" &&
              !CraftingFoodRecipeCatalog.TryGet("호박", out _) &&
              !CraftingFoodRecipeCatalog.TryGet(null, out _),
            "only explicit recipe aliases resolve; partial names and null never guess");
        check(CraftingFoodRecipeCatalog.Unresolved.Count == 2 &&
              CraftingFoodRecipeCatalog.Unresolved.Keys.All(x =>
                  !CraftingFoodRecipeCatalog.TryGet(x, out _)),
            "conflicting published quantities never enter the usable recipe catalog");
        foreach (int crafts in new[] { 0, -1, 11, int.MaxValue })
        {
            bool rejected = false;
            try { soup.ForBatch(crafts); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "invalid batch rejected: " + crafts);
        }
    }
}
