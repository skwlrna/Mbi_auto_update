using DungeonVisionBot;

namespace FishingAutomation;

internal sealed record CraftingProgress(
    long ConfirmedQuantity,
    int TargetQuantity,
    int CompletedCrafts,
    int RequiredCrafts,
    string Stage)
{
    internal string Summary(string displayName)
        => $"{displayName} {ConfirmedQuantity:N0}/{TargetQuantity:N0} · 제작 {CompletedCrafts}/{RequiredCrafts} · {Stage}";
}

internal sealed class CraftingAutomation
{
    private readonly CraftingCliData _data;
    private readonly GatheringCliData _gathering;
    private readonly ICraftingScreen _screen;
    private readonly RecursiveAlteringSupplyResolver _alteringResolver;
    private readonly IAlteringData? _altering;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal event Action<string>? Log;
    internal event Action<CraftingProgress>? Progress;
    internal long Gained { get; private set; }
    internal int CompletedCrafts { get; private set; }

    internal CraftingAutomation(
        CraftingCliData data,
        GatheringCliData gathering,
        ICraftingScreen screen,
        RecursiveAlteringSupplyResolver alteringResolver,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IAlteringData? altering = null)
    {
        _data = data;
        _gathering = gathering;
        _screen = screen;
        _alteringResolver = alteringResolver;
        _altering = altering;
        _delay = delay ?? Task.Delay;
    }

    internal async Task RunAsync(CraftingPlan plan, CancellationToken ct)
    {
        plan.Validate();
        Gained = 0;
        CompletedCrafts = 0;

        var exact = await _data.ExactAsync(plan.DisplayName, ct);
        plan = plan with { ProducedPerCraft = exact.ProducedPerCraft };
        plan.Validate();

        long baseline = await _data.ItemCountAsync(plan.DisplayName, ct);
        long lastObserved = baseline;
        int requiredCrafts = plan.RequiredCrafts;

        Log?.Invoke(
            $"[제작] 시작 · {(plan.Category == CraftingCategory.Food ? "음식" : "아이템")} · " +
            $"{plan.DisplayName} 추가 {plan.TargetQuantity}개 · 1회 {plan.ProducedPerCraft}개 · 최대 10회 퀘스트");

        while (Gained < plan.TargetQuantity)
        {
            ct.ThrowIfCancellationRequested();

            long currentOutput = await _data.ItemCountAsync(plan.DisplayName, ct);
            if (currentOutput < lastObserved)
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 보유량이 제작 중 감소했습니다: 이전 {lastObserved} / 현재 {currentOutput}. " +
                    "목표 수량을 안전하게 추적할 수 없어 정지합니다.");
            lastObserved = currentOutput;
            Gained = currentOutput - baseline;
            if (Gained >= plan.TargetQuantity)
                break;

            long remainingOutput = plan.TargetQuantity - Gained;
            int remainingCrafts = CraftingQueries.RequiredCrafts(remainingOutput, plan.ProducedPerCraft);
            int batchCrafts = CraftingQueries.NextBatchCrafts(remainingCrafts);
            int beforeCompletedCrafts = CompletedCrafts;

            Progress?.Invoke(new(
                Gained, plan.TargetQuantity, CompletedCrafts, requiredCrafts,
                $"다음 {batchCrafts}회 퀘스트 준비"));

            await _screen.CreateQuestAsync(plan, batchCrafts, ct);
            await ResolveQuestMaterialsAsync(plan, ct);

            long beforeCraft = await _data.ItemCountAsync(plan.DisplayName, ct);
            await _screen.ReturnToStationAndCraftAsync(plan, batchCrafts, ct);

            long expectedMinimum = checked((long)batchCrafts * plan.ProducedPerCraft);
            long afterCraft = await WaitForOutputIncreaseAsync(
                plan.DisplayName, beforeCraft, expectedMinimum, ct);

            CompletedCrafts += batchCrafts;
            lastObserved = afterCraft;
            Gained = afterCraft - baseline;
            Log?.Invoke(
                $"[제작] 배치 완료 · {plan.DisplayName} {batchCrafts}회 · " +
                $"이번 +{afterCraft - beforeCraft} · 전체 +{Gained}/{plan.TargetQuantity}");

            Progress?.Invoke(new(
                Math.Min(Gained, plan.TargetQuantity),
                plan.TargetQuantity,
                CompletedCrafts,
                requiredCrafts,
                "배치 완료"));

            if (CompletedCrafts <= beforeCompletedCrafts)
                throw new InvalidOperationException("제작 배치 진행 수량이 증가하지 않아 정지합니다.");
        }

        long final = await _data.ItemCountAsync(plan.DisplayName, ct);
        long finalGain = final - baseline;
        if (finalGain < plan.TargetQuantity)
            throw new InvalidOperationException(
                $"제작 종료 시 실제 재고 증가량이 목표보다 적습니다: +{finalGain}/{plan.TargetQuantity}");

        Gained = finalGain;
        Progress?.Invoke(new(
            plan.TargetQuantity, plan.TargetQuantity, CompletedCrafts, requiredCrafts, "완료"));
        Log?.Invoke($"[제작] 목표 완료 · {plan.DisplayName} +{finalGain}개 · 초과 생산 허용");
    }

    private async Task ResolveQuestMaterialsAsync(CraftingPlan plan, CancellationToken ct)
    {
        string? previousSignature = null;
        int unchanged = 0;

        for (int pass = 1; pass <= 30; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var deficits = await _screen.ReadQuestDeficitsAsync(plan, ct);
            if (deficits.Count == 0)
            {
                Log?.Invoke("[제작] 현재 퀘스트의 부족 재료 없음 · 제작대 복귀 준비");
                await _screen.CloseOverlayAsync(ct);
                return;
            }

            string signature = string.Join("|", deficits.Select(x =>
                $"{x.DisplayName}:{x.Current}/{x.Required}"));
            if (signature == previousSignature) unchanged++;
            else unchanged = 0;
            previousSignature = signature;
            if (unchanged >= 3)
                throw new InvalidOperationException(
                    "제작 퀘스트 부족 재료 상태가 3회 연속 변하지 않아 중복 입력 없이 정지합니다.");

            var deficit = deficits[0];
            long shortage = Math.Max(0, deficit.Required - deficit.Current);
            if (shortage == 0)
                continue;

            var gatherableCatalog = await _gathering.CatalogAsync(ct);
            var gatherable = gatherableCatalog.SingleOrDefault(x =>
                string.Equals(x.DisplayName, deficit.DisplayName, StringComparison.Ordinal));

            bool intermediate = await IsKnownAlteringOutputAsync(deficit.DisplayName, ct);

            // Direct materials use the crafting quest route. Quest-only materials
            // such as insects may not appear in get_gatherable_items, so anything
            // that is not a known processing output also stays on the quest path.
            if (gatherable is not null || !intermediate)
            {
                if (gatherable is not null && !gatherable.ToolOk)
                    throw new InvalidOperationException(
                        $"{deficit.DisplayName} 채집 도구가 없거나 내구도가 부족합니다.");

                Log?.Invoke(
                    $"[제작] 퀘스트 직접 확보 재료 · {deficit.DisplayName} {deficit.Current}/{deficit.Required} · " +
                    (gatherable is null
                        ? "독립 채집 미지원/퀘스트 전용 재료도 추천 획득처로 처리"
                        : "제작 퀘스트 추천 획득처 사용"));
                await _screen.GatherQuestDeficitAsync(deficit, ct);
                continue;
            }

            // Confirmed intermediate processing output: make only the deficit.
            await _screen.CloseOverlayAsync(ct);
            Log?.Invoke(
                $"[제작] 중간 가공 재료 · {deficit.DisplayName} 부족 {shortage}개 · 자동 가공 연결");
            await _alteringResolver.ResolveExternalAsync(deficit.DisplayName, shortage, ct);
            Log?.Invoke($"[제작] 중간 가공 완료 · {deficit.DisplayName} 재확인");
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }

        throw new InvalidOperationException("제작 퀘스트 재료 해결 반복 한도를 초과했습니다.");
    }

    private async Task<bool> IsKnownAlteringOutputAsync(
        string displayName,
        CancellationToken ct)
    {
        if (_altering is null)
            return false;

        var recipes = await _altering.RecipesAsync(ct);
        return recipes.Any(recipe =>
            string.Equals(recipe.DisplayName, displayName, StringComparison.Ordinal) ||
            string.Equals(
                System.Text.RegularExpressions.Regex.Replace(
                    recipe.DisplayName, @"\([^()]*\)$", "").Trim(),
                displayName,
                StringComparison.Ordinal));
    }

    private async Task<long> WaitForOutputIncreaseAsync(
        string displayName,
        long before,
        long expectedMinimum,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(25);
        long last = before;
        int stable = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _data.ItemCountAsync(displayName, ct);
            if (current < before)
                throw new InvalidOperationException(
                    $"{displayName} 제작 직후 보유량이 감소해 결과를 확정할 수 없습니다.");
            if (current != last)
            {
                last = current;
                stable = 0;
            }

            if (current - before >= expectedMinimum)
            {
                stable++;
                if (stable >= 2)
                    return current;
            }
            else stable = 0;

            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }

        long final = await _data.ItemCountAsync(displayName, ct);
        throw new InvalidOperationException(
            $"{displayName} 제작 완료 화면 이후 실제 재고 증가를 확인하지 못했습니다: " +
            $"+{final - before} / 최소 +{expectedMinimum}");
    }
}
