namespace FishingAutomation;

internal sealed record MultiAlteringSupplyPreflight(
    AlteringPlan Plan,
    AlteringRecipe Recipe,
    int RemainingWorks);

// V3.2.42: read-only *partial* forecast. MissingIngredients is NOT a
// full bill of materials; it may be empty because of global queue_full.
// Summed numbers must never be used as production/input authorization.
internal sealed record MultiAlteringMaterialForecastItem(
    string DisplayName, long EvidencedDemand, long FreshStock, long Shortage,
    string AcquisitionRoute, int SelectedRecipes);

internal static class MultiAlteringMaterialForecastPolicy
{
    // Diagnostic-only route classification, not a permission to click or gather.
    internal const string SupportedBulk = "verified_bulk_screen";
    internal const string MissingTool = "gathering_tool_unavailable";
    internal const string QuestOnly = "quest_only_not_bulk_gatherable";
    internal const string BulkUnverified = "bulk_route_unverified";
    internal const string Ambiguous = "ambiguous_gathering_route";
    internal const string Unverified = "acquisition_route_unverified";

    internal static async Task<IReadOnlyList<MultiAlteringMaterialForecastItem>>
        ForecastAsync(
            IReadOnlyList<MultiAlteringSupplyPreflight> shortages,
            IReadOnlyList<GatherableItem> gatheringCatalog,
            Func<string, CancellationToken, Task<long>> readFreshStock,
            CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shortages);
        ArgumentNullException.ThrowIfNull(gatheringCatalog);
        ArgumentNullException.ThrowIfNull(readFreshStock);

        // Only explicitly evidenced material lines enter the forecast.
        // Reading the existing plan selection twice must not duplicate demand.
        var seenPlans = new HashSet<string>(StringComparer.Ordinal);
        var demandByItem = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeKeysByItem = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var request in shortages)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);
            request.Plan.Validate();
            if (request.RemainingWorks < 0 ||
                request.RemainingWorks > request.Plan.RequiredWorks ||
                request.Recipe.DisplayName != request.Plan.DisplayName ||
                request.Recipe.ProducedPerWork != request.Plan.ProducedPerWork ||
                AlteringFacilityResolver.Resolve(request.Recipe) != request.Plan.FacilityName)
                throw new InvalidDataException("재료 예측 제법/남은 작업 증거 불일치");

            string planKey = MultiAlteringBatchStore.Key(request.Plan);
            if (!seenPlans.Add(planKey))
                throw new InvalidDataException("재료 예측 품목 중복 · 부족 수량 중복 계산 방지");
            if (request.RemainingWorks == 0) continue;

            foreach (var missing in request.Recipe.MissingIngredients)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(missing.DisplayName) ||
                    missing.Required < 0 || missing.Owned < 0)
                    throw new InvalidDataException("재료 예측 CLI 부족 목록에 잘못된 수량/품목이 있습니다");
                if (missing.Required == 0) continue;
                long required = checked(missing.Required * request.RemainingWorks);
                demandByItem[missing.DisplayName] = checked(
                    demandByItem.GetValueOrDefault(missing.DisplayName) + required);
                if (!recipeKeysByItem.TryGetValue(missing.DisplayName, out var usedBy))
                {
                    usedBy = new HashSet<string>(StringComparer.Ordinal);
                    recipeKeysByItem.Add(missing.DisplayName, usedBy);
                }
                usedBy.Add(planKey);
            }
        }

        // One *fresh* CLI stock read per distinct material. Owned fields from
        // a selected or masked UI snapshot are never inventory authority.
        var results = new List<MultiAlteringMaterialForecastItem>();
        foreach (var item in demandByItem.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            long live = await readFreshStock(item.Key, ct);
            if (live < 0)
                throw new InvalidDataException("재료 예측 음수 재고 · 과거 재고로 대체 금지");
            long shortage = Math.Max(0, item.Value - live);
            var routes = gatheringCatalog.Where(x =>
                string.Equals(x.DisplayName, item.Key, StringComparison.Ordinal)).ToArray();
            string route = LivingSkillGatheringCatalog.IsQuestOnlyMaterial(item.Key)
                ? QuestOnly
                : routes.Length > 1
                    ? Ambiguous
                    : routes.Length == 0
                        ? Unverified // May still have a recursive producer.
                        : !routes[0].ToolOk
                            ? MissingTool
                            : LivingSkillGatheringCatalog.TryResolveBulk(item.Key, out _)
                                ? SupportedBulk : BulkUnverified;
            results.Add(new MultiAlteringMaterialForecastItem(
                item.Key, item.Value, live, shortage, route,
                recipeKeysByItem[item.Key].Count));
        }

        // Useful attention order ONLY. The existing F9 user-selected recipe
        // sequence and the guarded resolver's virtual stock ledger are intact.
        return results
            .OrderByDescending(x => x.Shortage > 0)
            .ThenByDescending(x => x.AcquisitionRoute == SupportedBulk)
            .ThenByDescending(x => x.Shortage)
            .ThenBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }
}

internal static class MultiAlteringMaterialPreflightPolicy
{
    internal static bool CanRun(bool hasResumableSession, bool hasSelectedFacilityWorks)
        => !hasResumableSession && !hasSelectedFacilityWorks;

    // Evidence summary is diagnostic only. "No known shortage" is not a
    // complete bill of materials, and queue_full never proves that stock is
    // sufficient. Runtime resolver and input guards remain authoritative.
    internal static IReadOnlyList<string> DescribeSelectedEvidence(
        IReadOnlyList<AlteringPlan> plans,
        IReadOnlyList<AlteringRecipe> freshRecipes,
        IReadOnlyList<AlteringWork> works)
    {
        var result = new List<string>();
        foreach (var plan in plans)
        {
            var recipe = freshRecipes.Where(x => x.DisplayName == plan.DisplayName)
                .ElementAtOrDefault(plan.RecipeOrdinal - 1);
            int occupied = works.Count(w => w.FacilityName == plan.FacilityName);
            if (recipe is null)
            {
                result.Add($"{plan.DisplayName} · 제법 없음 · 등록 금지");
                continue;
            }
            string state = recipe.MissingIngredients.Count > 0
                ? "CLI 확정 부족 " + string.Join(", ", recipe.MissingIngredients
                    .Take(8).Select(x => $"{x.DisplayName}({x.Owned}/{x.Required})"))
                : string.Equals(recipe.Reason, "queue_full", StringComparison.Ordinal)
                    ? "queue_full · 부족 재료 정보 가려짐/미확정"
                    : recipe.Alterable
                        ? "첫 작업 가공 가능 · 이후 재료는 실행 직전 재검증"
                        : $"가공 보류 Reason={recipe.Reason ?? "unknown"} · 부족 정보 없음";
            result.Add($"{plan.FacilityName} · {plan.DisplayName} · {occupied}/7칸 · {state}");
        }
        return result;
    }

    // Global F9 free-gather preflight is independent of the order in which
    // facilities are serviced. It never receives jobs or registers production.
    // Existing per-recipe preflight is retained for later rechecks.
    internal static IReadOnlyList<MultiAlteringSupplyPreflight> SelectInitialKnownShortages(
        IReadOnlyList<AlteringPlan> plans,
        IReadOnlyList<AlteringRecipe> freshRecipes,
        IReadOnlyList<MultiAlteringSupplyPreflight> queuedEvidence)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(freshRecipes);
        ArgumentNullException.ThrowIfNull(queuedEvidence);
        var selected = new List<MultiAlteringSupplyPreflight>();

        foreach (var plan in plans)
        {
            plan.Validate();
            var fresh = freshRecipes.Where(x =>
                string.Equals(x.DisplayName, plan.DisplayName, StringComparison.Ordinal))
                .ElementAtOrDefault(plan.RecipeOrdinal - 1);
            if (fresh is null || fresh.ProducedPerWork != plan.ProducedPerWork ||
                AlteringFacilityResolver.Resolve(fresh) != plan.FacilityName)
                throw new InvalidOperationException(
                    $"F9 선행채집 제법 확인 실패: {plan.DisplayName}. 새로고침 후 재확인하세요.");

            // Leather raw-material shortage is terminal for this recipe; the
            // existing F9 engine handles it without auto-gathering. Exclude it
            // even when prior evidence is masked by a global queue_full.
            if (string.Equals(plan.FacilityName, "가죽 가공 시설",
                StringComparison.Ordinal))
                continue;

            // A new read-only CLI shortage is preferred over any UI evidence.
            if (fresh.MissingIngredients.Count > 0)
            {
                selected.Add(new MultiAlteringSupplyPreflight(
                    plan, fresh, plan.RequiredWorks));
                continue;
            }

            // Some CLI versions replace the shortage reason with queue_full
            // while another selected facility has seven occupied slots. Only
            // an exact prior *user-selected* shortage snapshot may fill this
            // gap, and only when the fresh CLI confirms queue_full.
            if (fresh.Alterable ||
                !string.Equals(fresh.Reason, "queue_full", StringComparison.Ordinal))
                continue;

            var prior = queuedEvidence.SingleOrDefault(x =>
                x.Plan.FacilityName == plan.FacilityName &&
                x.Plan.DisplayName == plan.DisplayName &&
                x.Plan.RecipeOrdinal == plan.RecipeOrdinal &&
                x.Plan.ProducedPerWork == plan.ProducedPerWork &&
                x.Plan.TargetQuantity == plan.TargetQuantity);

            if (prior is not null &&
                !prior.Recipe.Alterable &&
                string.Equals(prior.Recipe.Reason, "not_enough_ingredient", StringComparison.Ordinal) &&
                prior.Recipe.DisplayName == fresh.DisplayName &&
                prior.Recipe.ProducedPerWork == fresh.ProducedPerWork &&
                AlteringFacilityResolver.Resolve(prior.Recipe) == plan.FacilityName &&
                prior.Recipe.MissingIngredients.Count > 0 &&
                prior.Recipe.MissingIngredients.All(x =>
                    !string.IsNullOrWhiteSpace(x.DisplayName) && x.Required > 0 && x.Owned >= 0))
                selected.Add(new MultiAlteringSupplyPreflight(
                    plan, prior.Recipe, plan.RequiredWorks));
        }

        return selected;
    }
}

internal sealed class RecursiveAlteringSupplyResolver : IAlteringSupplyResolver
{
    private readonly IAlteringData _altering;
    private readonly IGatheringData _gathering;
    private readonly IAlteringScreen _alteringScreen;
    private readonly IGatheringScreen _gatheringScreen;
    private readonly int _maxDepth;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly int _verificationAttempts;
    private readonly IAlteringDependencyScheduler? _dependencyScheduler;
    private readonly FacilityLaneState? _laneState;
    private readonly LifeSkillManagerState? _managerState;
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    internal event Action<string>? Log;

    internal RecursiveAlteringSupplyResolver(
        IAlteringData altering,
        IGatheringData gathering,
        IAlteringScreen alteringScreen,
        IGatheringScreen gatheringScreen,
        int maxDepth = 8,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int verificationAttempts = 120,
        IAlteringDependencyScheduler? dependencyScheduler = null,
        FacilityLaneState? laneState = null,
        LifeSkillManagerState? managerState = null)
    {
        _altering = altering;
        _gathering = gathering;
        _alteringScreen = alteringScreen;
        _gatheringScreen = gatheringScreen;
        _maxDepth = Math.Clamp(maxDepth, 1, 16);
        _delay = delay;
        _verificationAttempts = Math.Clamp(verificationAttempts, 1, 120);
        _dependencyScheduler = dependencyScheduler;
        _laneState = laneState;
        _managerState = managerState;
    }

    // The resolver does not decide where to move. It only reports a confirmed
    // transition back to the field to the shared facility manager. A failed
    // transition leaves the true location unknown, so discard stale onsite
    // authority before propagating the original error.
    private async Task ExitToFieldAndNotifyManagerAsync(
        IAlteringFieldExitScreen fieldExit,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await fieldExit.ExitToFieldAsync(ct);
        }
        catch
        {
            _laneState?.InvalidateOnsite(
                $"필드 이탈 진행 중 위치 미확정 · {reason}");
            throw;
        }

        _laneState?.InvalidateOnsite(
            $"실제 필드 복귀 확인 · {reason}");
        Log?.Invoke(
            $"[재료 해결] 필드 복귀 확인 · 중간관리자 현장확정 해제 · {reason}");
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

        if (string.Equals(parentPlan.FacilityName, "가죽 가공 시설", StringComparison.Ordinal))
        {
            // Read-only recursive check before any gathering, travel or
            // production. Never buy or gather 생가죽 tiers or tannin.
            var knownRecipes = await _altering.RecipesAsync(ct);
            var catalog = await _gathering.CatalogAsync(ct);
            var nextTicket = parentPlan with { TargetQuantity = parentPlan.ProducedPerWork };
            var hold = await MaterialSupplyRoutePolicy.FindHoldAsync(
                nextTicket, blockedRecipe, knownRecipes, catalog,
                _altering.ItemCountAsync, ct);
            if (hold is not null)
                throw new MaterialSupplyHoldException(hold);
        }

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
            await ExitToFieldAndNotifyManagerAsync(
                fieldExit,
                $"후속 다중 채집 · {parentPlan.DisplayName}",
                ct);
            Log?.Invoke("[재료 해결] 다중 채집 전 가공 UI 종료 · 일반 필드 복귀 확인");

            var coordinator = new MultiGatheringCoordinator(
                _gathering, _gatheringScreen, _delay, _verificationAttempts, _managerState);
            coordinator.Log += text => Log?.Invoke(text);
            await coordinator.RunAsync(multiGather, ct);
            _managerState?.BeginStage(LifeSkillStage.Altering,
                "다중 원재료 채집 완료 · 가공 복귀 시 재검증");
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
            // A leather batch may speculatively prepare up to seven parent
            // tickets. Only the next confirmed ticket is compulsory; stop a
            // child at a later genuine raw shortage and use verified partial
            // output instead of discarding the entire batch.
            long minimumDeficit = parentPlan.FacilityName == "가죽 가공 시설"
                ? Math.Max(1, missing.Required - current) : deficit;
            await ResolveItemAsync(
                missing.DisplayName, deficit, parentPlan, 0, ct, minimumDeficit);
        }
    }

    internal async Task PreGatherKnownShortagesAsync(
        IReadOnlyList<MultiAlteringSupplyPreflight> requests,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);

        // Guard the resolver entry point too: callers outside the F9 UI
        // must never pre-gather leather raw inputs before the shortage policy.
        var eligible = requests
            .Where(x => x.RemainingWorks > 0 &&
                x.Recipe.MissingIngredients.Count > 0 &&
                !string.Equals(x.Plan.FacilityName, "가죽 가공 시설",
                    StringComparison.Ordinal))
            .ToArray();
        if (eligible.Length == 0)
            return;

        foreach (var request in eligible)
            request.Plan.Validate();

        var multiGather = await BuildMultiGatheringPlanAsync(eligible, ct);
        if (multiGather.Count == 0)
        {
            Log?.Invoke(
                "[재료 해결] 전체 품목 통합 계획 · 현재 CLI가 확정한 원재료 부족분 없음 · " +
                "숨은 재료는 각 품목 실행 시 기존 재귀 검증으로 재확인");
            return;
        }

        if (_alteringScreen is not IAlteringFieldExitScreen fieldExit)
            throw new InvalidOperationException(
                "전체 품목 통합 채집 전에 가공 UI를 안전하게 종료할 수 없는 화면 구현입니다.");

        Log?.Invoke(
            $"[재료 해결] 전체 품목 통합 채집 준비 · {eligible.Length}종 확정 부족분 · " +
            string.Join(", ", multiGather.Select(x => $"{x.DisplayName} 목표 {x.TargetTotal}")));
        await ExitToFieldAndNotifyManagerAsync(
            fieldExit,
            "전체 품목 통합 선행채집",
            ct);
        Log?.Invoke("[재료 해결] 전체 품목 통합 채집 전 가공 UI 종료 · 일반 필드 복귀 확인");

        var coordinator = new MultiGatheringCoordinator(
            _gathering, _gatheringScreen, _delay, _verificationAttempts, _managerState);
        coordinator.Log += text => Log?.Invoke(text);
        await coordinator.RunAsync(multiGather, ct);
        _managerState?.BeginStage(LifeSkillStage.Altering,
            "통합 선행 채집 완료 · 가공 재검증");

        Log?.Invoke(
            "[재료 해결] 전체 품목 통합 채집 완료 · 각 품목 시작 직전 실제 재고/부족분을 다시 검증");
    }

    internal Task ResolveExternalAsync(string itemName, long quantity, CancellationToken ct)
        => ResolveItemAsync(itemName, quantity, null, 0, ct);

    private async Task ResolveItemAsync(
        string itemName,
        long quantity,
        AlteringPlan? sourceRecipe,
        int depth,
        CancellationToken ct,
        long? minimumQuantity = null)
    {
        if (quantity <= 0) return;
        long minimum = minimumQuantity ?? quantity;
        if (minimum <= 0 || minimum > quantity)
            throw new ArgumentOutOfRangeException(nameof(minimumQuantity));
        if (quantity > 1_000_000)
            throw new InvalidOperationException($"재료 자동 확보 수량이 안전 한도를 넘었습니다: {itemName} {quantity}개");
        if (LeatherRawMaterialPolicy.IsExternalRaw(itemName))
        {
            long observed = await _altering.ItemCountAsync(itemName, ct);
            // Defend the recursive entry point even when a producer exposes
            // a previously hidden shortage after the initial preflight.
            throw new MaterialSupplyHoldException(
                new(itemName, checked(observed + quantity), observed,
                    "external_leather_raw_missing"));
        }
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
                await ExitToFieldAndNotifyManagerAsync(
                    fieldExit,
                    $"재귀 후속 단일 채집 · {itemName}",
                    ct);

                long currentBeforeFallback = await _gathering.ItemCountAsync(itemName, ct);
                long fallbackTargetTotal = checked(currentBeforeFallback + quantity);
                var coordinator = new MultiGatheringCoordinator(
                    _gathering, _gatheringScreen, _delay, _verificationAttempts, _managerState);
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
                _managerState?.BeginStage(LifeSkillStage.Altering,
                    "재귀 원재료 채집 후 가공 복귀");
                return;
            }

            var recipes = await _altering.RecipesAsync(ct);
            var candidates = recipes.Where(x =>
                x.DisplayName == itemName ||
                OutputName(x.DisplayName) == itemName).ToArray();

            if (candidates.Length == 0)
                throw new InvalidOperationException($"{itemName}은(는) 자동 채집 품목도 가공 가능한 중간 재료도 아닙니다.");

            AlteringRecipe producer = SelectProducer(itemName, candidates);
            if (itemName == "철괴" &&
                producer.DisplayName == "철괴(철 광석)")
                Log?.Invoke("[재료 해결] 철괴 생산 제법 고정 · 철괴(철 광석) 사용 · 철괴(광석) 제외");

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
                        await ResolveItemAsync(
                            missing.DisplayName, deficit, subPlan, depth + 1, ct,
                            subPlan.FacilityName == "가죽 가공 시설"
                                ? Math.Max(1, missing.Required - current) : deficit);
                }

                recipes = await _altering.RecipesAsync(ct);
                producer = recipes.SingleOrDefault(x => x.DisplayName == subPlan.DisplayName)
                    ?? throw new InvalidOperationException($"{subPlan.DisplayName} 제법을 재확인하지 못했습니다.");
                if (!producer.Alterable)
                    throw new InvalidOperationException($"{subPlan.DisplayName} 하위 재료 확보 후에도 가공 불가 상태입니다.");
            }

            _managerState?.BeginStage(LifeSkillStage.Altering,
                "중간가공 " + subPlan.DisplayName);
            long outputBefore = _managerState is null
                ? await _altering.ItemCountAsync(subPlan.OutputName, ct)
                : await _managerState.ReadFreshCountAsync(
                    LifeSkillStage.Altering, subPlan.OutputName,
                    _altering.ItemCountAsync, ct);
            Log?.Invoke($"[재료 해결] 중간 가공 시작 · {subPlan.DisplayName} 추가 {quantity}개 · 정령의 날개 0개 원칙");

            if (_dependencyScheduler is not null)
            {
                Log?.Invoke(
                    $"[재료 해결] 중간 가공을 메인 시설 배치 스케줄러에 위임 · " +
                    $"{subPlan.DisplayName} · {subPlan.FacilityName.Replace(" 시설", "")}");
                await _dependencyScheduler.RunAsync(
                    subPlan,
                    outputBefore,
                    checked((int)quantity),
                    this,
                    ct,
                    minimumAdditionalQuantity: checked((int)minimum));
            }
            else
            {
                // Single-altering keeps the proven recursive fallback. Multi-altering
                // injects a dependency scheduler and never enters this independent
                // full-run path.
                var nested = new AlteringAutomation(
                    _altering,
                    _alteringScreen,
                    _delay,
                    _verificationAttempts,
                    this);
                nested.Log += text => Log?.Invoke(text);
                await nested.RunAsync(subPlan, ct);
            }

            long outputAfter = _managerState is null
                ? await _altering.ItemCountAsync(subPlan.OutputName, ct)
                : await _managerState.ReadFreshCountAsync(
                    LifeSkillStage.Altering, subPlan.OutputName,
                    _altering.ItemCountAsync, ct);
            if (outputAfter - outputBefore < minimum)
                throw new InvalidOperationException(
                    $"{subPlan.OutputName} 중간 가공 후 최소 수량 검증 실패: " +
                    $"+{outputAfter - outputBefore} / 최소 +{minimum} / 묶음 목표 +{quantity}");
            Log?.Invoke($"[재료 해결] 중간 가공 수령·재고 확인 · {subPlan.OutputName} " +
                $"+{outputAfter - outputBefore}개 / 묶음 목표 +{quantity}개");
        }
        finally
        {
            _active.Remove(itemName);
        }
    }

    private Task<IReadOnlyList<MultiGatheringRequest>> BuildMultiGatheringPlanAsync(
        AlteringPlan parentPlan,
        AlteringRecipe blockedRecipe,
        int remainingWorks,
        CancellationToken ct)
        => BuildMultiGatheringPlanAsync(
            new[] { new MultiAlteringSupplyPreflight(parentPlan, blockedRecipe, remainingWorks) },
            ct);

    private async Task<IReadOnlyList<MultiGatheringRequest>> BuildMultiGatheringPlanAsync(
        IReadOnlyList<MultiAlteringSupplyPreflight> roots,
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

            if (LeatherRawMaterialPolicy.IsExternalRaw(itemName))
                return; // Never schedule a free gather or raw-input purchase.

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

                AlteringRecipe producer = SelectProducer(itemName, candidates);
                if (producer.MissingIngredients.Count == 0)
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

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (root.RemainingWorks <= 0)
                continue;

            foreach (var missing in root.Recipe.MissingIngredients)
            {
                long demand = checked(missing.Required * (long)root.RemainingWorks);
                await PlanItemAsync(
                    missing.DisplayName,
                    demand,
                    root.Plan,
                    0);
            }
        }

        // The virtual inventory ledger applies the current stock only once across
        // every selected root and every sibling branch. Duplicate raw leaves therefore
        // become one summed request without counting the same current stock twice.
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

    private static AlteringRecipe SelectProducer(
        string itemName,
        IReadOnlyList<AlteringRecipe> candidates)
    {
        var exact = candidates.Where(x => x.DisplayName == itemName).ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1)
            throw new InvalidOperationException(
                $"{itemName} 가공 제법이 여러 개라 자동 선택하지 않습니다.");

        // Explicit live-test rule: when the output "철괴" has both qualified
        // recipes, recursive production always uses 철괴(철 광석). The legacy
        // 철괴(광석) route is intentionally excluded.
        if (itemName == "철괴")
        {
            var preferred = candidates
                .Where(x => x.DisplayName == "철괴(철 광석)")
                .ToArray();
            if (preferred.Length == 1) return preferred[0];
        }

        if (candidates.Count == 1) return candidates[0];

        throw new InvalidOperationException(
            $"{itemName}을 만드는 가공 제법이 여러 개라 자동 선택하지 않습니다.");
    }

    private static string OutputName(string displayName)
        => System.Text.RegularExpressions.Regex.Replace(displayName, @"\([^()]*\)$", "").Trim();

    private static string ResolveFacility(AlteringRecipe recipe, string outputName)
        => AlteringFacilityResolver.Resolve(recipe)
            ?? throw new InvalidOperationException(
                $"{outputName}의 가공 시설을 CLI 정보와 제법 이름에서 확인할 수 없습니다.");
}
