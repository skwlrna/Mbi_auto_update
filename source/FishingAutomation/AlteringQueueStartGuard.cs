namespace FishingAutomation;

// Queue configuration is read-only. Revalidate the latest CLI capability at
// F9 BEFORE any game-window inputs, while preserving a deferred queue entry.
internal static class AlteringQueueStartGuard
{
    internal static void EnsureStartable(
        IReadOnlyList<AlteringPlan> plans,
        IReadOnlyList<AlteringRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(recipes);
        foreach (var plan in plans)
        {
            plan.Validate();
            var matching = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
            if (matching.Length < plan.RecipeOrdinal ||
                matching[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 제법 조회 결과가 등록 당시와 달라져 가공 시작을 중단합니다. 목록을 새로고침하세요.");

            var recipe = matching[plan.RecipeOrdinal - 1];
            if (!recipe.Alterable && recipe.MissingIngredients.Count == 0)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: CLI가 현재 가공 불가로 응답하여 F9 시작을 보류합니다. " +
                    $"Reason={recipe.Reason ?? "unknown"} · Missing=없음 · 작업 목록을 유지하고 가공 조건을 다시 조회하세요.");
            // Concrete shortages remain eligible for the existing recursive
            // material resolver, not an unsafe unconditional game input.
        }
    }
}
