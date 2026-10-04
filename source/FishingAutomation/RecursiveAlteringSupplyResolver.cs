namespace FishingAutomation;

internal sealed class RecursiveAlteringSupplyResolver : IAlteringSupplyResolver
{
    private readonly IAlteringData _altering;
    private readonly IGatheringData _gathering;
    private readonly IAlteringScreen _alteringScreen;
    private readonly IGatheringScreen _gatheringScreen;
    private readonly int _maxDepth;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly int _verificationAttempts;
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    internal event Action<string>? Log;

    internal RecursiveAlteringSupplyResolver(
        IAlteringData altering,
        IGatheringData gathering,
        IAlteringScreen alteringScreen,
        IGatheringScreen gatheringScreen,
        int maxDepth = 8,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int verificationAttempts = 120)
    {
        _altering = altering;
        _gathering = gathering;
        _alteringScreen = alteringScreen;
        _gatheringScreen = gatheringScreen;
        _maxDepth = Math.Clamp(maxDepth, 1, 16);
        _delay = delay;
        _verificationAttempts = Math.Clamp(verificationAttempts, 1, 120);
    }

    public async Task ResolveAsync(
        AlteringPlan parentPlan,
        AlteringRecipe blockedRecipe,
        int remainingWorks,
        CancellationToken ct)
    {
        if (remainingWorks <= 0) return;
        if (blockedRecipe.MissingIngredients.Count == 0)
            throw new InvalidOperationException($"부족 재료 목록이 없어 자동으로 해결하지 않습니다: {blockedRecipe.Reason ?? "unknown"}");

        // Pre-plan every raw shortage that can be proven from the current missing-
        // ingredient tree, then gather those raws in one field session. The planner
        // is conservative: if the CLI does not expose an ingredient because it is
        // currently sufficient, the normal recursive fallback below will re-check
        // later rather than inventing recipe data.
        var multiGather = await BuildMultiGatheringPlanAsync(
            parentPlan, blockedRecipe, remainingWorks, ct);
        if (multiGather.Count > 0)
        {
            if (_alteringScreen is not IAlteringFieldExitScreen fieldExit)
                throw new InvalidOperationException(
                    "다중 채집 전에 가공 UI를 안전하게 종료할 수 없는 화면 구현입니다.");

            Log?.Invoke(
                $"[재료 해결] 다중 채집 준비 · {parentPlan.DisplayName} · " +
                string.Join(", ", multiGather.Select(x => $"{x.DisplayName} 목표 {x.TargetTotal}")));
            await fieldExit.ExitToFieldAsync(ct);
            Log?.Invoke("[재료 해결] 다중 채집 전 가공 UI 종료 · 일반 필드 복귀 확인");

            var coordinator = new MultiGatheringCoordinator(
                _gathering, _gatheringScreen, _delay, _verificationAttempts);
            coordinator.Log += text => Log?.Invoke(text);
            await coordinator.RunAsync(multiGather, ct);
        }

        foreach (var missing in blockedRecipe.MissingIngredients)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _altering.ItemCountAsync(missing.DisplayName, ct);
            long requiredTotal = checked(missing.Required * (long)remainingWorks);
            long deficit = Math.Max(0, requiredTotal - current);
            if (deficit == 0) continue;

            Log?.Invoke($"[재료 해결] {parentPlan.DisplayName} -> {missing.DisplayName} 부족 {deficit}개 " +
                        $"(현재 {current} / 남은 작업 기준 {requiredTotal})");
            await ResolveItemAsync(missing.DisplayName, deficit, parentPlan, 0, ct);
        }
    }

    internal Task ResolveExternalAsync(string itemName, long quantity, CancellationToken ct)
        => ResolveItemAsync(itemName, quantity, null, 0, ct);

    private async Task ResolveItemAsync(
        string itemName,
        long quantity,
        AlteringPlan? sourceRecipe,
        int depth,
        CancellationToken ct)
    {
        if (quantity <= 0) return;
        if (quantity > 1_000_000)
            throw new InvalidOperationException($"재료 자동 확보 수량이 안전 한도를 넘었습니다: {itemName} {quantity}개");
        if (depth >= _maxDepth)
            throw new InvalidOperationException($"재료 단계가 {_maxDepth}단계를 넘어 자동 처리를 중단합니다: {itemName}");
        if (!_active.Add(itemName))
            throw new InvalidOperationException($"재료 순환 의존성이 감지되어 정지합니다: {string.Join(" -> ", _active)} -> {itemName}");

        try
        {
            var catalog = await _gathering.CatalogAsync(ct);
            var gatherable = catalog.SingleOrDefault(x => x.DisplayName == itemName);
            if (gatherable is not null)
            {
                if (LivingSkillGatheringCatalog.IsQuestOnlyMaterial(itemName))
                    throw new InvalidOperationException(
                        $"{itemName}은(는) 곤충채집 퀘스트 전용 재료라 자동가공의 대량채집에서 직접 처리하지 않습니다. 제작 퀘스트 경로에서만 처리합니다.");
                if (!gatherable.ToolOk)
                    throw new InvalidOperationException($"{itemName} 채집 도구가 없거나 내구도가 부족합니다.");

                if (sourceRecipe is null)
                    throw new InvalidOperationException(
                        $"{itemName}은 직접 채집 재료입니다. 제작에서는 제작 퀘스트 채집 경로를 사용해야 합니다.");

                if (_alteringScreen is not IAlteringFieldExitScreen fieldExit)
                    throw new InvalidOperationException(
                        $"{itemName} 자동 채집 전에 가공 UI를 안전하게 종료할 수 없습니다.");

                Log?.Invoke($"[재료 해결] {itemName} 단일 보충 채집 전 가공 UI 종료 · 일반 필드 복귀 확인");
                await fieldExit.ExitToFieldAsync(ct);

                long currentBeforeFallback = await _gathering.ItemCountAsync(itemName, ct);
                long fallbackTargetTotal = checked(currentBeforeFallback + quantity);
                var coordinator = new MultiGatheringCoordinator(
                    _gathering, _gatheringScreen, _delay, _verificationAttempts);
                coordinator.Log += text => Log?.Invoke(text);
                await coordinator.RunAsync(
                    new[]
                    {
                        new MultiGatheringRequest(
                            itemName,
                            fallbackTargetTotal,
                            sourceRecipe with { AllowPaidButton = false })
                    },
                    ct);
                return;
            }

            var recipes = await _altering.RecipesAsync(ct);
            var candidates = recipes.Where(x =>
                x.DisplayName == itemName ||
                OutputName(x.DisplayName) == itemName).ToArray();

            if (candidates.Length == 0)
                throw new InvalidOperationException($"{itemName}은(는) 자동 채집 품목도 가공 가능한 중간 재료도 아닙니다.");

            AlteringRecipe producer;
            var exact = candidates.Where(x => x.DisplayName == itemName).ToArray();
            if (exact.Length == 1) producer = exact[0];
            else if (exact.Length > 1)
                throw new InvalidOperationException($"{itemName} 가공 제법이 여러 개라 자동 선택하지 않습니다.");
            else if (candidates.Length == 1) producer = candidates[0];
            else
                throw new InvalidOperationException($"{itemName}을 만드는 가공 제법이 여러 개라 자동 선택하지 않습니다.");

            int duplicateCount = recipes.Count(x => x.DisplayName == producer.DisplayName);
            if (duplicateCount != 1)
                throw new InvalidOperationException($"{producer.DisplayName} 동일 이름 제법이 여러 개라 자동 선택하지 않습니다.");

            string facility = ResolveFacility(producer, itemName);
            var subPlan = new AlteringPlan(
                facility,
                producer.DisplayName,
                checked((int)quantity),
                producer.ProducedPerWork,
                false);

            if (!producer.Alterable)
            {
                if (producer.MissingIngredients.Count == 0)
                    throw new InvalidOperationException(
                        $"{producer.DisplayName} 가공 조건을 자동으로 해결할 수 없습니다: {producer.Reason ?? "unknown"}");

                Log?.Invoke($"[재료 해결] {itemName}은 중간 가공품 · 하위 재료 확인");
                foreach (var missing in producer.MissingIngredients)
                {
                    long current = await _altering.ItemCountAsync(missing.DisplayName, ct);
                    long requiredTotal = checked(missing.Required * (long)subPlan.RequiredWorks);
                    long deficit = Math.Max(0, requiredTotal - current);
                    if (deficit > 0)
                        await ResolveItemAsync(missing.DisplayName, deficit, subPlan, depth + 1, ct);
                }

                recipes = await _altering.RecipesAsync(ct);
                producer = recipes.SingleOrDefault(x => x.DisplayName == subPlan.DisplayName)
                    ?? throw new InvalidOperationException($"{subPlan.DisplayName} 제법을 재확인하지 못했습니다.");
                if (!producer.Alterable)
                    throw new InvalidOperationException($"{subPlan.DisplayName} 하위 재료 확보 후에도 가공 불가 상태입니다.");
            }

            long outputBefore = await _altering.ItemCountAsync(subPlan.OutputName, ct);
            Log?.Invoke($"[재료 해결] 중간 가공 시작 · {subPlan.DisplayName} 추가 {quantity}개 · 정령의 날개 0개 원칙");
            var nested = new AlteringAutomation(
                _altering,
                _alteringScreen,
                _delay,
                _verificationAttempts,
                this);
            nested.Log += text => Log?.Invoke(text);
            await nested.RunAsync(subPlan, ct);
            long outputAfter = await _altering.ItemCountAsync(subPlan.OutputName, ct);
            if (outputAfter - outputBefore < quantity)
                throw new InvalidOperationException(
                    $"{subPlan.OutputName} 중간 가공 후 수량 검증 실패: +{outputAfter - outputBefore} / 필요 +{quantity}");
            Log?.Invoke($"[재료 해결] 중간 가공 완료 · {subPlan.OutputName} +{outputAfter - outputBefore}개");
        }
        finally
        {
            _active.Remove(itemName);
        }
    }

    private async Task<IReadOnlyList<MultiGatheringRequest>> BuildMultiGatheringPlanAsync(
        AlteringPlan parentPlan,
        AlteringRecipe blockedRecipe,
        int remainingWorks,
        CancellationToken ct)
    {
        var catalog = await _gathering.CatalogAsync(ct);
        var recipes = await _altering.RecipesAsync(ct);
        var requests = new List<MultiGatheringRequest>();
        var virtualAvailable = new Dictionary<string, long>(StringComparer.Ordinal);
        var planningPath = new HashSet<string>(StringComparer.Ordinal);

        async Task<long> TakeAvailableAsync(string itemName, long demand)
        {
            if (!virtualAvailable.TryGetValue(itemName, out long available))
            {
                available = await _altering.ItemCountAsync(itemName, ct);
                virtualAvailable[itemName] = available;
            }

            long used = Math.Min(available, demand);
            virtualAvailable[itemName] = available - used;
            return demand - used;
        }

        async Task PlanItemAsync(
            string itemName,
            long demand,
            AlteringPlan sourceRecipe,
            int depth)
        {
            if (demand <= 0) return;
            if (depth >= _maxDepth)
                throw new InvalidOperationException(
                    $"다중 채집 계획 중 재료 단계가 {_maxDepth}단계를 넘었습니다: {itemName}");

            long shortage = await TakeAvailableAsync(itemName, demand);
            if (shortage <= 0) return;

            var gatherable = catalog.SingleOrDefault(x => x.DisplayName == itemName);
            if (gatherable is not null)
            {
                if (LivingSkillGatheringCatalog.IsQuestOnlyMaterial(itemName))
                    return; // Existing resolver will report the quest-only rule.
                if (!gatherable.ToolOk)
                    throw new InvalidOperationException($"{itemName} 채집 도구가 없거나 내구도가 부족합니다.");

                long currentNow = await _altering.ItemCountAsync(itemName, ct);
                requests.Add(new MultiGatheringRequest(
                    itemName,
                    checked(currentNow + shortage),
                    sourceRecipe with { AllowPaidButton = false }));
                return;
            }

            if (!planningPath.Add(itemName))
                throw new InvalidOperationException(
                    $"다중 채집 계획에서 재료 순환 의존성이 감지되었습니다: {itemName}");
            try
            {
                var candidates = recipes.Where(x =>
                    x.DisplayName == itemName ||
                    OutputName(x.DisplayName) == itemName).ToArray();
                if (candidates.Length == 0)
                    return; // Existing resolver gives the authoritative error later.

                AlteringRecipe? producer = null;
                var exact = candidates.Where(x => x.DisplayName == itemName).ToArray();
                if (exact.Length == 1) producer = exact[0];
                else if (exact.Length == 0 && candidates.Length == 1) producer = candidates[0];
                if (producer is null || producer.MissingIngredients.Count == 0)
                    return;

                string? facility = AlteringFacilityResolver.Resolve(producer);
                if (facility is null)
                    return;

                var subPlan = new AlteringPlan(
                    facility,
                    producer.DisplayName,
                    checked((int)shortage),
                    producer.ProducedPerWork,
                    false);

                foreach (var missing in producer.MissingIngredients)
                {
                    long childDemand = checked(missing.Required * (long)subPlan.RequiredWorks);
                    await PlanItemAsync(
                        missing.DisplayName,
                        childDemand,
                        subPlan,
                        depth + 1);
                }
            }
            finally
            {
                planningPath.Remove(itemName);
            }
        }

        foreach (var missing in blockedRecipe.MissingIngredients)
        {
            ct.ThrowIfCancellationRequested();
            long demand = checked(missing.Required * (long)remainingWorks);
            await PlanItemAsync(
                missing.DisplayName,
                demand,
                parentPlan,
                0);
        }

        // The virtual inventory ledger applies the current stock only once across
        // sibling branches. Duplicate raw leaves therefore become one summed request.
        var merged = new List<MultiGatheringRequest>();
        foreach (var group in requests.GroupBy(x => x.DisplayName, StringComparer.Ordinal))
        {
            long current = await _altering.ItemCountAsync(group.Key, ct);
            long additional = checked(group.Sum(x => Math.Max(0, x.TargetTotal - current)));
            merged.Add(new MultiGatheringRequest(
                group.Key,
                checked(current + additional),
                group.First().SourceRecipe));
        }
        return merged.ToArray();
    }

    private static string OutputName(string displayName)
        => System.Text.RegularExpressions.Regex.Replace(displayName, @"\([^()]*\)$", "").Trim();

    private static string ResolveFacility(AlteringRecipe recipe, string outputName)
        => AlteringFacilityResolver.Resolve(recipe)
            ?? throw new InvalidOperationException(
                $"{outputName}의 가공 시설을 CLI 정보와 제법 이름에서 확인할 수 없습니다.");
}
