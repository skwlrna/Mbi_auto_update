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
        if (blockedRecipe.Reason != "not_enough_ingredient" || blockedRecipe.MissingIngredients.Count == 0)
            throw new InvalidOperationException("재료 부족이 아닌 조건은 자동으로 해결하지 않습니다.");

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

    private async Task ResolveItemAsync(
        string itemName,
        long quantity,
        AlteringPlan sourceRecipe,
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
                if (!gatherable.ToolOk)
                    throw new InvalidOperationException($"{itemName} 채집 도구가 없거나 내구도가 부족합니다.");

                long before = await _gathering.ItemCountAsync(itemName, ct);
                var gatherPlan = new GatheringPlan(itemName, checked((int)quantity))
                {
                    SourceRecipe = sourceRecipe with { AllowPaidButton = false }
                };
                var gathering = new GatheringAutomation(_gathering, _gatheringScreen, _delay, _verificationAttempts);
                gathering.Log += text => Log?.Invoke(text);
                Log?.Invoke($"[재료 해결] {itemName} 자동 채집 시작 · 추가 {quantity}개");
                await gathering.RunAsync(gatherPlan, ct);
                long after = await _gathering.ItemCountAsync(itemName, ct);
                if (after - before < quantity)
                    throw new InvalidOperationException($"{itemName} 채집 후 수량 검증 실패: +{after - before} / 필요 +{quantity}");
                Log?.Invoke($"[재료 해결] {itemName} 자동 채집 완료 · +{after - before}개");
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
                if (producer.Reason != "not_enough_ingredient" || producer.MissingIngredients.Count == 0)
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

    private static string OutputName(string displayName)
        => System.Text.RegularExpressions.Regex.Replace(displayName, @"\([^()]*\)$", "").Trim();

    private static string ResolveFacility(AlteringRecipe recipe, string outputName)
    {
        if (!string.IsNullOrWhiteSpace(recipe.FacilityName) &&
            AlteringPlan.Facilities.Contains(recipe.FacilityName))
            return recipe.FacilityName;

        if (outputName.Contains("괴", StringComparison.Ordinal) ||
            outputName.Contains("철", StringComparison.Ordinal) ||
            outputName.Contains("금속", StringComparison.Ordinal))
            return "금속 가공 시설";
        if (outputName.Contains("목재", StringComparison.Ordinal) ||
            outputName.Contains("원목", StringComparison.Ordinal) ||
            outputName.Contains("통나무", StringComparison.Ordinal))
            return "목재 가공 시설";
        if (outputName.Contains("가죽", StringComparison.Ordinal) ||
            outputName.Contains("피혁", StringComparison.Ordinal))
            return "가죽 가공 시설";
        if (outputName.Contains("옷감", StringComparison.Ordinal) ||
            outputName.Contains("실크", StringComparison.Ordinal) ||
            outputName.Contains("천", StringComparison.Ordinal) ||
            outputName.Contains("실", StringComparison.Ordinal))
            return "옷감 가공 시설";
        if (outputName.Contains("물약", StringComparison.Ordinal) ||
            outputName.Contains("비약", StringComparison.Ordinal) ||
            outputName.Contains("가루", StringComparison.Ordinal) ||
            outputName.Contains("항마석", StringComparison.Ordinal))
            return "약품 가공 시설";
        if (outputName.Contains("식재료", StringComparison.Ordinal) ||
            outputName.Contains("요리", StringComparison.Ordinal))
            return "식재료 가공 시설";

        throw new InvalidOperationException($"{outputName}의 가공 시설을 CLI 정보에서 확인할 수 없습니다.");
    }
}
