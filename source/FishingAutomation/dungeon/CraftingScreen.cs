using FishingAutomation;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

internal sealed class CraftingScreen : ICraftingScreen
{
    private readonly CraftingCliData _data;
    private readonly MabinogiMobileCli _cli;
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("제작");
    private string? _activeCraftingRecipe;
    private CraftingPlan? _registeredFoodPlan;
    private IReadOnlyList<CraftingFoodIngredient>? _registeredFoodBatch;
    private string? _directCraftPendingName;
    private int _directCraftPendingCount;
    // The original detail screen must visibly transition before the same
    // bottom-right teal area may authorize Space at the actual station.
    private Bitmap? _directCraftBeforeTravel;
    private CraftingQuestDeficit? _lastSingleQuestDeficit;
    // Per quest/batch only: no reuse across a freshly created quest.
    private string? _lastNamedDeficitRecipe;
    private IReadOnlyList<CraftingQuestDeficit> _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();
    private IReadOnlyList<string>? _questMaterialNameCatalog;
    private IReadOnlyList<string> _questRecipeMaterialNames = Array.Empty<string>();

    internal CraftingScreen(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        MabinogiMobileCli cli)
    {
        _data = new CraftingCliData(cli);
        _cli = cli;
        _ui = new ProductionUiRuntime(hwnd, settings, debugDir, "crafting");
        _ui.Log += text => Log?.Invoke(text);
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[제작][상태] {stage} · {detail}");
    }

    public string InputMode => _ui.InputMode;
    public IReadOnlyList<CraftingFoodIngredient>? RegisteredFoodBatch => _registeredFoodBatch;
    public event Action<string>? Log;

    internal Task<bool> CheckInterruptionsAsync(CancellationToken ct) => _ui.CheckInterruptionsAsync(ct);

    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);

    public async Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        plan.Validate();
        if (craftCount is < 1 or > 10)
            throw new InvalidDataException("제작은 한 번에 1~10회만 설정할 수 있습니다.");
        if (!CraftingHubLayout.IsSafeCraftDetailGeometry())
            throw new InvalidOperationException("제작 상세 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        if (!string.Equals(_activeCraftingRecipe, plan.DisplayName, StringComparison.Ordinal))
        {
            _activeCraftingRecipe = plan.DisplayName;
            _questRecipeMaterialNames = Array.Empty<string>();
            _questMaterialNameCatalog = null;
            _lastSingleQuestDeficit = null;
        }
        _directCraftBeforeTravel?.Dispose();
        _directCraftBeforeTravel = null;
        _directCraftPendingName = null;
        _directCraftPendingCount = 0;
        _lastNamedDeficitRecipe = null;
        _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();

        await OpenProductAsync(plan, ct);
        await SetCraftCountAsync(plan.DisplayName, craftCount, ct);

        // CLI Craftable describes one craft, not the selected batch. Registered
        // foods use a complete recipe and fresh stock; unregistered foods/items
        // retain the selected-detail UI fallback.
        var exact = await _data.ExactAsync(plan.DisplayName, ct);
        (bool Parsed, bool AllEnough, string Summary, IReadOnlyList<string> MaterialNames) materialState;
        _registeredFoodPlan = null;
        _registeredFoodBatch = null;
        if (plan.Category == CraftingCategory.Food &&
            CraftingFoodRecipeCatalog.TryGet(plan.DisplayName, out var recipe))
        {
            _registeredFoodPlan = plan;
            _registeredFoodBatch = recipe.ForBatch(craftCount);
            _questRecipeMaterialNames = recipe.Ingredients.Select(x => x.Name).ToArray();
            var stock = await _data.InventoryOnlyCountsWithLoadingRetryAsync(
                _questRecipeMaterialNames, ct, Log);
            var deficits = CraftingFoodBatchPolicy.Deficits(_registeredFoodBatch, stock);
            materialState = (true, deficits.Count == 0,
                string.Join(", ", _registeredFoodBatch.Select(x =>
                    $"{x.Name} CLI {stock[x.Name]}/{x.Amount}")), _questRecipeMaterialNames);
            Log?.Invoke($"[제작][레시피+CLI] {plan.DisplayName} {craftCount}회 · " +
                "재료명/수량 OCR 생략 · " + materialState.Summary);
        }
        else
        {
            materialState = await ReadSelectedBatchMaterialStateAsync(plan, craftCount, ct);

            // Refresh canonical ingredient names exposed by the filtered CLI on every
            // batch. Do not clear names learned by earlier batches: an ingredient that
            // was sufficient before can become short after prior crafts consume stock.
            _questRecipeMaterialNames = _questRecipeMaterialNames
                .Concat(exact.MissingIngredients.Select(ingredient => ingredient.DisplayName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Seed the quest-name matcher before the quest is created. The CLI gives
            // canonical names for currently missing one-craft ingredients, while the
            // selected batch detail can reveal additional ingredients that become short
            // only after the count is raised (for example 10 crafts). Accept detail OCR
            // names only when they exactly normalize to one known real item; fuzzy
            // correction is intentionally deferred until the quest row is read.
            _questMaterialNameCatalog ??= await LoadQuestMaterialNameCatalogAsync(
                plan.DisplayName, ct);
            var detailCanonicalNames = materialState.MaterialNames
                .Select(name =>
                {
                    string normalized = FuzzyText.Normalize(name);
                    var matches = _questMaterialNameCatalog
                        .Where(candidate =>
                            string.Equals(
                                FuzzyText.Normalize(candidate),
                                normalized,
                                StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    return matches.Length == 1 ? matches[0] : null;
                })
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray();

            _questRecipeMaterialNames = _questRecipeMaterialNames
                .Concat(detailCanonicalNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (_questRecipeMaterialNames.Count > 0)
                Log?.Invoke(
                    $"[제작] 퀘스트 생성 전 레시피 재료 후보 확정 · " +
                    string.Join(", ", _questRecipeMaterialNames));
            else
                Log?.Invoke(
                    "[제작] 퀘스트 생성 전 레시피 재료 후보를 확정하지 못함 · " +
                    "기존 전체 카탈로그 안전 보정 경로 유지");

        }

        bool canDirectCraft =
            exact.Craftable &&
            materialState.Parsed &&
            materialState.AllEnough;

        Log?.Invoke(
            $"[제작] 선택 {craftCount}회 실제 재료 판정 · {materialState.Summary} · " +
            (canDirectCraft ? "직접 제작 가능" : "부족/판독불확실 → 퀘스트 만들기"));

        if (canDirectCraft)
        {
            _stage.Move(ProductionStage.Travel, $"{plan.DisplayName} {craftCount}회 제작하러 가기");
            _directCraftPendingName = plan.DisplayName;
            _directCraftPendingCount = craftCount;

            _directCraftBeforeTravel?.Dispose();
            _directCraftBeforeTravel = Capture(ct);
            _ui.ClickFresh(CraftingHubLayout.CraftGoButtonPoint, ct);
            Log?.Invoke(
                $"[제작] 재료 충분 · 제작하러 가기 · 고정좌표 " +
                $"({CraftingHubLayout.CraftGoButtonPoint.X},{CraftingHubLayout.CraftGoButtonPoint.Y})");
            Log?.Invoke("[제작] 이동 중간 상태 확인 생략 · 제작대 도착 후 제작하기 버튼 대기");
            await Task.Delay(350, ct);
            return;
        }

        _stage.Move(ProductionStage.CreateQuest, $"{plan.DisplayName} {craftCount}회 퀘스트 생성");
        _ui.ClickFresh(CraftingHubLayout.CraftQuestButtonPoint, ct);
        Log?.Invoke(
            $"[제작] 재료 부족 · 퀘스트 만들기 · 고정좌표 " +
            $"({CraftingHubLayout.CraftQuestButtonPoint.X},{CraftingHubLayout.CraftQuestButtonPoint.Y})");
        await Task.Delay(800, ct);
    }

    private async Task<(bool Parsed, bool AllEnough, string Summary, IReadOnlyList<string> MaterialNames)> ReadSelectedBatchMaterialStateAsync(
        CraftingPlan plan, int craftCount,
        CancellationToken ct)
    {
        // Let the material counters finish updating after the final + count click.
        await Task.Delay(250, ct);

        static List<(long Owned, long Required)> Parse(IEnumerable<DetectionResult> lines)
        {
            var ratios = new List<(long Owned, long Required)>();
            foreach (var line in lines)
            {
                string text = (line.ReadText ?? "").Replace(" ", "");
                var match = Regex.Match(text, @"(?<owned>\d+)\s*/\s*(?<required>\d+)");
                if (!match.Success ||
                    !long.TryParse(match.Groups["owned"].Value, out long owned) ||
                    !long.TryParse(match.Groups["required"].Value, out long required) ||
                    owned < 0 || required <= 0)
                    continue;
                ratios.Add((owned, required));
            }
            return ratios;
        }

        static IReadOnlyList<string> ParseMaterialNames(IReadOnlyList<DetectionResult> lines)
        {
            var names = new List<string>();
            foreach (var line in lines)
            {
                string text = line.ReadText ?? "";
                var ratio = Regex.Match(text, @"\d+\s*/\s*\d+");
                if (!ratio.Success)
                    continue;

                string inline = Regex.Replace(text, @"\d+\s*/\s*\d+.*$", "").Trim();
                string? name = CleanName(inline);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = lines
                        .Where(candidate => Math.Abs(candidate.Center.Y - line.Center.Y) <= 35)
                        .Where(candidate => candidate.Center.X < 470)
                        .Select(candidate => CleanName(candidate.ReadText ?? ""))
                        .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
                }

                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            return names
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        List<(long Owned, long Required)>? first = null;
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var lines = await _ui.Ocr.ReadLinesAsync(
                frame,
                CraftingHubLayout.ProductionQuestMaterialArea,
                3,
                ct);
            var ratios = Parse(lines);

            if (ratios.Count == 0)
            {
                Log?.Invoke(
                    $"[제작] 선택 {craftCount}회 상세 재료 수량 OCR 없음 · 안전하게 퀘스트 경로 사용");
                return (false, false, "재료 비율 OCR 없음", Array.Empty<string>());
            }

            if (first is null)
            {
                first = ratios;
                await Task.Delay(180, ct);
                continue;
            }

            if (first.Count != ratios.Count ||
                (plan.Category == CraftingCategory.Food
                    ? !first.Select(x => x.Required).SequenceEqual(ratios.Select(x => x.Required))
                    : !first.SequenceEqual(ratios)))
            {
                Log?.Invoke(
                    $"[제작] 선택 {craftCount}회 상세 재료 수량 2프레임 불일치 · 안전하게 퀘스트 경로 사용");
                return (false, false, "재료 비율 2프레임 불일치", Array.Empty<string>());
            }

            bool allEnough = ratios.All(x => x.Owned >= x.Required);
            string summary = string.Join(", ", ratios.Select(x => $"{x.Owned}/{x.Required}"));
            var materialNames = ParseMaterialNames(lines);
            if (plan.Category == CraftingCategory.Food)
            {
                // Required comes from selected count; Owned comes from CLI.
                if (materialNames.Count != ratios.Count)
                    return (false, false, "재료명 재인식 필요 · 퀘스트 경로 사용", materialNames);
                var stock = await _data.InventoryOnlyCountsWithLoadingRetryAsync(materialNames, ct, Log);
                allEnough = materialNames.Select((name, index) =>
                    stock[name] >= ratios[index].Required).All(x => x);
                summary = string.Join(", ", materialNames.Select((name, index) =>
                    $"{name} CLI {stock[name]}/{ratios[index].Required}"));
            }
            if (materialNames.Count > 0)
                Log?.Invoke(
                    $"[제작] 선택 {craftCount}회 상세 레시피 재료명 선판독 · " +
                    string.Join(", ", materialNames));
            return (true, allEnough, summary, materialNames);
        }

        return (false, false, "재료 비율 확인 실패", Array.Empty<string>());
    }

    private async Task OpenProductAsync(CraftingPlan plan, CancellationToken ct)
    {
        _stage.Move(ProductionStage.OpenHub, $"{plan.DisplayName} 제작 허브 진입");
        // If a matching detail sheet is already open, reuse it. The detail OCR
        // can take long enough for animated field pixels/players/chat to change.
        // Never compare the entire field frame after that OCR: K is a harmless
        // navigation shortcut and only needs a fresh 800x1000/window guard.
        using (var current = Capture(ct))
        {
            if (await IsProductDetailAsync(current, plan.DisplayName, ct))
            {
                _stage.Move(ProductionStage.Detail, $"{plan.DisplayName} 상세 화면 재사용");
                return;
            }
        }

        _ui.TapFresh(0x25, ct); // K: 가공/제작 허브
        Log?.Invoke("[제작] 제작 허브 열기 · K 입력 · fresh frame");
        await Task.Delay(750, ct);

        // The user-confirmed layout keeps 제작 immediately to the right of 가공.
        // OCR first, then click the fresh exact label inside the fixed bottom navigation.
        await ClickExactAsync(
            "제작",
            new Rectangle(285, 850, 320, 145),
            ct,
            "하단 제작 탭을 확인하지 못했습니다.");
        await Task.Delay(650, ct);

        string category = plan.Category == CraftingCategory.Food ? "음식" : "아이템";
        _stage.Move(ProductionStage.SelectCategory, category);
        await ClickCraftingCategoryCardAsync(plan.Category, category, ct);
        Log?.Invoke($"[제작] {category} 제작 목록 안정화 대기 · {CraftingHubLayout.ProductListSettleDelayMs}ms");
        await Task.Delay(CraftingHubLayout.ProductListSettleDelayMs, ct);

        _stage.Move(ProductionStage.Search, plan.DisplayName);
        await SearchProductAsync(plan.DisplayName, category, ct);
        _stage.Move(ProductionStage.Detail, plan.DisplayName);
    }

    private async Task ClickCraftingCategoryCardAsync(
        CraftingCategory category,
        string label,
        CancellationToken ct)
    {
        Rectangle cardArea = CraftingHubLayout.CategoryCardArea(category);
        Point fixedPoint = CraftingHubLayout.CategoryClickPoint(category);
        if (cardArea.IsEmpty || fixedPoint.IsEmpty ||
            !CraftingHubLayout.IsSafeFallbackPoint(category))
            throw new InvalidOperationException("지원하지 않는 제작 허브 분류입니다.");

        // Use the same bounded observation policy as the shared production
        // runtime. A single OCR miss must not abort a stable crafting hub.
        _ = await _ui.RequireStableExactAsync(
            "제작", CraftingHubLayout.HubHeaderArea, ct,
            "제작 허브 상단 제목을 확인하지 못했습니다.",
            compact: true, dimText: true, settleMs: 150);
        _ui.ClickFresh(fixedPoint, ct);
        Log?.Invoke(
            $"[제작] 제작 허브 카드 선택 · {label} · 고정좌표 ({fixedPoint.X},{fixedPoint.Y})");
    }

    private async Task SearchProductAsync(string displayName, string category, CancellationToken ct)
    {
        if (!CraftingHubLayout.IsSafeSearchGeometry())
            throw new InvalidOperationException("제작 검색 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        Log?.Invoke($"[제작] {category} 제작 목록 · 고정 UI 경로 진입 · 헤더 OCR 생략");

        // V3.0.2 live evidence: the magnifier click really opened the search
        // dialog, but placeholder OCR missed it. That false negative caused an
        // OCR fallback click on an unrelated location and every later input was
        // then applied to the wrong screen. From V3.0.3 onward, state changes
        // are authorized only by the fixed search ROI transition.
        using var beforeSearch = Capture(ct);

        _ui.ClickFresh(CraftingHubLayout.ProductSearchIconPoint, ct);
        Log?.Invoke(
            $"[제작] 검색 돋보기 · 고정좌표 ({CraftingHubLayout.ProductSearchIconPoint.X},{CraftingHubLayout.ProductSearchIconPoint.Y})");
        await Task.Delay(450, ct);

        using var searchDialog = Capture(ct);
        double openRatio = ProductionUiRuntime.MeasureVisualChangeRatio(
            beforeSearch,
            searchDialog,
            CraftingHubLayout.ProductSearchDialogArea);
        Log?.Invoke($"[제작] 검색창 화면 변화 확인 · {openRatio:P1}");

        if (openRatio < CraftingHubLayout.SearchDialogOpenChangeRatio)
            throw Fail(
                searchDialog,
                $"검색 돋보기 입력 후 검색창 화면 전환을 확인하지 못했습니다. 변화율={openRatio:P1} · 추가 클릭 없이 정지");

        Log?.Invoke("[제작] 검색창 열림 확인 · OCR 미사용 · 다음 입력 허용");

        // Recent-search chips can occupy the old (400,862) point.
        // The user verified that Enter opens text entry in the search popup.
        // Keep food/item first-result coordinates unchanged.
        _ui.TapFresh(0x1C, ct); // Enter: focus text input, not commit.
        Log?.Invoke("[제작] 검색 입력칸 · Enter로 활성화 · 최근 검색어 고정좌표 클릭 금지");
        await Task.Delay(120, ct);
        _ui.PasteFresh(displayName, ct);
        await Task.Delay(120, ct);

        _ui.TapFresh(0x1C, ct); // Enter: typed text commit is mandatory.
        Log?.Invoke($"[제작] 검색어 입력 확정 · Enter · {displayName}");
        await Task.Delay(220, ct);

        using var beforeApply = Capture(ct);

        _ui.TapFresh(0x39, ct); // Space = 적용
        Log?.Invoke("[제작] 검색 적용 · Space · 고정 UI");
        await Task.Delay(700, ct);

        using var resultFrame = Capture(ct);
        double resultRatio = ProductionUiRuntime.MeasureVisualChangeRatio(
            beforeApply,
            resultFrame,
            CraftingHubLayout.ProductSearchResultArea);
        var exact = await FindUniqueAsync(
            resultFrame,
            CraftingHubLayout.ProductSearchResultArea,
            displayName,
            ct);

        Log?.Invoke(
            exact is null
                ? $"[제작] 검색 적용 후 결과 확인 · OCR 미검출 · 화면 변화 {resultRatio:P1}"
                : $"[제작] 검색 적용 후 결과 확인 · exact OCR 확인 · 화면 변화 {resultRatio:P1}");

        if (exact is null &&
            resultRatio < CraftingHubLayout.SearchResultChangeRatio)
            throw Fail(
                resultFrame,
                $"검색 적용 후 결과 화면 전환을 확인하지 못했습니다. 변화율={resultRatio:P1} · 첫 결과 클릭 없이 정지");

        _ui.ClickFresh(CraftingHubLayout.ProductFirstResultPoint, ct);
        Log?.Invoke(
            $"[제작] 검색 첫 결과 선택 · 고정좌표 ({CraftingHubLayout.ProductFirstResultPoint.X},{CraftingHubLayout.ProductFirstResultPoint.Y})");
        await Task.Delay(550, ct);

        using var verify = Capture(ct);
        if (await IsProductDetailStructureAsync(verify, ct))
        {
            Log?.Invoke($"[제작] 선택 후 품목명 OCR 재확인 생략 · 상세 화면 구조 확인 · 검색 단계 완료");
            return;
        }

        throw Fail(
            verify,
            $"첫 결과 클릭 후 제작 상세 화면 구조를 확인하지 못했습니다. 추가 fallback 클릭 없이 정지");
    }

    private async Task<bool> IsProductDetailStructureAsync(Bitmap frame, CancellationToken ct)
    {
        var client = new Rectangle(Point.Empty, frame.Size);
        var materials = await FindUniqueAsync(frame, client, "필요한 재료", ct);
        var quest = await FindUniqueAsync(frame, client, "퀘스트 만들기", ct);
        return materials is not null && quest is not null &&
            materials.Value.Center.Y < quest.Value.Center.Y;
    }

    private async Task<bool> IsProductDetailAsync(Bitmap frame, string displayName, CancellationToken ct)
    {
        var client = new Rectangle(Point.Empty, frame.Size);
        var title = await FindUniqueAsync(frame, client, displayName, ct);
        if (title is null) return false;
        var materials = await FindUniqueAsync(frame, client, "필요한 재료", ct);
        var quest = await FindUniqueAsync(frame, client, "퀘스트 만들기", ct);
        return materials is not null && quest is not null &&
            title.Value.Center.Y < materials.Value.Center.Y &&
            materials.Value.Center.Y < quest.Value.Center.Y;
    }

    private async Task SetCraftCountAsync(
        string displayName,
        int wanted,
        CancellationToken ct)
    {
        if (wanted is < 1 or > 10)
            throw new InvalidDataException("제작 횟수는 1~10회만 설정할 수 있습니다.");
        if (!CraftingHubLayout.IsSafeCraftDetailGeometry())
            throw new InvalidOperationException("제작 상세 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        // The caller has already verified the product detail screen immediately
        // before this method. Re-running three full-screen OCR passes here caused
        // a live 17.8s delay before the first + click.
        await Task.Delay(CraftingHubLayout.CraftDetailSettleDelayMs, ct);
        Log?.Invoke(
            $"[제작] 제작 횟수 입력 시작 · 상세창 중복 OCR 생략 · 기본 1회 · 목표 {wanted}회");

        // Live crafting detail opens at 1회. Only add the required increments.
        for (int i = 1; i < wanted; i++)
        {
            _ui.ClickFresh(CraftingHubLayout.CraftCountPlusPoint, ct);
            await Task.Delay(70, ct);
        }

        Log?.Invoke(
            wanted == 1
                ? "[제작] 제작 횟수 설정 · 기본 1회 유지 · 횟수 버튼 입력 없음"
                : $"[제작] 제작 횟수 설정 · 기본 1회 기준 · + {wanted - 1}회 · " +
                  $"({CraftingHubLayout.CraftCountPlusPoint.X},{CraftingHubLayout.CraftCountPlusPoint.Y})");
    }

    public async Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
        CraftingPlan plan, CancellationToken ct)
    {
        if (_registeredFoodBatch is not null && _registeredFoodPlan?.DisplayName == plan.DisplayName)
            return await ReadRegisteredFoodDeficitsAsync(plan, ct);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var rows = await ReadQuestDeficitsOnceAsync(plan, ct);
            if (plan.Category != CraftingCategory.Food ||
                rows.All(x => !string.IsNullOrWhiteSpace(x.DisplayName)))
                return rows;
            Log?.Invoke($"[제작][인식 재시도] {plan.DisplayName} · 재료명 미확인 · {attempt}/3 · 재료 부족으로 건너뛰지 않음");
            await Task.Delay(300, ct);
        }
        using var failed = Capture(ct);
        throw Fail(failed, "음식 재료명 OCR 미확인 · 수급 불가와 구분 · 현재 배치 재인식 필요");
    }

    private async Task<IReadOnlyList<CraftingQuestDeficit>> ReadRegisteredFoodDeficitsAsync(
        CraftingPlan plan, CancellationToken ct)
    {
        if (_directCraftPendingName == plan.DisplayName)
            return Array.Empty<CraftingQuestDeficit>();
        var stock = await _data.InventoryOnlyCountsWithLoadingRetryAsync(
            _registeredFoodBatch!.Select(x => x.Name), ct, Log);
        var deficits = CraftingFoodBatchPolicy.Deficits(_registeredFoodBatch, stock);
        Log?.Invoke($"[제작][CLI 부족량] {plan.DisplayName} · " +
            (deficits.Count == 0 ? "전체 재료 확보" : string.Join(", ",
                deficits.Select(x => $"{x.DisplayName} {x.Current}/{x.Required}"))));
        if (deficits.Count > 0) return deficits;

        using var frame = Capture(ct);
        if (!await HasCraftActionLabelAsync(frame, ct))
        {
            await CloseOverlayAsync(ct);
            await Task.Delay(CraftingHubLayout.ProductionQuestListSettleDelayMs, ct);
            _ui.ClickFresh(CraftingHubLayout.ProductionQuestTopPoint, ct);
            Log?.Invoke("[제작] 전체 레시피 CLI 재고 충족 · 퀘스트 1회 클릭 · 제작대 자동 복귀");
        }
        ClearQuestDeficits();
        return Array.Empty<CraftingQuestDeficit>();
    }

    private async Task<CraftingFoodQuestRow> FindRegisteredFoodRowAsync(
        CraftingQuestDeficit requested, CancellationToken ct)
    {
        await OpenQuestPopupAsync(_registeredFoodPlan!, ct);
        var gatherable = GatheringQueries.ParseCatalog(await _cli.GetGatherableItemsAsync(ct));
        var supported = _registeredFoodBatch!.Where(x =>
            !CraftingFoodMaterialPolicy.IsUnsupportedDirect(x.Name) &&
            !CraftingFoodMaterialPolicy.IsKnownProcessing(x.Name) &&
            (gatherable.Any(g => g.DisplayName == x.Name && g.ToolOk) ||
             (!gatherable.Any(g => g.DisplayName == x.Name) &&
              (LivingSkillGatheringCatalog.TryResolveBulk(x.Name, out _) ||
               LivingSkillGatheringCatalog.IsQuestOnlyMaterial(x.Name)))))
            .Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            var lines = await _ui.Ocr.ReadLinesAsync(frame,
                CraftingHubLayout.ProductionQuestMaterialArea, 3, ct);
            var visible = new List<CraftingQuestDeficit>();
            foreach (var line in lines.OrderBy(x => x.Center.Y))
            {
                var match = Regex.Match(line.ReadText ?? "", @"(?<cur>\d+)\s*/\s*(?<req>\d+)");
                if (match.Success && long.TryParse(match.Groups["cur"].Value, out long current) &&
                    long.TryParse(match.Groups["req"].Value, out long required) && required > 0)
                    visible.Add(new("", current, required, line.Center.Y));
            }
            var stock = await _data.InventoryOnlyCountsWithLoadingRetryAsync(
                _registeredFoodBatch.Select(x => x.Name), ct, Log);
            var deficits = CraftingFoodBatchPolicy.Deficits(_registeredFoodBatch, stock);
            if (!deficits.Any(x => x.DisplayName == requested.DisplayName))
                return new(requested, Array.Empty<CraftingQuestDeficit>());
            var row = CraftingFoodBatchPolicy.FindGatherRow(
                requested.DisplayName, deficits, visible, supported);
            if (row is not null)
            {
                Log?.Invoke($"[제작] 레시피 재료 행 위치 · Y={row.Row.RowY} · " +
                    $"CLI 검증 대상={string.Join(", ", row.Targets.Select(x => x.DisplayName))} · 재료명 OCR 미사용");
                return row;
            }
            Log?.Invoke($"[제작][행 위치 재확인] {attempt}/3 · 퀘스트 재생성 없음");
            await Task.Delay(300, ct);
        }
        throw new CraftingQuestRowRecognitionException(
            "등록 음식 재료 행 위치를 확인하지 못했습니다. 재료 부족으로 건너뛰거나 퀘스트를 반복 생성하지 않습니다.");
    }

    private async Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsOnceAsync(
        CraftingPlan plan, CancellationToken ct)
    {
        if (string.Equals(_directCraftPendingName, plan.DisplayName, StringComparison.Ordinal))
        {
            _lastSingleQuestDeficit = null;
            _lastNamedDeficitRecipe = null;
            _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();
            Log?.Invoke("[제작] 재료 충분 직접 제작 · 퀘스트 재료 확인 생략");
            return Array.Empty<CraftingQuestDeficit>();
        }

        // A completed quest navigates to the station; it no longer opens a shortage popup.
        // Check the previous quest's remaining named deficits BEFORE requiring that popup.
        using (var current = Capture(ct))
        {
            if (await HasCraftActionLabelAsync(current, ct))
            {
                ClearQuestDeficits();
                Log?.Invoke("[제작] 제작하기 상세창 확인 · 부족 재료 팝업 검사 생략 · 기존 수량 유지");
                return Array.Empty<CraftingQuestDeficit>();
            }
        }
        if (string.Equals(_lastNamedDeficitRecipe, plan.DisplayName, StringComparison.Ordinal) &&
            _lastNamedDeficits.Count > 0 &&
            _lastNamedDeficits.All(x => !string.IsNullOrWhiteSpace(x.DisplayName)))
        {
            bool allReady = true;
            foreach (var deficit in _lastNamedDeficits)
            {
                long inventory = await _data.InventoryOnlyCountWithLoadingRetryAsync(
                    deficit.DisplayName, ct, Log);
                if (inventory < deficit.Required) allReady = false;
            }
            if (allReady)
            {
                // Recheck after inventory queries: navigation may already have completed.
                using var latest = Capture(ct);
                if (!await HasCraftActionLabelAsync(latest, ct))
                {
                    await CloseOverlayAsync(ct);
                    await Task.Delay(CraftingHubLayout.ProductionQuestListSettleDelayMs, ct);
                    _ui.ClickFresh(CraftingHubLayout.ProductionQuestTopPoint, ct);
                    Log?.Invoke("[제작] 남은 부족 재료 재고 확보 확인 · 퀘스트 1회 클릭 · 제작대 자동 이동 대기");
                }
                ClearQuestDeficits();
                return Array.Empty<CraftingQuestDeficit>();
            }
        }

        _stage.Move(ProductionStage.ReadQuest, $"{plan.DisplayName} 부족 재료 확인");
        // This method is entered immediately after creating the production quest.
        // The new quest is pinned at the top of the right quest list in the live
        // 800x1000 UI, so do not spend multiple OCR passes trying to rediscover
        // the small orange title before opening it.
        await OpenQuestPopupAsync(plan, ct);
        using var frame = Capture(ct);
        var roi = CraftingHubLayout.ProductionQuestMaterialArea;
        Log?.Invoke(
            $"[제작] 퀘스트 재료 OCR 영역 · ({roi.X},{roi.Y},{roi.Width},{roi.Height}) · " +
            "실제 하단 재료행 기준");
        var lines = (await _ui.Ocr.ReadLinesAsync(frame, roi, 3, ct))
            .OrderBy(x => x.Center.Y)
            .ThenBy(x => x.Center.X)
            .ToArray();

        _ui.NoteDiagnostic("quest-material-ocr",
            $"{plan.DisplayName} ROI={roi} · " + string.Join(" | ", lines.Select(x => $"Y={x.Center.Y}:{x.ReadText}")));
        var ratios = new List<(long Current, long Required, int Y, string Text)>();
        foreach (var line in lines)
        {
            string text = (line.ReadText ?? "").Replace(" ", "");
            var m = Regex.Match(text, @"(?<cur>\d+)\s*/\s*(?<req>\d+)");
            if (!m.Success ||
                !long.TryParse(m.Groups["cur"].Value, out long current) ||
                !long.TryParse(m.Groups["req"].Value, out long required) ||
                required <= 0)
                continue;
            ratios.Add((current, required, line.Center.Y, line.ReadText ?? ""));
        }

        var deficits = new List<CraftingQuestDeficit>();
        if (ratios.Count == 0)
        {
            // After the final missing material is gathered/processed, the live quest
            // popup can remove every material ratio row entirely. V3.0.17 treated
            // that normal completed state as an OCR failure. Accept the empty ratio
            // screen only when the immediately preceding read had exactly one deficit
            // and current inventory independently proves that material is now enough.
            if (_lastSingleQuestDeficit is CraftingQuestDeficit verified)
            {
                long current = await _data.InventoryOnlyCountWithLoadingRetryAsync(
                    verified.DisplayName, ct, Log);
                if (current >= verified.Required)
                {
                    Log?.Invoke(
                        $"[제작] 퀘스트 재료행 없음 · 직전 마지막 부족 재료 재고 검증 완료 · " +
                        $"{verified.DisplayName} {current}/{verified.Required} · 부족 재료 없음으로 진행");
                    _lastSingleQuestDeficit = null;
                    _lastNamedDeficitRecipe = null;
                    _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();
                    return Array.Empty<CraftingQuestDeficit>();
                }
            }

            throw Fail(frame,
                "제작 퀘스트 재료 수량을 읽지 못했습니다. 직전 마지막 부족 재료의 재고 완료 근거도 없어 중단합니다.");
        }
        foreach (var ratio in ratios)
        {
            // The final station step also has 0/N; it is not a material row.
            if (ratio.Text.Replace(" ", "").Contains("제작대", StringComparison.Ordinal))
                continue;
            string inline = Regex.Replace(ratio.Text, @"\d+\s*/\s*\d+.*$", "").Trim();
            string? name = CleanName(inline);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = lines
                    .Where(x => Math.Abs(x.Center.Y - ratio.Y) <= 35)
                    .Where(x => x.Center.X < 470)
                    .Select(x => CleanName(x.ReadText ?? ""))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                // Live V3.0.26 evidence: Windows OCR can read the bright 1/80
                // counter but omit the dim-gray material name on the same row.
                // Re-read only the left name cell with low-threshold/high-contrast
                // OCR. The later material click still re-verifies this exact name
                // on two fresh frames before any input is sent.
                var nameRoi = Rectangle.Intersect(
                    new Rectangle(215, Math.Max(610, ratio.Y - 34), 255, 68),
                    new Rectangle(Point.Empty, frame.Size));
                var dimLines = await _ui.Ocr.ReadDimLinesAsync(frame, nameRoi, ct);
                var candidates = dimLines
                    .Select(x => CleanName(x.ReadText ?? ""))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .GroupBy(x => x, StringComparer.Ordinal)
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Name, StringComparer.Ordinal)
                    .ToArray();

                if (candidates.Length > 0 &&
                    (candidates.Length == 1 || candidates[0].Count > candidates[1].Count))
                {
                    name = candidates[0].Name;
                    Log?.Invoke(
                        $"[제작] 퀘스트 재료명 회색글씨 보강 OCR · {name} · " +
                        $"행Y={ratio.Y} · 후보={candidates[0].Count}회");
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    name = await CanonicalizeQuestMaterialNameAsync(name!, plan.DisplayName, ct);
                }
                catch (InvalidOperationException ex)
                {
                    Log?.Invoke(
                        $"[제작] 퀘스트 재료명 OCR 보조 판정 포기 · 수량={ratio.Current}/{ratio.Required} · " +
                        $"행Y={ratio.Y} · {ex.Message} · 부족 행 자체로 계속");
                    name = null;
                }
            }

            long currentQuantity = ratio.Current;
            if (plan.Category == CraftingCategory.Food && !string.IsNullOrWhiteSpace(name))
            {
                currentQuantity = await _data.InventoryOnlyCountWithLoadingRetryAsync(name!, ct, Log);
                Log?.Invoke($"[제작][수량 기준=CLI] {name} · OCR={ratio.Current}/{ratio.Required} · CLI={currentQuantity}/{ratio.Required} · 행Y={ratio.Y}");
            }
            if (currentQuantity < ratio.Required ||
                plan.Category == CraftingCategory.Food && string.IsNullOrWhiteSpace(name))
            {
                if (string.IsNullOrWhiteSpace(name))
                    Log?.Invoke(
                        $"[제작] 퀘스트 재료명 OCR 미확인 · 수량={ratio.Current}/{ratio.Required} · " +
                        $"행Y={ratio.Y} · 이름 없이 부족 행으로 등록");
                deficits.Add(new(name ?? "", currentQuantity, ratio.Required, ratio.Y));
            }
        }

        // An unnamed FOOD deficit is not a generic gathering instruction.
        // Only prior OCR-identified rows from THIS quest and fresh exact CLI
        // inventory may recover a unique identity. No fixed-name guessing
        // and no resurrection of an old batch after quest recreation.
        if (plan.Category == CraftingCategory.Food &&
            deficits.Count > 0 &&
            deficits.Any(x => string.IsNullOrWhiteSpace(x.DisplayName)) &&
            string.Equals(_lastNamedDeficitRecipe, plan.DisplayName, StringComparison.Ordinal) &&
            _lastNamedDeficits.Count > 0)
        {
            try
            {
                var fresh = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (string name in _lastNamedDeficits.Select(x => x.DisplayName)
                             .Distinct(StringComparer.Ordinal))
                    fresh[name] = await _data.InventoryOnlyCountWithLoadingRetryAsync(
                        name, ct, Log);

                var recovered = CraftingQuestMaterialRecoveryPolicy.IdentifyUniqueFromInventory(
                    _lastNamedDeficits, deficits, fresh);
                if (recovered is not null)
                {
                    int index = deficits.FindIndex(x => string.IsNullOrWhiteSpace(x.DisplayName));
                    deficits[index] = recovered;
                    Log?.Invoke(
                        $"[제작] 이전 퀘스트 재료명 안전 복원 · {recovered.DisplayName} " +
                        $"{recovered.Current}/{recovered.Required} · 현재 배치 이름/필요량 + CLI 부족 재료 유일 확인 · " +
                        "이후 일반 재료 수급 검증 유지");
                }
                else
                    Log?.Invoke(
                        "[제작] 재료명 복원 미확정 · CLI 부족 후보가 하나로 좁혀지지 않음 · 건너뛰기 대신 재인식");
            }
            catch (InvalidDataException ex)
            {
                Log?.Invoke(
                    $"[제작] 재료명 복원 CLI 검증 실패 · {ex.Message} · 재료명 미확인 유지");
            }
        }

        if (plan.Category == CraftingCategory.Food && deficits.Count == 0 && ratios.Count > 0)
        {
            await CloseOverlayAsync(ct);
            await Task.Delay(CraftingHubLayout.ProductionQuestListSettleDelayMs, ct);
            _ui.ClickFresh(CraftingHubLayout.ProductionQuestTopPoint, ct);
            Log?.Invoke("[제작] CLI 재료 충분 · 부족 팝업 정리 후 퀘스트 1회 클릭 · 제작대 자동 복귀");
        }
        if (deficits.Count == 0)
        {
            _lastNamedDeficitRecipe = null;
            _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();
        }
        else if (deficits.All(x => !string.IsNullOrWhiteSpace(x.DisplayName)))
        {
            _lastNamedDeficitRecipe = plan.DisplayName;
            _lastNamedDeficits = deficits.ToArray();
        }

        _lastSingleQuestDeficit =
            deficits.Count == 1 && !string.IsNullOrWhiteSpace(deficits[0].DisplayName)
                ? deficits[0]
                : null;
        Log?.Invoke(deficits.Count == 0
            ? $"[제작] {plan.DisplayName} 퀘스트 부족 재료 없음"
            : "[제작] 퀘스트 부족 재료 · " +
              string.Join(", ", deficits.Select(x => $"{x.DisplayName} {x.Current}/{x.Required}")));
        return deficits;
    }

    private async Task<string> CanonicalizeQuestMaterialNameAsync(
        string ocrName,
        string recipeName,
        CancellationToken ct)
    {
        string normalizedOcr = FuzzyText.Normalize(ocrName);
        if (normalizedOcr.Length == 0)
            return ocrName;

        _questMaterialNameCatalog ??= await LoadQuestMaterialNameCatalogAsync(recipeName, ct);

        // The recipe candidates were collected before quest creation from the
        // filtered CLI recipe and the selected-count detail screen. They must win
        // even over an exact global-catalog OCR hit, because dim quest text can turn
        // one recipe ingredient into the exact name of an unrelated real item.
        // Example: a bad OCR read of 양파 must not become 양털 merely because 양털
        // exists globally.
        var recipeDecision = QuestMaterialNameMatcher.Decide(
            ocrName,
            _questRecipeMaterialNames);
        if (recipeDecision.Name is not null)
        {
            Log?.Invoke(
                $"[제작] 퀘스트 재료명 OCR 보정 · {ocrName} → {recipeDecision.Name} · " +
                $"현재 레시피 재료 우선 · {recipeDecision.Diagnostic}");
            return recipeDecision.Name;
        }
        if (recipeDecision.AmbiguousCandidates.Count > 0)
            throw new InvalidOperationException(
                $"제작 퀘스트 재료명 '{ocrName}'의 현재 레시피 후보가 여러 개라 자동 보정하지 않습니다: " +
                string.Join(", ", recipeDecision.AmbiguousCandidates));

        var exact = _questMaterialNameCatalog
            .Where(x => string.Equals(FuzzyText.Normalize(x), normalizedOcr, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (exact.Length == 1)
            return exact[0];
        if (exact.Length > 1)
            throw new InvalidOperationException(
                $"제작 퀘스트 재료명 '{ocrName}'과 정확히 일치하는 실제 품목명이 여러 개라 자동 선택하지 않습니다.");

        var catalogDecision = QuestMaterialNameMatcher.Decide(
            ocrName,
            _questMaterialNameCatalog);
        if (catalogDecision.Name is not null)
        {
            Log?.Invoke(
                $"[제작] 퀘스트 재료명 OCR 보정 · {ocrName} → {catalogDecision.Name} · " +
                $"전체 문자열 유일 최상위 후보 · {catalogDecision.Diagnostic}");
            return catalogDecision.Name;
        }

        if (catalogDecision.AmbiguousCandidates.Count > 0)
            throw new InvalidOperationException(
                $"제작 퀘스트 재료명 '{ocrName}'의 실제 품목 최상위 후보가 여러 개라 자동 보정하지 않습니다: " +
                string.Join(", ", catalogDecision.AmbiguousCandidates));

        // Keep exact visual text for quest-only materials that are not exposed by
        // current read-only catalogs. The later two-frame row verification still
        // prevents a click when that visual name is not really present.
        Log?.Invoke($"[제작] 퀘스트 재료명 카탈로그 미확인 · {ocrName} · 화면 2프레임 재검증 경로 유지");
        return ocrName;
    }

    private async Task<IReadOnlyList<string>> LoadQuestMaterialNameCatalogAsync(
        string recipeName,
        CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var gatherable = GatheringQueries.ParseCatalog(await _cli.GetGatherableItemsAsync(ct));
        foreach (var item in gatherable)
            names.Add(item.DisplayName);

        var altering = AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync(ct));
        foreach (var recipe in altering)
        {
            names.Add(recipe.DisplayName);
            string output = Regex.Replace(recipe.DisplayName, @"\([^()]*\)$", "").Trim();
            if (!string.IsNullOrWhiteSpace(output))
                names.Add(output);
            foreach (var ingredient in recipe.MissingIngredients)
                names.Add(ingredient.DisplayName);
        }

        // Add ingredient names exposed for the current recipe and keep them as
        // the highest-priority correction set. This is narrower than the global
        // catalog and resolves live cases such as "양배" -> "양배추".
        var crafting = CraftingQueries.ParseCatalog(await _cli.GetCraftableItemsAsync(recipeName, ct));
        var cliRecipeMaterialNames = crafting
            .SelectMany(item => item.MissingIngredients)
            .Select(ingredient => ingredient.DisplayName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        _questRecipeMaterialNames = _questRecipeMaterialNames
            .Concat(cliRecipeMaterialNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        foreach (string ingredientName in _questRecipeMaterialNames)
            names.Add(ingredientName);

        var result = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Log?.Invoke($"[제작] 퀘스트 재료명 검증 카탈로그 준비 · {result.Length}개");
        return result;
    }

    private static string? CleanName(string text)
    {
        string cleaned = Regex.Replace(text, @"[^가-힣A-Za-z0-9+()\s]", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return null;
        string compact = cleaned.Replace(" ", "");
        string[] blocked = { "퀘스트", "필요한아이템", "구하는방법", "취소", "제작" };
        if (blocked.Any(x => compact.Contains(x, StringComparison.Ordinal))) return null;
        if (!Regex.IsMatch(cleaned, "[가-힣A-Za-z]", RegexOptions.CultureInvariant)) return null;
        return cleaned;
    }

    private async Task OpenQuestPopupAsync(CraftingPlan plan, CancellationToken ct)
    {
        // The quest shortage popup can persist during auto-gathering.
        // Clicking the quest list behind it only changes pixels and causes
        // false OCR failures. Reuse the verified existing popup instead.
        using (var existing = Capture(ct))
        {
            if (await IsQuestMaterialPopupAsync(existing, ct))
            {
                await Task.Delay(300, ct);
                using var confirmed = Capture(ct);
                if (await IsQuestMaterialPopupAsync(confirmed, ct))
                {
                    Log?.Invoke("[제작] 기존 부족 재료 팝업 2프레임 확인 · 추가 퀘스트 클릭 없음");
                    return;
                }
            }
        }
        await CloseOverlayAsync(ct);
        await Task.Delay(CraftingHubLayout.ProductionQuestListSettleDelayMs, ct);

        using var before = Capture(ct);
        _ui.ClickFresh(CraftingHubLayout.ProductionQuestTopPoint, ct);
        Log?.Invoke(
            $"[제작] 새 제작 퀘스트 열기 · 오른쪽 최상단 고정좌표 " +
            $"({CraftingHubLayout.ProductionQuestTopPoint.X},{CraftingHubLayout.ProductionQuestTopPoint.Y}) · " +
            $"제목 OCR 생략 · {plan.DisplayName}");

        await Task.Delay(CraftingHubLayout.ProductionQuestOpenDelayMs, ct);

        using var after = Capture(ct);
        double ratio = ProductionUiRuntime.MeasureVisualChangeRatio(
            before,
            after,
            CraftingHubLayout.ProductionQuestPopupArea);
        // Pixel changes can come from the animated field; they do not prove a quest opened.
        // The generic shortage dialog does not display the recipe name.
        int stablePopupFrames = 0;
        bool verified = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var popup = Capture(ct);
            stablePopupFrames = await IsQuestMaterialPopupAsync(popup, ct)
                ? stablePopupFrames + 1 : 0;
            if (stablePopupFrames >= 2)
            {
                verified = true;
                break;
            }
            await Task.Delay(300, ct);
        }
        if (!verified)
            throw Fail(after, "부족 재료 팝업 연속 2프레임 미확인 · 재료 OCR 및 추가 클릭 중단");
        Log?.Invoke($"[제작] 부족 재료 팝업 2프레임 확인 · 화면 변화 {ratio:P1} · 후속 재료행 OCR 진행");
    }

    public async Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
    {
        IReadOnlyList<CraftingQuestDeficit> gatherTargets = new[] { deficit };
        if (_registeredFoodBatch is not null && _registeredFoodPlan is not null)
        {
            var binding = await FindRegisteredFoodRowAsync(deficit, ct);
            if (binding.Targets.Count == 0) return;
            deficit = binding.Row;
            gatherTargets = binding.Targets;
        }
        async Task<(string Name, long Current, long Required)> ReadGatherStockAsync()
        {
            var stock = await _data.InventoryOnlyCountsWithLoadingRetryAsync(
                gatherTargets.Select(x => x.DisplayName), ct, Log);
            var reached = gatherTargets.FirstOrDefault(x => stock[x.DisplayName] >= x.Required)
                ?? gatherTargets[0];
            return (reached.DisplayName, stock[reached.DisplayName], reached.Required);
        }

        string label = string.IsNullOrWhiteSpace(deficit.DisplayName)
            ? $"부족 재료 행 {deficit.Current}/{deficit.Required}"
            : deficit.DisplayName;

        _stage.Move(ProductionStage.AcquireMaterial, label);

        // Material-name OCR is diagnostic only. The actionable identity is the
        // stable shortage ratio row that the game itself presents in the quest.
        // This lets a visible 0/60 row continue even when dim-gray "양배추" is
        // completely omitted by Windows OCR.
        using var beforeAcquire = Capture(ct);
        await ClickStableQuestDeficitRowAsync(deficit, ct);
        await Task.Delay(CraftingHubLayout.AcquisitionMethodSettleDelayMs, ct);

        int stablePopup = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var header = await FindUniqueAsync(
                frame,
                CraftingHubLayout.AcquisitionMethodHeaderArea,
                "구하는 방법",
                ct);
            double changeRatio = ProductionUiRuntime.MeasureVisualChangeRatio(
                beforeAcquire,
                frame,
                CraftingHubLayout.AcquisitionMethodPopupArea);

            bool popupSeen =
                header is not null ||
                changeRatio >= CraftingHubLayout.AcquisitionMethodOpenChangeRatio;
            if (popupSeen) stablePopup++;
            else stablePopup = 0;

            Log?.Invoke(
                $"[제작] 구하는 방법 팝업 구조 확인 · pass={pass + 1} · " +
                $"헤더OCR={(header is not null ? "확인" : "없음")} · " +
                $"화면변화={changeRatio:P1} · stable={stablePopup}/2");

            if (pass == 0)
                await Task.Delay(120, ct);
        }

        if (stablePopup < 2)
        {
            using var failed = Capture(ct);
            throw Fail(
                failed,
                $"{label} 부족 행 클릭 후 구하는 방법 팝업 전환을 안정적으로 확인하지 못했습니다.");
        }

        Log?.Invoke(
            $"[제작] 추천 첫 행 사용 · {label} · 재료명 OCR은 클릭 조건에서 제외");

        _stage.Move(ProductionStage.Travel, $"{label} 추천 획득처 이동/채집");
        _ui.ClickFresh(CraftingHubLayout.AcquisitionMethodRecommendedPoint, ct);
        Log?.Invoke(
            $"[제작] {label} 추천 획득처 선택 · 고정좌표 " +
            $"({CraftingHubLayout.AcquisitionMethodRecommendedPoint.X}," +
            $"{CraftingHubLayout.AcquisitionMethodRecommendedPoint.Y}) · " +
            $"퀘스트 필요수량 {deficit.Required}");

        DateTime deadline = DateTime.UtcNow.AddMinutes(12);
        int stableInventory = 0;
        int stableIdle = 0;
        int activityLoadingRejects = 0;
        int polls = 0;
        bool sawActivity = false;
        bool inventoryFallbackLogged = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            polls++;

            var activityResponse = await _cli.GetActivityAsync(ct);
            if (!activityResponse.Success)
            {
                if (CliAutomationGuards.IsTransientLoadingRejection(activityResponse))
                {
                    activityLoadingRejects++;
                    stableIdle = 0;
                    stableInventory = 0;
                    if (activityLoadingRejects == 1 || activityLoadingRejects % 5 == 0)
                        Log?.Invoke(
                            $"[제작] 지역 이동/채집 중 get_activity CLI 일시 거부 · 재시도 {activityLoadingRejects}회");
                    await Task.Delay(1000, ct);
                    continue;
                }

                _ = GatheringQueries.ParseActivity(activityResponse);
            }

            var activity = GatheringQueries.ParseActivity(activityResponse);
            if (!activity.IsSafeField)
                throw new InvalidOperationException(
                    "제작 재료 채집 중 안전하지 않은 상태가 확인되었습니다. 현재 배치 자동복구 대상으로 전환합니다.");

            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            if (active)
            {
                sawActivity = true;
                stableIdle = 0;
                stableInventory = 0;
                if (polls % 10 == 0)
                    Log?.Invoke(
                        $"[제작] {label} 이동/채집 진행 중 · 이동={activity.IsAutoTraveling} · " +
                        $"채집={activity.IsGathering || activity.IsFishing}");
                await Task.Delay(1000, ct);
                continue;
            }

            stableIdle++;

            if (!string.IsNullOrWhiteSpace(deficit.DisplayName))
            {
                try
                {
                    var observed = await ReadGatherStockAsync();
                    long current = observed.Current;
                    if (current >= observed.Required)
                    {
                        stableInventory++;
                        if (CraftingGatherCompletionPolicy.HasVerifiedTarget(
                            observed.Name, current, observed.Required, stableInventory))
                        {
                            Log?.Invoke(
                                $"[제작] {observed.Name} 준비 완료 · {current}/{observed.Required} · " +
                                "CLI 검증 성공 · 초과 허용");
                            _stage.Move(
                                ProductionStage.VerifyInventory,
                                $"{observed.Name} {current}/{observed.Required}");
                            return;
                        }
                    }
                    else
                    {
                        stableInventory = 0;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    stableInventory = 0;
                    if (!inventoryFallbackLogged)
                    {
                        inventoryFallbackLogged = true;
                        Log?.Invoke(
                            $"[제작] {label} 재고 CLI 검증 보조 실패 · {ex.Message} · " +
                            "활동 종료 후 퀘스트 부족 행 재확인으로 계속");
                    }
                }
            }

            // Auto-travel can briefly report idle before the game's automatic
            // gathering has finished (live 2026-10-20: parsnip 32/100).
            // Never open a quest or declare collection complete from idle alone.
            // A named material is complete ONLY after two fresh CLI inventory
            // observations reach the requested quantity (checked above).
            int requiredIdlePolls = sawActivity ? 5 : 8;
            if (stableIdle == requiredIdlePolls ||
                (stableIdle > requiredIdlePolls && stableIdle % 15 == 0))
                Log?.Invoke(
                    $"[제작] {label} 일시 idle={stableIdle} · 목표 재고 미확인 · " +
                    "게임 자동채집 대기 · 퀘스트창 재진입 금지");

            // Unnamed OCR rows cannot be verified by item identity. Do not
            // guess completion and do not click through an uncertain popup.
            if (CraftingGatherCompletionPolicy.MustStopUnnamed(
                deficit.DisplayName, stableIdle, sawActivity))
                throw new InvalidOperationException(
                    "재료명 미확인 상태에서 자동채집 완료를 검증할 수 없습니다. " +
                    "퀘스트창을 임의로 다시 열지 않고 안전 정지합니다.");

            await Task.Delay(1000, ct);
        }

        if (!string.IsNullOrWhiteSpace(deficit.DisplayName))
        {
            try
            {
                var observed = await ReadGatherStockAsync();
                long final = observed.Current;
                if (final >= observed.Required)
                {
                    Log?.Invoke(
                        $"[제작] {observed.Name} 제한시간 종료 직전 재고 검증 완료 · " +
                        $"{final}/{observed.Required}");
                    _stage.Move(
                        ProductionStage.VerifyInventory,
                        $"{deficit.DisplayName} {final}/{deficit.Required}");
                    return;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                Log?.Invoke(
                    $"[제작] {label} 제한시간 최종 재고 확인도 보조 실패 · {ex.Message}");
            }
        }

        // Named target with verified insufficient stock: material acquisition
        // failure. The caller may skip this recipe only after safe UI cleanup.
        if (!string.IsNullOrWhiteSpace(deficit.DisplayName))
            throw new CraftingMaterialAcquisitionException(
                deficit.DisplayName,
                $"{label} 추천 획득처 진행을 제한 시간 안에 확인하지 못했습니다. 현재 배치 자동복구 대상으로 전환합니다.");
        throw new InvalidOperationException(
            $"{label} 추천 획득처 진행을 제한 시간 안에 확인하지 못했습니다. 재료명 미확인으로 전체 안전 정지합니다.");
    }

    private async Task ClickStableQuestDeficitRowAsync(
        CraftingQuestDeficit deficit,
        CancellationToken ct)
    {
        int clickY = deficit.RowY;
        int? firstFreshY = null;
        int? secondFreshY = null;

        if (!CraftingHubLayout.ProductionQuestMaterialArea.Contains(
                new Point(CraftingHubLayout.QuestMaterialRowClickX, clickY)))
        {
            using var failed = Capture(ct);
            throw Fail(
                failed,
                $"저장된 부족 재료 행 좌표가 안전 영역을 벗어났습니다. " +
                $"{deficit.Current}/{deficit.Required} · 행Y={deficit.RowY}");
        }

        // The quest reader already established the shortage row. A fresh ratio OCR
        // is useful to refine its Y coordinate, but it must not become a second hard
        // gate: the live V3.0.32 failure read 감자 6/80 correctly, then omitted the
        // exact same ratio milliseconds later. If fresh OCR disappears, keep the
        // previously confirmed row Y and validate the resulting popup instead.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var lines = await _ui.Ocr.ReadLinesAsync(
                frame,
                CraftingHubLayout.ProductionQuestMaterialArea,
                3,
                ct);

            var matches = lines
                .Where(line => QuestRatioMatches(line, deficit))
                .OrderBy(line => Math.Abs(line.Center.Y - deficit.RowY))
                .ToArray();

            if (matches.Length == 1)
            {
                int freshY = matches[0].Center.Y;
                if (pass == 0) firstFreshY = freshY;
                else secondFreshY = freshY;
            }
            else
            {
                Log?.Invoke(
                    $"[제작] 부족 재료 행 클릭 직전 OCR 보조 미확인 · " +
                    $"{deficit.Current}/{deficit.Required} · 행Y={deficit.RowY} · " +
                    $"pass={pass + 1} · 후보={matches.Length} · 저장 행 위치 사용");
            }

            if (pass == 0)
                await Task.Delay(120, ct);
        }

        string source = "저장행";
        if (firstFreshY is int first && secondFreshY is int second &&
            Math.Abs(first - second) <= CraftingHubLayout.QuestMaterialRowStableTolerance)
        {
            clickY = second;
            source = "fresh 2프레임";
        }
        else if (secondFreshY is int latest)
        {
            clickY = latest;
            source = "fresh 보조 1프레임";
        }
        else if (firstFreshY is int firstOnly)
        {
            clickY = firstOnly;
            source = "fresh 보조 1프레임";
        }

        var click = new Point(
            CraftingHubLayout.QuestMaterialRowClickX,
            clickY);
        if (!CraftingHubLayout.ProductionQuestMaterialArea.Contains(click))
        {
            using var failed = Capture(ct);
            throw Fail(frame: failed, "부족 재료 행 클릭 좌표가 안전 영역을 벗어났습니다.");
        }

        _ui.ClickFresh(click, ct);
        Log?.Invoke(
            $"[제작] 부족 재료 행 선택 · {deficit.Current}/{deficit.Required} · " +
            $"행Y={clickY} · 기준={source} · 재료명=" +
            $"{(string.IsNullOrWhiteSpace(deficit.DisplayName) ? "OCR 미확인" : deficit.DisplayName)}");
    }

    private static bool QuestRatioMatches(
        DetectionResult line,
        CraftingQuestDeficit deficit)
    {
        if (Math.Abs(line.Center.Y - deficit.RowY) > 35)
            return false;

        string text = (line.ReadText ?? "").Replace(" ", "");
        if (text.Contains("제작대", StringComparison.Ordinal))
            return false;

        var match = Regex.Match(text, @"(?<cur>\d+)\s*/\s*(?<req>\d+)");
        return match.Success &&
               long.TryParse(match.Groups["cur"].Value, out long current) &&
               long.TryParse(match.Groups["req"].Value, out long required) &&
               current == deficit.Current &&
               required == deficit.Required;
    }

    public Task RecordMaterialFailureAsync(string reason, CancellationToken ct)
    {
        using var frame = Capture(ct);
        Log?.Invoke(Fail(frame, "[제작][건너뛰기 근거] " + reason).Message);
        return Task.CompletedTask;
    }

    public async Task RecoverBatchPreparationAsync(
        CraftingPlan plan,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _directCraftPendingName = null;
        _directCraftPendingCount = 0;
        _lastSingleQuestDeficit = null;
        _lastNamedDeficitRecipe = null;
        _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();

        int stableNeutralFrames = 0;
        for (int attempt = 1; attempt <= 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);

            var pharmacy = await _ui.Ocr.FindCompactLabelAsync(
                frame, CraftingHubLayout.ProductListHeaderArea, "약품 제작대", ct);
            var locationLines = await _ui.Ocr.ReadLinesAsync(
                frame, new Rectangle(200, 140, 560, 570), 2, ct);
            string locationText = string.Join("", locationLines.Select(x => x.ReadText ?? "")).Replace(" ", "");
            bool locationMenu = locationText.Contains("성장가이드", StringComparison.Ordinal) &&
                locationText.Contains("나의마이홈", StringComparison.Ordinal);
            if (pharmacy.Found || locationMenu)
            {
                stableNeutralFrames = 0;
                Log?.Invoke($"[제작][자동복구] 화면 정리 {attempt}/12 · 약품 제작대/장소 메뉴 확인 · Esc 후 재판정");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(500, ct);
                continue;
            }

            var acquisitionHeader = await FindUniqueAsync(
                frame,
                CraftingHubLayout.AcquisitionMethodHeaderArea,
                "구하는 방법",
                ct);
            var cancel = await FindUniqueAsync(
                frame,
                new Rectangle(100, 350, 600, 280),
                "취소",
                ct);
            if (acquisitionHeader is not null || cancel is not null)
            {
                stableNeutralFrames = 0;
                Log?.Invoke(
                    $"[제작][자동복구] 화면 정리 {attempt}/12 · 구하는 방법/모달 확인 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(450, ct);
                continue;
            }

            if (await IsQuestMaterialPopupAsync(frame, ct))
            {
                stableNeutralFrames = 0;
                Log?.Invoke(
                    $"[제작][자동복구] 화면 정리 {attempt}/12 · 제작 퀘스트 재료 팝업 확인 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(500, ct);
                continue;
            }

            if (await IsProductDetailAsync(frame, plan.DisplayName, ct))
            {
                stableNeutralFrames = 0;
                Log?.Invoke(
                    $"[제작][자동복구] 화면 정리 {attempt}/12 · {plan.DisplayName} 제작 상세 확인 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(500, ct);
                continue;
            }

            // K opens the common 가공/제작 hub on its processing tab first. A failed
            // bottom 제작-tab OCR can therefore leave either 가공 or 제작 visible.
            // Close either known hub/list state before attempting a fresh K entry.
            var craftingHeader = await _ui.Ocr.FindCompactLabelAsync(
                frame,
                CraftingHubLayout.HubHeaderArea,
                "제작",
                ct);
            var processingHeader = await _ui.Ocr.FindCompactLabelAsync(
                frame,
                CraftingHubLayout.HubHeaderArea,
                "가공",
                ct);
            var bottomCrafting = await FindUniqueAsync(
                frame,
                new Rectangle(285, 850, 320, 145),
                "제작",
                ct);

            if (craftingHeader.Found || processingHeader.Found || bottomCrafting is not null)
            {
                stableNeutralFrames = 0;
                string visible = craftingHeader.Found ? "제작" :
                    processingHeader.Found ? "가공" : "하단 제작 탭";
                Log?.Invoke(
                    $"[제작][자동복구] 화면 정리 {attempt}/12 · {visible} 허브/목록 확인 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(600, ct);
                continue;
            }

            // Absence of a recognized modal is not proof of the field. Require
            // positive HUD evidence on two independent captures before K.
            if (!AlteringScreen.HasManagedKEntryFieldHudVisual(frame))
            {
                stableNeutralFrames = 0;
                Log?.Invoke("[제작][자동복구] 필드 화면 미확인 · 복구 완료 판정 보류 · K 입력 금지");
                await Task.Delay(350, ct);
                continue;
            }
            stableNeutralFrames++;
            if (stableNeutralFrames >= 2)
            {
                Log?.Invoke(
                    "[제작][자동복구] 기준 화면 복귀 확인 · 필드 HUD 확인 2프레임 · " +
                    "다음 K 입력 허용");
                return;
            }

            Log?.Invoke(
                $"[제작][자동복구] 화면 정리 {attempt}/12 · 전환/일반 화면 재확인 · 입력 없음");
            await Task.Delay(350, ct);
        }

        using var failed = Capture(ct);
        throw Fail(
            failed,
            "자동복구 전 제작/퀘스트 화면을 안전한 기준 상태로 정리하지 못했습니다.");
    }

    private async Task<bool> IsQuestMaterialPopupAsync(
        Bitmap frame,
        CancellationToken ct)
    {
        var captionLines = await _ui.Ocr.ReadLinesAsync(
            frame, new Rectangle(175, 645, 455, 125), 3, ct);
        var caption = string.Join("", captionLines.Select(x => x.ReadText ?? ""));
        if (CraftingQuestText.IsShortageDialogCaption(caption))
        {
            Log?.Invoke("[제작] 기존 재료 부족 팝업 구조 확인 · 퀘스트/구하는 방법 문구 · 중복 열기 생략");
            return true;
        }

        var lines = await _ui.Ocr.ReadLinesAsync(
            frame,
            CraftingHubLayout.ProductionQuestMaterialArea,
            3,
            ct);
        if (lines.Any(line =>
            Regex.IsMatch(
                (line.ReadText ?? "").Replace(" ", ""),
                @"\d+\s*/\s*\d+")))
            return true;

        var itemHeader = await FindUniqueAsync(
            frame,
            CraftingHubLayout.ProductionQuestPopupArea,
            "필요한 아이템",
            ct);
        return itemHeader is not null;
    }

    public async Task CloseOverlayAsync(CancellationToken ct)
    {
        // ESC is idempotent for the supplied bottom/detail modals. Do not press it
        // when the persistent field/quest view is already visible.
        using var frame = Capture(ct);
        var modalHeader = await FindUniqueAsync(
            frame,
            CraftingHubLayout.AcquisitionMethodHeaderArea,
            "구하는 방법",
            ct);
        var cancel = await FindUniqueAsync(frame, new Rectangle(100, 350, 600, 280), "취소", ct);
        bool questPopup = await IsQuestMaterialPopupAsync(frame, ct);
        var pharmacy = await _ui.Ocr.FindCompactLabelAsync(
            frame, CraftingHubLayout.ProductListHeaderArea, "약품 제작대", ct);
        if (modalHeader is null && cancel is null && !questPopup && !pharmacy.Found)
            return;
        _ui.TapFresh(0x01, ct);
        await Task.Delay(300, ct);
    }

    // The user-confirmed crafting station action is at the fixed bottom-right
    // button region. Reading tiny "제작하기" text is NOT the input authority:
    // UI OCR sometimes misses it despite the button being present.
    // Reject wrong resolution, gray/inactive controls, and partial color patches.
    private static bool HasCraftReadyActionVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;
        Rectangle area = CraftingHubLayout.CraftActionButtonArea;
        int sampled = 0, cyanGreen = 0;
        int left = area.Right, top = area.Bottom;
        int right = area.Left, bottom = area.Top;
        for (int y = area.Top + 5; y < area.Bottom - 5; y += 3)
        for (int x = area.Left + 5; x < area.Right - 5; x += 3)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            if (p.G < 105 || p.B < 70 || p.R > 145 ||
                p.G < p.R + 25 || p.B < p.R + 5)
                continue;
            cyanGreen++;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return sampled > 0 && cyanGreen * 100 >= sampled * 18 &&
               right - left >= 110 && bottom - top >= 24;
    }

    private bool HasCraftReadyWithStationTransition(Bitmap frame)
    {
        if (!HasCraftReadyActionVisual(frame))
            return false;
        // The initial detail also has a teal '제작하러 가기' button at this
        // same coordinate. Do not treat it as the later '제작하기' button.
        if (_directCraftBeforeTravel is not null &&
            ProductionUiRuntime.MeasureVisualChangeRatio(
                _directCraftBeforeTravel, frame,
                CraftingHubLayout.ProductDetailPanelArea) < 0.15)
            return false;
        return true;
    }

    private void ClearQuestDeficits()
    {
        _lastSingleQuestDeficit = null;
        _lastNamedDeficitRecipe = null;
        _lastNamedDeficits = Array.Empty<CraftingQuestDeficit>();
    }

    private async Task<bool> HasCraftActionLabelAsync(Bitmap frame, CancellationToken ct)
    {
        if (!HasCraftReadyActionVisual(frame)) return false;
        var label = await _ui.Ocr.FindCompactLabelAsync(
            frame, CraftingHubLayout.CraftActionButtonArea, "제작하기", ct);
        return label.Found;
    }

    private async Task<bool> HasTwoFreshCraftReadyFramesAsync(Bitmap first, CancellationToken ct)
    {
        if (!HasCraftReadyWithStationTransition(first) ||
            !await HasCraftActionLabelAsync(first, ct))
            return false;
        await Task.Delay(180, ct);
        using var fresh = Capture(ct);
        // First-frame label distinguishes 제작하기 from 제작하러 가기.
        // A second fresh active button suffices; do not repeat the OCR hard gate.
        return HasCraftReadyWithStationTransition(fresh);
    }

    public async Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        if (string.Equals(_directCraftPendingName, plan.DisplayName, StringComparison.Ordinal) &&
            _directCraftPendingCount == craftCount)
        {
            await FinishDirectCraftAsync(plan, craftCount, ct);
            return;
        }

        _stage.Move(ProductionStage.Travel, $"{plan.DisplayName} 제작대 자동 복귀 대기");
        Log?.Invoke(
            $"[제작] 재료 확보 완료 · 마지막 퀘스트 클릭 후 게임 자동 복귀 대기 · " +
            $"추가 퀘스트/ESC/Space 입력 없음 · 기존 제작 횟수 {craftCount}회 유지");

        DateTime stationDeadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < stationDeadline)
        {
            ct.ThrowIfCancellationRequested();

            using var frame = Capture(ct);
            bool craftReady = await HasTwoFreshCraftReadyFramesAsync(frame, ct);

            if (craftReady)
            {
                long inventoryBefore = await _data.InventoryOnlyCountAsync(plan.DisplayName, ct);
                // An independent final capture guards against a disappeared
                // popup after CLI reads. No stale bitmap can authorize Space.
                using var immediate = Capture(ct);
                if (!HasCraftReadyWithStationTransition(immediate))
                    continue;
                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} {craftCount}회 제작");

                // The quest keeps the quantity selected when it was created.
                // Do not touch the count controls again after the automatic return.
                _ui.ClickFresh(CraftingHubLayout.CraftGoButtonPoint, ct); // 제작하기
                Log?.Invoke(
                    $"[제작] 제작대 도착 · 제작 버튼 화면 픽셀 2프레임+최종 확인 · 기존 {craftCount}회 유지 · " +
                    $"수량 재설정 없음 · 제작하기 클릭 · 완료판정 재고기준={inventoryBefore}");

                await WaitForCompletionAsync(plan.DisplayName, inventoryBefore, ct);
                return;
            }

            await Task.Delay(CraftingHubLayout.CraftReadyPollDelayMs, ct);
        }

        using var failed = Capture(ct);
        throw Fail(
            failed,
            "재료 확보 후 게임 자동 복귀를 기다렸지만 제작대의 제작하기 버튼이 열린 제작창을 확인하지 못했습니다.");
    }

    private async Task FinishDirectCraftAsync(
        CraftingPlan plan,
        int craftCount,
        CancellationToken ct)
    {
        _stage.Move(ProductionStage.Travel, $"{plan.DisplayName} 제작대 도착 대기");
        DateTime deadline = DateTime.UtcNow.AddMinutes(3);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            using var frame = Capture(ct);
            bool craftReady = await HasTwoFreshCraftReadyFramesAsync(frame, ct);

            if (craftReady)
            {
                long inventoryBefore = await _data.InventoryOnlyCountAsync(plan.DisplayName, ct);
                using var immediate = Capture(ct);
                if (!HasCraftReadyWithStationTransition(immediate))
                    continue;
                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} {craftCount}회 직접 제작");
                _ui.ClickFresh(CraftingHubLayout.CraftGoButtonPoint, ct); // 제작하기
                Log?.Invoke(
                    $"[제작] 제작대 도착 · 제작하기 버튼 즉시 확인 · 화면 픽셀 2프레임+최종 확인 · 기존 {craftCount}회 유지 · 제작하기 클릭 · 완료판정 재고기준={inventoryBefore}");
                await WaitForCompletionAsync(plan.DisplayName, inventoryBefore, ct);
                _directCraftBeforeTravel?.Dispose();
                _directCraftBeforeTravel = null;
                _directCraftPendingName = null;
                _directCraftPendingCount = 0;
                return;
            }

            await Task.Delay(CraftingHubLayout.CraftReadyPollDelayMs, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, "제작대로 이동 후 제작하기 버튼이 열린 제작 상세 화면을 확인하지 못했습니다.");
    }

    private DetectionResult? FindQuestTitleCandidate(
        IReadOnlyList<DetectionResult> lines,
        CraftingPlan plan)
    {
        var exact = lines
            .Where(x => CraftingQuestText.IsTitle(x.ReadText ?? "", plan.DisplayName))
            .OrderBy(x => x.Center.Y)
            .FirstOrDefault();
        if (exact.Found)
            return exact;

        // V3.0.8 live food quest: the orange right-side title was visible but
        // Windows OCR did not return the whole "야채볶음 제작" line. Within the
        // fixed quest list, the recipe name alone is sufficient and safer than
        // failing the already-created quest.
        var nameOnly = lines
            .Where(x => CraftingQuestText.IsTitleNameOnly(x.ReadText ?? "", plan.DisplayName))
            .OrderBy(x => x.Center.Y)
            .FirstOrDefault();
        if (nameOnly.Found)
        {
            Log?.Invoke(
                $"[제작] 퀘스트 제목 보조 인식 · 품목명만 확인 · {plan.DisplayName} · " +
                $"y={nameOnly.Center.Y}");
            return nameOnly;
        }

        // Final fallback uses the visible quest structure, not an arbitrary click:
        // one or more ingredient "준비 n/N" rows plus the facility "제작대에서 제작 n/N"
        // row means the newly-created production quest is present. Infer the title
        // immediately above the first progress row and still require two-frame
        // position stability before clicking it.
        var progress = lines
            .Where(x => CraftingQuestText.IsQuestProgress(x.ReadText ?? ""))
            .OrderBy(x => x.Center.Y)
            .ToArray();
        bool hasStation = progress.Any(x =>
            CraftingQuestText.IsStationStage(x.ReadText ?? ""));
        bool hasPreparation = progress.Any(x =>
            CraftingQuestText.Compact(x.ReadText ?? "").Contains("준비", StringComparison.Ordinal));

        if (hasPreparation && hasStation)
        {
            int inferredCenterY = Math.Max(165, progress[0].Bounds.Top - 26);
            var bounds = new Rectangle(680, inferredCenterY - 14, 110, 28);
            Log?.Invoke(
                $"[제작] 퀘스트 제목 구조 보조 인식 · 준비행+제작대행 확인 · " +
                $"추정 클릭 ({bounds.Left + bounds.Width / 2},{bounds.Top + bounds.Height / 2})");
            return new DetectionResult(
                true,
                bounds,
                0.5,
                plan.DisplayName + " 제작[구조]");
        }

        return null;
    }

    private async Task<DetectionResult?> FindQuestStageAsync(
        Bitmap frame, CraftingPlan plan, bool directOnly, CancellationToken ct)
    {
        var lines = await _ui.Ocr.ReadLinesAsync(
            frame,
            new Rectangle(500, 140, 300, 650),
            3,
            ct);
        var title = FindQuestTitleCandidate(lines, plan);
        if (title is null)
            return null;

        var stages = lines
            .Where(x => x.Center.Y > title.Value.Center.Y &&
                        x.Center.Y <= title.Value.Bounds.Bottom + 150)
            .Where(x => CraftingQuestText.IsDirectStage(x.ReadText ?? "") ||
                (!directOnly && CraftingQuestText.IsStationStage(x.ReadText ?? "")))
            .ToArray();
        return stages.Length == 1 ? stages[0] : null;
    }

    private async Task WaitForCompletionAsync(
        string displayName,
        long inventoryBefore,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        DateTime nextInventoryCheck = DateTime.MinValue;
        long latestInventory = inventoryBefore;
        long lastLoggedInventory = inventoryBefore;
        int stableCompletionPopup = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            // Inventory changes are progress/diagnostic information only.
            // A great success can make the output count reach or exceed the requested
            // quantity before every queued craft has finished, so inventory must never
            // be used as a completion signal.
            if (DateTime.UtcNow >= nextInventoryCheck)
            {
                try
                {
                    latestInventory = await _data.InventoryOnlyCountAsync(displayName, ct);
                    if (latestInventory != lastLoggedInventory)
                    {
                        Log?.Invoke(
                            $"[제작] 제작 진행 중 · 재고 {inventoryBefore}->{latestInventory} · " +
                            "완료창 대기 · 재고 증가는 완료 판정에 사용하지 않음");
                        lastLoggedInventory = latestInventory;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    Log?.Invoke($"[제작] 진행 재고 확인 보조 실패 · {ex.Message}");
                }
                nextInventoryCheck = DateTime.UtcNow.AddSeconds(1);
            }

            using var frame = Capture(ct);
            var complete = await FindUniqueAsync(
                frame,
                CraftingHubLayout.CraftCompletionHeaderArea,
                "제작 완료",
                ct);
            var confirm = await FindUniqueAsync(
                frame,
                CraftingHubLayout.CraftCompletionConfirmArea,
                "확인",
                ct);

            // The visible result popup is the only completion authority.
            // Do not infer completion from inventory gain or busy/idle transitions.
            bool completionPopupVisible =
                complete is not null ||
                confirm is not null;

            if (!completionPopupVisible)
            {
                stableCompletionPopup = 0;
                await Task.Delay(600, ct);
                continue;
            }

            stableCompletionPopup++;
            Log?.Invoke(
                $"[제작] 완료창 후보 · 제작완료OCR={(complete is not null ? "확인" : "없음")} · " +
                $"확인OCR={(confirm is not null ? "확인" : "없음")} · " +
                $"재고={inventoryBefore}->{latestInventory} · stable={stableCompletionPopup}/2");

            if (stableCompletionPopup < 2)
            {
                await Task.Delay(350, ct);
                continue;
            }

            using (var fresh = Capture(ct))
            {
                var freshComplete = await FindUniqueAsync(
                    fresh,
                    CraftingHubLayout.CraftCompletionHeaderArea,
                    "제작 완료",
                    ct);
                var freshConfirm = await FindUniqueAsync(
                    fresh,
                    CraftingHubLayout.CraftCompletionConfirmArea,
                    "확인",
                    ct);

                if (freshComplete is null && freshConfirm is null)
                {
                    stableCompletionPopup = 0;
                    await Task.Delay(450, ct);
                    continue;
                }
            }

            _stage.Move(ProductionStage.Complete, $"{displayName} 제작 완료");
            _ui.TapFresh(0x39, ct); // Space = actual result popup confirm only
            Log?.Invoke(
                $"[제작] 실제 제작 완료창 확인 · 2프레임+fresh 확인 · Space 확인 · " +
                $"재고 {inventoryBefore}->{latestInventory}");
            await Task.Delay(600, ct);
            return;
        }

        using var failed = Capture(ct);
        throw Fail(
            failed,
            $"{displayName} 실제 제작 완료/확인 창을 제한 시간 안에 확인하지 못했습니다. " +
            $"재고 변화는 완료 판정에 사용하지 않았습니다: {inventoryBefore}->{latestInventory}");
    }

    private async Task ClickExactAsync(
        string text,
        Rectangle roi,
        CancellationToken ct,
        string failure,
        string? pasteText = null)
    {
        if (pasteText is null)
        {
            _ = await _ui.ClickStableExactAsync(
                text, roi, ct, failure, dimText: true);
        }
        else
        {
            _ = await _ui.ClickStableExactAndPasteAsync(
                text, roi, pasteText, ct, failure, dimText: true);
        }
    }

    private Task<DetectionResult?> FindUniqueAsync(
        Bitmap frame, Rectangle roi, string text, CancellationToken ct)
        => _ui.FindUniqueAsync(frame, roi, text, ct, dimText: true);

    private InvalidOperationException Fail(Bitmap frame, string message)
        => _ui.Failure(frame, message);

    public void Dispose() => _ui.Dispose();
}
