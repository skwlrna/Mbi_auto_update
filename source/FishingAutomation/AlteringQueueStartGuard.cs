namespace FishingAutomation;

// Queue configuration is read-only. Revalidate the latest CLI capability at
// F9 BEFORE any game-window inputs, while preserving a deferred queue entry.
internal static class AlteringQueueStartGuard
{
    internal static void EnsureStartable(
        IReadOnlyList<AlteringPlan> plans,
        IReadOnlyList<AlteringRecipe> recipes,
        IReadOnlyList<AlteringWork>? liveWorks = null)
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
            {
                // queue_full describes occupied *existing* game slots, not a
                // broken recipe. Let the established supervisor manage/await
                // those works only when a fresh read-only CLI work snapshot
                // proves that the exact target facility is fully occupied.
                // A missing/partial/contradictory snapshot remains fail-closed.
                if (string.Equals(recipe.Reason, "queue_full", StringComparison.Ordinal))
                {
                    int occupied = liveWorks?.Count(x =>
                        string.Equals(x.FacilityName, plan.FacilityName, StringComparison.Ordinal)) ?? -1;
                    bool fullSelectedLane = liveWorks is not null &&
                        plans.Select(p => p.FacilityName)
                            .Distinct(StringComparer.Ordinal)
                            .Any(f => liveWorks.Count(w =>
                                string.Equals(w.FacilityName, f, StringComparison.Ordinal)) == 7);
                    if (occupied == 7 || fullSelectedLane)
                        continue; // A full selected lane must drain before another lane registers.
                    throw new InvalidOperationException(
                        $"{plan.DisplayName}: CLI Reason=queue_full 이지만 {plan.FacilityName} " +
                        $"실제 대기열은 {(occupied < 0 ? "조회되지 않음" : $"{occupied}/7칸")}입니다. " +
                        "선택한 시설에 7칸 가득 찬 대기열 증거가 없어 입력 없이 안전 정지합니다.");
                }
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: CLI가 현재 가공 불가로 응답하여 F9 시작을 보류합니다. " +
                    $"Reason={recipe.Reason ?? "unknown"} · Missing=없음 · 작업 목록을 유지하고 가공 조건을 다시 조회하세요.");
            }
            // Concrete shortages remain eligible for the existing recursive
            // material resolver, not an unsafe unconditional game input.
        }
    }
}
