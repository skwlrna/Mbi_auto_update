using FishingAutomation;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

internal sealed class CraftingScreen : ICraftingScreen
{
    private readonly CraftingCliData _data;
    private readonly MabinogiMobileCli _cli;
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("제작");
    private string? _directCraftPendingName;
    private int _directCraftPendingCount;
    private CraftingQuestDeficit? _lastSingleQuestDeficit;
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
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[제작][상태] {stage} · {detail}");
    }

    public string InputMode => _ui.InputMode;
    public event Action<string>? Log;

    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);

    public async Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        plan.Validate();
        if (craftCount is < 1 or > 10)
            throw new InvalidDataException("제작은 한 번에 1~10회만 설정할 수 있습니다.");
        if (!CraftingHubLayout.IsSafeCraftDetailGeometry())
            throw new InvalidOperationException("제작 상세 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        _directCraftPendingName = null;
        _directCraftPendingCount = 0;

        await OpenProductAsync(plan, ct);
        await SetCraftCountAsync(plan.DisplayName, craftCount, ct);

        // CLI Craftable reflects whether the recipe is currently craftable at all
        // (effectively one craft in the live catalog). For a selected 2~10 craft batch,
        // the detail UI is authoritative because it already scales every material
        // requirement to the selected count (e.g. 65/80 for a 10-craft batch).
        var exact = await _data.ExactAsync(plan.DisplayName, ct);
        var materialState = await ReadSelectedBatchMaterialStateAsync(craftCount, ct);
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

    private async Task<(bool Parsed, bool AllEnough, string Summary)> ReadSelectedBatchMaterialStateAsync(
        int craftCount,
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
                return (false, false, "재료 비율 OCR 없음");
            }

            if (first is null)
            {
                first = ratios;
                await Task.Delay(180, ct);
                continue;
            }

            if (first.Count != ratios.Count ||
                !first.SequenceEqual(ratios))
            {
                Log?.Invoke(
                    $"[제작] 선택 {craftCount}회 상세 재료 수량 2프레임 불일치 · 안전하게 퀘스트 경로 사용");
                return (false, false, "재료 비율 2프레임 불일치");
            }

            bool allEnough = ratios.All(x => x.Owned >= x.Required);
            string summary = string.Join(", ", ratios.Select(x => $"{x.Owned}/{x.Required}"));
            return (true, allEnough, summary);
        }

        return (false, false, "재료 비율 확인 실패");
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

        DetectionResult? firstHeader = null;
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);

            var header = await _ui.Ocr.FindCompactLabelAsync(
                frame, CraftingHubLayout.HubHeaderArea, "제작", ct);
            if (!header.Found)
            {
                var exact = await _ui.Ocr.FindAlteringLabelsAsync(
                    frame,
                    CraftingHubLayout.HubHeaderArea,
                    "제작",
                    ct,
                    acceptedBounds: CraftingHubLayout.HubHeaderArea,
                    dimText: true);
                if (exact.Count != 1)
                    throw Fail(frame,
                        "제작 허브 상단 제목을 확인하지 못해 고정 카드 좌표 클릭을 중단합니다.");
                header = exact[0];
            }

            if (pass == 0)
            {
                firstHeader = header;
                await Task.Delay(150, ct);
                continue;
            }

            if (firstHeader is null ||
                !CraftingHubLayout.IsStableTitle(firstHeader.Value.Bounds, header.Bounds))
                throw Fail(frame,
                    "제작 허브 상단 제목 위치가 두 프레임에서 안정적으로 일치하지 않았습니다.");

            // 800x1000 제작 허브는 카드 위치가 고정이다. OCR은 허브 상태
            // 확인에만 사용하고 음식/아이템 선택 좌표는 저장된 카드 중심을 쓴다.
            _ui.ClickFresh(fixedPoint, ct);
            Log?.Invoke(
                $"[제작] 제작 허브 카드 선택 · {label} · 고정좌표 ({fixedPoint.X},{fixedPoint.Y})");
            return;
        }

        using var failed = Capture(ct);
        throw Fail(failed, $"제작 허브에서 {label} 카드를 선택하지 못했습니다.");
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

        _ui.ClickFresh(CraftingHubLayout.ProductSearchInputPoint, ct);
        Log?.Invoke(
            $"[제작] 검색 입력칸 · 고정좌표 ({CraftingHubLayout.ProductSearchInputPoint.X},{CraftingHubLayout.ProductSearchInputPoint.Y})");
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
        if (await IsProductDetailAsync(verify, displayName, ct))
        {
            Log?.Invoke($"[제작] {displayName} 상세 화면 확인 · 검색 단계 완료");
            return;
        }

        throw Fail(
            verify,
            $"첫 결과 클릭 후 {displayName} 제작 상세 화면을 확인하지 못했습니다. 추가 fallback 클릭 없이 정지");
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
        CraftingPlan plan,
        CancellationToken ct)
    {
        if (string.Equals(_directCraftPendingName, plan.DisplayName, StringComparison.Ordinal))
        {
            _lastSingleQuestDeficit = null;
            Log?.Invoke("[제작] 재료 충분 직접 제작 · 퀘스트 재료 확인 생략");
            return Array.Empty<CraftingQuestDeficit>();
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

            if (string.IsNullOrWhiteSpace(name))
                throw Fail(
                    frame,
                    $"제작 퀘스트 재료 이름을 확인하지 못했습니다. 수량={ratio.Current}/{ratio.Required} · 행Y={ratio.Y}");

            name = await CanonicalizeQuestMaterialNameAsync(name!, plan.DisplayName, ct);
            if (ratio.Current < ratio.Required)
                deficits.Add(new(name, ratio.Current, ratio.Required, ratio.Y));
        }

        _lastSingleQuestDeficit = deficits.Count == 1 ? deficits[0] : null;
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

        var exact = _questMaterialNameCatalog
            .Where(x => string.Equals(FuzzyText.Normalize(x), normalizedOcr, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (exact.Length == 1)
            return exact[0];
        if (exact.Length > 1)
            throw new InvalidOperationException(
                $"제작 퀘스트 재료명 '{ocrName}'과 정확히 일치하는 실제 품목명이 여러 개라 자동 선택하지 않습니다.");

        // Korean quest material OCR commonly substitutes or drops one syllable
        // on the dim-gray row text (live examples: 감사->감자, 양배->양배추).
        // Never use substring fuzzy matching here: a short OCR token such as "양배"
        // otherwise matches unrelated one/two-syllable catalog entries.
        string? recipeCandidate = ChooseCanonicalQuestMaterial(
            ocrName,
            _questRecipeMaterialNames,
            out string recipeDiagnostic);
        if (recipeCandidate is not null)
        {
            Log?.Invoke(
                $"[제작] 퀘스트 재료명 OCR 보정 · {ocrName} → {recipeCandidate} · " +
                $"현재 레시피 재료 우선 · {recipeDiagnostic}");
            return recipeCandidate;
        }

        string? catalogCandidate = ChooseCanonicalQuestMaterial(
            ocrName,
            _questMaterialNameCatalog,
            out string catalogDiagnostic);
        if (catalogCandidate is not null)
        {
            Log?.Invoke(
                $"[제작] 퀘스트 재료명 OCR 보정 · {ocrName} → {catalogCandidate} · " +
                $"전체 문자열 유일 최상위 후보 · {catalogDiagnostic}");
            return catalogCandidate;
        }

        if (catalogDiagnostic.StartsWith("ambiguous:", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"제작 퀘스트 재료명 '{ocrName}'의 실제 품목 최상위 후보가 여러 개라 자동 보정하지 않습니다: " +
                catalogDiagnostic["ambiguous:".Length..]);

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
        _questRecipeMaterialNames = crafting
            .SelectMany(item => item.MissingIngredients)
            .Select(ingredient => ingredient.DisplayName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
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

    private static string? ChooseCanonicalQuestMaterial(
        string ocrName,
        IEnumerable<string> candidates,
        out string diagnostic)
    {
        string needle = FuzzyText.Normalize(ocrName);
        if (needle.Length == 0)
        {
            diagnostic = "none";
            return null;
        }

        var scored = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Select(name =>
            {
                string normalized = FuzzyText.Normalize(name);
                int distance = FullEditDistance(needle, normalized);
                bool fullPrefix = normalized.StartsWith(needle, StringComparison.Ordinal);
                int prefix = CommonPrefixLength(needle, normalized);
                int lengthGap = Math.Abs(normalized.Length - needle.Length);
                return new
                {
                    Name = name,
                    Distance = distance,
                    FullPrefix = fullPrefix,
                    Prefix = prefix,
                    LengthGap = lengthGap
                };
            })
            .Where(x => x.Distance <= 1)
            .OrderBy(x => x.Distance)
            .ThenByDescending(x => x.FullPrefix)
            .ThenByDescending(x => x.Prefix)
            .ThenBy(x => x.LengthGap)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        if (scored.Length == 0)
        {
            diagnostic = "none";
            return null;
        }

        var best = scored[0];
        var tied = scored
            .Where(x =>
                x.Distance == best.Distance &&
                x.FullPrefix == best.FullPrefix &&
                x.Prefix == best.Prefix &&
                x.LengthGap == best.LengthGap)
            .ToArray();

        if (tied.Length != 1)
        {
            diagnostic = "ambiguous:" + string.Join(", ", tied.Select(x => x.Name));
            return null;
        }

        diagnostic =
            $"거리={best.Distance} · 전체접두={(best.FullPrefix ? "예" : "아니오")} · " +
            $"공통접두={best.Prefix} · 길이차={best.LengthGap}";
        return best.Name;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        int limit = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    private static int FullEditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(
                    Math.Min(cur[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
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
        Log?.Invoke($"[제작] 제작 퀘스트 팝업 전환 · 화면 변화 {ratio:P1} · 후속 재료행 OCR 진행");
    }

    public async Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
    {
        _stage.Move(ProductionStage.AcquireMaterial, deficit.DisplayName);
        // Re-read the exact material on two fresh popup frames. RowY is only a
        // narrowing hint; it is never a saved click coordinate.
        var deficitRoi = new Rectangle(
            70,
            Math.Max(120, deficit.RowY - 70),
            650,
            140);
        _ = await _ui.ClickStableExactAsync(
            deficit.DisplayName,
            deficitRoi,
            ct,
            $"부족 재료 {deficit.DisplayName}의 정확한 행을 다시 확인하지 못했습니다.",
            dimText: true);

        await Task.Delay(CraftingHubLayout.AcquisitionMethodSettleDelayMs, ct);

        bool anyHeaderSeen = false;
        bool firstMaterialSeen = false;
        bool secondMaterialSeen = false;
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var header = await FindUniqueAsync(
                frame,
                CraftingHubLayout.AcquisitionMethodHeaderArea,
                "구하는 방법",
                ct);
            anyHeaderSeen |= header is not null;

            var lines = await _ui.Ocr.ReadLinesAsync(
                frame,
                CraftingHubLayout.AcquisitionMethodListArea,
                3,
                ct);

            // The first row is the game's recommended route. The yellow "추천"
            // badge is decorative and can disappear from OCR between frames.
            // Authorize the fixed click from the stable material name in that
            // recommended-row geometry; require the popup header only once.
            bool recommendedSeen = lines.Any(x =>
                AcquisitionMethodPolicy.IsRecommended(x.ReadText));
            bool materialSeen = lines.Any(x =>
                CraftingHubLayout.AcquisitionMethodRecommendedRowArea.Contains(x.Center) &&
                (x.ReadText ?? "").Replace(" ", "")
                    .Contains(deficit.DisplayName.Replace(" ", ""), StringComparison.Ordinal));

            Log?.Invoke(
                $"[제작] 구하는 방법 팝업 확인 · pass={pass + 1} · " +
                $"헤더={(header is not null ? "확인" : "없음")} · " +
                $"추천={(recommendedSeen ? "확인" : "없음")} · " +
                $"{deficit.DisplayName}={(materialSeen ? "확인" : "없음")}");

            if (pass == 0)
            {
                firstMaterialSeen = materialSeen;
                await Task.Delay(120, ct);
            }
            else
            {
                secondMaterialSeen = materialSeen;
            }
        }

        if (!anyHeaderSeen || !firstMaterialSeen || !secondMaterialSeen)
        {
            using var failed = Capture(ct);
            throw Fail(
                failed,
                $"{deficit.DisplayName} 구하는 방법 팝업의 추천 첫 행 재료명을 2프레임 안정적으로 확인하지 못했습니다.");
        }

        Log?.Invoke(
            $"[제작] 추천 첫 행 안정 확인 · {deficit.DisplayName} 2/2프레임 · " +
            "추천 글자 OCR은 클릭 조건에서 제외");

        _stage.Move(ProductionStage.Travel, $"{deficit.DisplayName} 추천 획득처 이동/채집");
        _ui.ClickFresh(CraftingHubLayout.AcquisitionMethodRecommendedPoint, ct);
        Log?.Invoke(
            $"[제작] {deficit.DisplayName} 추천 획득처 선택 · 고정좌표 " +
            $"({CraftingHubLayout.AcquisitionMethodRecommendedPoint.X}," +
            $"{CraftingHubLayout.AcquisitionMethodRecommendedPoint.Y}) · 필요 {deficit.Required}개");

        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        int stable = 0;
        int activityLoadingRejects = 0;
        int polls = 0;
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
                    stable = 0;
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
                throw new InvalidOperationException("제작 재료 채집 중 안전하지 않은 상태가 확인되어 정지합니다.");

            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            if (active)
            {
                stable = 0;
                if (polls % 10 == 0)
                    Log?.Invoke(
                        $"[제작] {deficit.DisplayName} 이동/채집 진행 중 · 재고 CLI 조회 생략 · " +
                        $"이동={activity.IsAutoTraveling} · 채집={activity.IsGathering || activity.IsFishing}");
                await Task.Delay(1000, ct);
                continue;
            }

            // Match the automatic-altering travel guard: loading-time CLI rejection is
            // transient. Inventory is checked only after travel/gathering is no longer active.
            long current = await _data.InventoryOnlyCountWithLoadingRetryAsync(
                deficit.DisplayName, ct, Log);
            if (current >= deficit.Required)
            {
                stable++;
                if (stable >= 2)
                {
                    Log?.Invoke($"[제작] {deficit.DisplayName} 준비 완료 · {current}/{deficit.Required} · 초과 허용");
                    _stage.Move(ProductionStage.VerifyInventory, $"{deficit.DisplayName} {current}/{deficit.Required}");
                    return;
                }
            }
            else
            {
                stable = 0;
            }

            await Task.Delay(1000, ct);
        }

        long final = await _data.InventoryOnlyCountWithLoadingRetryAsync(
            deficit.DisplayName, ct, Log);
        throw new InvalidOperationException(
            $"{deficit.DisplayName} 제작 퀘스트 채집이 필요한 수량에 도달하지 못했습니다: {final}/{deficit.Required}");
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
        if (modalHeader is null && cancel is null)
            return;
        _ui.TapFresh(0x01, ct);
        await Task.Delay(300, ct);
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
            var craftReady = await FindUniqueAsync(
                frame,
                CraftingHubLayout.CraftActionButtonArea,
                "제작하기",
                ct);

            if (craftReady is not null)
            {
                long inventoryBefore = await _data.InventoryOnlyCountAsync(plan.DisplayName, ct);
                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} {craftCount}회 제작");

                // The quest keeps the quantity selected when it was created.
                // Do not touch the count controls again after the automatic return.
                _ui.TapFresh(0x39, ct); // Space = 제작하기
                Log?.Invoke(
                    $"[제작] 제작대 도착 · 제작창 자동 표시 확인 · 기존 {craftCount}회 유지 · " +
                    $"수량 재설정 없음 · Space 제작 시작 · 완료판정 재고기준={inventoryBefore}");

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
            var craftReady = await FindUniqueAsync(
                frame,
                CraftingHubLayout.CraftActionButtonArea,
                "제작하기",
                ct);

            if (craftReady is not null)
            {
                long inventoryBefore = await _data.InventoryOnlyCountAsync(plan.DisplayName, ct);
                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} {craftCount}회 직접 제작");
                _ui.TapFresh(0x39, ct); // Space = 제작하기
                Log?.Invoke(
                    $"[제작] 제작대 도착 · 제작하기 버튼 즉시 확인 · 기존 {craftCount}회 유지 · Space 제작 시작 · 완료판정 재고기준={inventoryBefore}");
                await WaitForCompletionAsync(plan.DisplayName, inventoryBefore, ct);
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
