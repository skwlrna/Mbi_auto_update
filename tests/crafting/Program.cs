using FishingAutomation;
using System.Text;
using System.Text.Json;

try
{
    int checks = 0;
    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }
    Check(CraftingQuestText.IsTitle("◆ 캠프파이어 키트 제작", "캠프파이어 키트") &&
          !CraftingQuestText.IsTitle("숙련 캠프파이어 키트 제작", "캠프파이어 키트"),
        "quest title exact match rejects prefixed recipe names");
    Check(CraftingQuestText.IsTitleNameOnly("야채볶음", "야채볶음") &&
          !CraftingQuestText.IsTitleNameOnly("숙련 야채볶음", "야채볶음"),
        "quest title fallback accepts the exact recipe name when 제작 OCR is split");
    Check(CraftingQuestText.IsShortageDialogCaption(
              "퀘스트를 위해 필요한 아이템이 없습니다. 구하는 방법을 보시겠습니까?") &&
          !CraftingQuestText.IsShortageDialogCaption("퀘스트 수락 구하는 방법"),
        "existing shortage modal is recognized even when material-ratio OCR is missing");
    Check(CraftingQuestText.IsQuestProgress("감자 준비 7/80") &&
          CraftingQuestText.IsQuestProgress("음식 제작대에서 제작 0/10") &&
          !CraftingQuestText.IsQuestProgress("모험가 길드 퀘스트"),
        "quest structure fallback recognizes preparation and station progress rows");
    Check(CraftingQuestText.IsDirectStage("• 바로 제작 진행") &&
          !CraftingQuestText.IsDirectStage("제작 진행"),
        "ready-material quest uses the observed direct crafting stage");
    Check(CraftingQuestText.IsStationStage("다목적 제작대에서 제작 0/10") &&
          !CraftingQuestText.IsStationStage("다목적 제작대 사용 중"),
        "station progress stage cannot match crafting-busy state");
    Check(CraftingQuestText.IsStationStage("• 약품 제작대에서 제작") &&
          !CraftingQuestText.IsStationStage("바로 제작 진행"),
        "observed bandage quest omits ratio and stays separate from instant crafting");


    // V3.2.72 10:41: potato was gathered 8/60 -> 66/60, while
    // the remaining onion 6/60 row lost its dim OCR label.
    var priorPumpkinSoup = new[]
    {
        new DungeonVisionBot.CraftingQuestDeficit("감자", 8, 60, 777),
        new DungeonVisionBot.CraftingQuestDeficit("양파", 6, 60, 822)
    };
    var unnamedOnion = new[] { new DungeonVisionBot.CraftingQuestDeficit("", 6, 60, 778) };
    var freshAfterPotato = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["감자"] = 66, ["양파"] = 6
    };
    var recoveredOnion = DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup, unnamedOnion, freshAfterPotato);
    Check(recoveredOnion?.DisplayName == "양파" &&
          recoveredOnion.Current == 6 && recoveredOnion.RowY == 778,
        "10:41 pumpkin soup recovers only known 6/60 onion after CLI proves 66/60 potato");

    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup, unnamedOnion,
        new Dictionary<string, long> { ["감자"] = 8, ["양파"] = 6 }) is null,
        "still-short potato must block replacing unknown 6/60 row");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup, unnamedOnion,
        new Dictionary<string, long> { ["감자"] = 66, ["양파"] = 7 }) is null,
        "stale onion inventory must never authorize recovered identity");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup, unnamedOnion,
        new Dictionary<string, long> { ["양파"] = 6 }) is null,
        "missing inventory proof for another prior shortage fails closed");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup,
        new[] { new DungeonVisionBot.CraftingQuestDeficit("", 7, 60, 778) },
        freshAfterPotato) is null,
        "changed shortage ratio is not guessed as an older ingredient");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup,
        new[] { new DungeonVisionBot.CraftingQuestDeficit("", 6, 70, 778) },
        freshAfterPotato) is null,
        "changed batch requirement is rejected");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup,
        new[] { new DungeonVisionBot.CraftingQuestDeficit("", 6, 60, 778),
                new DungeonVisionBot.CraftingQuestDeficit("", 8, 60, 815) },
        freshAfterPotato) is null,
        "two simultaneous nameless rows are never inferred");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        new[]
        {
            new DungeonVisionBot.CraftingQuestDeficit("양파", 6, 60, 777),
            new DungeonVisionBot.CraftingQuestDeficit("양배추", 6, 60, 822)
        }, unnamedOnion,
        new Dictionary<string, long> { ["양파"] = 6, ["양배추"] = 6 }) is null,
        "equal-count distinct materials remain ambiguous even with fresh CLI");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        Array.Empty<DungeonVisionBot.CraftingQuestDeficit>(), unnamedOnion,
        freshAfterPotato) is null,
        "new quest or missing previous named OCR never reuses an old material");
    Check(DungeonVisionBot.CraftingQuestMaterialRecoveryPolicy.IdentifyUnique(
        priorPumpkinSoup,
        new[]
        {
            new DungeonVisionBot.CraftingQuestDeficit("소금", 1, 60, 730),
            new DungeonVisionBot.CraftingQuestDeficit("", 6, 60, 778)
        },
        new Dictionary<string, long> { ["감자"] = 66, ["양파"] = 6, ["소금"] = 1 }) is null,
        "unexpected named row indicates a changed quest and blocks identity recovery");

    Check(!CraftingGatherCompletionPolicy.HasVerifiedTarget("파스닙", 32, 100, 5) &&
          !CraftingGatherCompletionPolicy.HasVerifiedTarget("파스닙", 100, 100, 1) &&
          CraftingGatherCompletionPolicy.HasVerifiedTarget("파스닙", 100, 100, 2),
        "auto gather cannot return at idle 32/100; requires two fresh sufficient CLI reads");
    Check(!CraftingGatherCompletionPolicy.HasVerifiedTarget("", 100, 100, 2) &&
          CraftingGatherCompletionPolicy.MustStopUnnamed("", 5, true) &&
          !CraftingGatherCompletionPolicy.MustStopUnnamed("허브", 100, true),
        "unnamed material cannot be treated as completed from idle alone");

    var telegramExample = new CraftingTelegramSnapshot(
        new CraftingTelegramItem[]
        {
            new("음식", "가든 샐러드", 60, 60, 6, 6,
                CraftingTelegramPhase.Completed, "완료", null),
            new("음식", "생선구이", 5, 50, 1, 10,
                CraftingTelegramPhase.Active, "소금 0/10 · 퀘스트 채집", null),
            new("아이템", "붕대", 0, 20, 0, 4,
                CraftingTelegramPhase.Pending, "순서 대기", null)
        },
        1, "생선구이 부족 재료 처리", DateTimeOffset.Now, true, null);
    string activeTelegram = CraftingTelegramStatus.Format(telegramExample);
    Check(activeTelegram.Contains("현재 순서: 2/3 · 생선구이", StringComparison.Ordinal) &&
          activeTelegram.Contains("목표 합계: 65/130개", StringComparison.Ordinal) &&
          activeTelegram.Contains("소금 0/10", StringComparison.Ordinal) &&
          activeTelegram.Contains("남은 제작 9회", StringComparison.Ordinal) &&
          activeTelegram.Contains("□ 대기", StringComparison.Ordinal),
        "telegram 상태 reports verified per-recipe quantities, stage and pending queue");
    var skippedTelegram = telegramExample with
    {
        Items = new CraftingTelegramItem[]
        {
            telegramExample.Items[0],
            telegramExample.Items[1] with
            {
                Phase = CraftingTelegramPhase.Skipped,
                Reason = "상점 구매 재료 부족: 소금 10개"
            },
            telegramExample.Items[2] with
            {
                Phase = CraftingTelegramPhase.Active,
                Stage = "다음 품목 제작 준비"
            }
        },
        ActiveIndex = 2,
        LastAction = "세 번째 품목 시작"
    };
    string skippedStatus = CraftingTelegramStatus.Format(skippedTelegram);
    Check(skippedStatus.Contains("재료 부족 건너뜀 1종", StringComparison.Ordinal) &&
          skippedStatus.Contains("상점 구매 재료 부족: 소금 10개", StringComparison.Ordinal) &&
          skippedStatus.Contains("현재 순서: 3/3 · 붕대", StringComparison.Ordinal),
        "telegram 상태 preserves skip reason and proceeds to third recipe");
    var stoppedTelegram = skippedTelegram with
    {
        Items = skippedTelegram.Items.Select(x =>
            x.DisplayName == "붕대"
                ? x with { Phase = CraftingTelegramPhase.Stopped, Reason = "정지 요청" }
                : x).ToArray(),
        ActiveIndex = -1,
        Running = false,
        Error = "정지 요청"
    };
    Check(CraftingTelegramStatus.Format(stoppedTelegram).Contains(
              "정지/오류 사유: 정지 요청", StringComparison.Ordinal),
        "telegram 상태 distinguishes stopped run without claiming completion");
    var manyRows = Enumerable.Range(0, 200).Select(i =>
        new CraftingTelegramItem("음식", "레시피" + i, 0, 100, 0, 10,
            CraftingTelegramPhase.Pending, "대기", null)).ToArray();
    string truncatedTelegram = CraftingTelegramStatus.Format(telegramExample with
    {
        Items = manyRows,
        ActiveIndex = -1
    });
    Check(truncatedTelegram.Length < 3100 &&
          truncatedTelegram.Contains("나머지 품목 생략", StringComparison.Ordinal),
        "telegram crafting details are line-capped before the global message limit");

    var serialFirst = new CraftingPlan(CraftingCategory.Food, "가든 샐러드", 60, 1);
    var serialSecond = new CraftingPlan(CraftingCategory.Food, "생선구이", 50, 1);
    var order = new List<string>();
    await CraftingQueueCoordinator.RunAsync(new[] { serialFirst, serialSecond },
        async (recipe, index, ct) =>
        {
            order.Add("start:" + recipe.DisplayName);
            await Task.Yield();
            order.Add("verified:" + recipe.DisplayName);
        }, CancellationToken.None);
    Check(order.SequenceEqual(new[]
    {
        "start:가든 샐러드", "verified:가든 샐러드",
        "start:생선구이", "verified:생선구이"
    }), "queued recipes are executed strictly one at a time");

    var serialThird = new CraftingPlan(CraftingCategory.Food, "버섯볶음", 20, 1);
    order.Clear();
    var skipResult = await CraftingQueueCoordinator.RunAsync(
        new[] { serialFirst, serialSecond, serialThird },
        (recipe, index, ct) =>
        {
            order.Add("start:" + recipe.DisplayName);
            if (index == 1)
                throw new CraftingMaterialAcquisitionException("소금", "소금 부족");
            order.Add("verified:" + recipe.DisplayName);
            return Task.CompletedTask;
        }, CancellationToken.None,
        (recipe, index, error, ct) =>
        {
            order.Add("skipped:" + recipe.DisplayName + ":" + error.MaterialName);
            return Task.CompletedTask;
        });
    Check(skipResult.Completed == 2 && skipResult.Skipped == 1 &&
          order.SequenceEqual(new[]
          {
              "start:가든 샐러드", "verified:가든 샐러드",
              "start:생선구이", "skipped:생선구이:소금",
              "start:버섯볶음", "verified:버섯볶음"
          }), "unavailable material skips only the affected recipe and continues to the third");

    order.Clear();
    try
    {
        await CraftingQueueCoordinator.RunAsync(new[] { serialFirst, serialSecond },
            (recipe, index, ct) =>
            {
                order.Add(recipe.DisplayName);
                throw new InvalidOperationException("craft not confirmed");
            }, CancellationToken.None);
        throw new Exception("failed recipe did not abort remaining queue");
    }
    catch (InvalidOperationException ex) when (ex.Message == "craft not confirmed")
    {
        Check(order.SequenceEqual(new[] { "가든 샐러드" }),
            "failed first recipe must never start the second recipe");
    }

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        order.Clear();
        try
        {
            await CraftingQueueCoordinator.RunAsync(new[] { serialFirst, serialSecond },
                (recipe, index, ct) => { order.Add(recipe.DisplayName); return Task.CompletedTask; },
                cancelled.Token);
            throw new Exception("cancelled queue ran");
        }
        catch (OperationCanceledException)
        {
            Check(order.Count == 0, "cancellation blocks all subsequent recipes");
        }
    }

    var stage = new ProductionStageMachine("test");
    stage.Move(ProductionStage.OpenHub, "hub");
    stage.Move(ProductionStage.SelectCategory, "category");
    stage.Move(ProductionStage.Search, "search");
    stage.Move(ProductionStage.Detail, "detail");
    stage.Move(ProductionStage.CreateQuest, "quest");
    stage.Move(ProductionStage.ReadQuest, "read");
    stage.Move(ProductionStage.AcquireMaterial, "material");
    stage.Move(ProductionStage.Travel, "travel");
    stage.Move(ProductionStage.VerifyInventory, "inventory");
    stage.Move(ProductionStage.ReadQuest, "read again");
    Check(stage.Current == ProductionStage.ReadQuest,
        "V3 production state machine supports the crafting material loop");
    try
    {
        var invalid = new ProductionStageMachine("test");
        invalid.Move(ProductionStage.Process, "illegal jump");
        throw new Exception("illegal production stage jump accepted");
    }
    catch (InvalidOperationException)
    {
        Check(true, "V3 production state machine rejects hidden direct jumps");
    }

    var itemTitle = CraftingHubLayout.CategoryTitleArea(CraftingCategory.Item);
    var foodTitle = CraftingHubLayout.CategoryTitleArea(CraftingCategory.Food);
    var itemCard = CraftingHubLayout.CategoryCardArea(CraftingCategory.Item);
    var foodCard = CraftingHubLayout.CategoryCardArea(CraftingCategory.Food);
    Check(itemCard.Contains(itemTitle) && foodCard.Contains(foodTitle) &&
          !itemTitle.IntersectsWith(foodTitle),
        "crafting hub item/food OCR title bands stay inside separate verified cards");
    Check(itemTitle.Contains(new System.Drawing.Point(400, 633)) &&
          foodTitle.Contains(new System.Drawing.Point(640, 633)),
        "uploaded 800x1000 crafting hub item/food titles are covered");
    Check(CraftingHubLayout.CategoryClickPoint(CraftingCategory.Item) ==
              new System.Drawing.Point(400, 632) &&
          CraftingHubLayout.CategoryClickPoint(CraftingCategory.Food) ==
              new System.Drawing.Point(640, 632) &&
          CraftingHubLayout.IsSafeFallbackPoint(CraftingCategory.Item) &&
          CraftingHubLayout.IsSafeFallbackPoint(CraftingCategory.Food),
        "live V2.0.4 crafting hub uses safe verified item/food fallback centers");
    Check(CraftingHubLayout.HubHeaderArea.Contains(new System.Drawing.Point(75, 62)),
        "crafting hub top-left 제작 header is covered for fallback authorization");
    Check(CraftingHubLayout.IsSafeSearchGeometry() &&
          CraftingHubLayout.ProductFilterArea.Contains(CraftingHubLayout.ProductSearchIconPoint) &&
          CraftingHubLayout.ProductSearchDialogArea.Contains(CraftingHubLayout.ProductSearchInputPoint) &&
          CraftingHubLayout.ProductSearchResultArea.Contains(CraftingHubLayout.ProductFirstResultPoint),
        "fixed 800x1000 crafting search points stay inside verified UI regions");
    Check(CraftingHubLayout.ProductSearchIconPoint ==
              new System.Drawing.Point(30, 118),
        "V3.0.1 live capture search magnifier center is preserved");
    Check(CraftingHubLayout.ProductSearchInputPoint ==
              new System.Drawing.Point(400, 862) &&
          CraftingHubLayout.ProductSearchDialogArea.Contains(CraftingHubLayout.ProductSearchInputPoint),
        "uploaded live search popup input center is preserved");
    Check(CraftingHubLayout.ProductFirstResultPoint ==
              new System.Drawing.Point(218, 613) &&
          CraftingHubLayout.ProductSearchResultArea.Contains(CraftingHubLayout.ProductFirstResultPoint),
        "V3.0.4 live search first result card center is preserved");
    Check(CraftingHubLayout.ProductListSettleDelayMs >= 1000,
        "crafting item/food list settles before the magnifier is clicked");
    Check(CraftingHubLayout.IsSafeCraftDetailGeometry() &&
          CraftingHubLayout.CraftCountMinusPoint == new System.Drawing.Point(275, 835) &&
          CraftingHubLayout.CraftCountCenterPoint == new System.Drawing.Point(400, 835) &&
          CraftingHubLayout.CraftCountPlusPoint == new System.Drawing.Point(525, 835) &&
          CraftingHubLayout.CraftQuestButtonPoint == new System.Drawing.Point(307, 950) &&
          CraftingHubLayout.CraftGoButtonPoint == new System.Drawing.Point(507, 950),
        "live V3.0.5 crafting detail count/quest/go controls are preserved");
    Check(CraftingHubLayout.CraftActionButtonArea.Contains(
              new System.Drawing.Point(507, 950)),
        "crafting station action button area covers the live 제작하기 button");
    Check(CraftingHubLayout.CraftDetailSettleDelayMs <= 100 &&
          CraftingHubLayout.CraftReadyPollDelayMs <= 200,
        "crafting detail and station-ready polling stay responsive");
    Check(CraftingHubLayout.ProductionQuestTopPoint ==
              new System.Drawing.Point(742, 310) &&
          CraftingHubLayout.ProductionQuestPopupArea.Contains(
              new System.Drawing.Point(400, 350)),
        "newest production quest fixed point and popup ROI are preserved");
    Check(CraftingHubLayout.ProductionQuestMaterialArea.Contains(
              new System.Drawing.Point(260, 710)) &&
          CraftingHubLayout.ProductionQuestMaterialArea.Contains(
              new System.Drawing.Point(560, 843)) &&
          !CraftingHubLayout.ProductionQuestMaterialArea.Contains(
              new System.Drawing.Point(400, 350)),
        "live quest material ROI covers potato/onion/cabbage rows and excludes upper quest list");
    Check(CraftingHubLayout.AcquisitionMethodHeaderArea.Contains(
              new System.Drawing.Point(175, 560)) &&
          CraftingHubLayout.AcquisitionMethodRecommendedRowArea.Contains(
              CraftingHubLayout.AcquisitionMethodRecommendedPoint) &&
          CraftingHubLayout.AcquisitionMethodRecommendedPoint ==
              new System.Drawing.Point(400, 650),
        "live 구하는 방법 header and recommended first-row geometry are preserved");
    Check(!CraftingHubLayout.CraftCompletionHeaderArea.IsEmpty &&
          !CraftingHubLayout.CraftCompletionConfirmArea.IsEmpty &&
          CraftingHubLayout.CraftCompletionConfirmArea.Bottom <= 1000,
        "crafting completion header/confirm regions stay inside the 800x1000 client");
    Check(CraftingHubLayout.SearchDialogOpenChangeRatio > 0 &&
          CraftingHubLayout.SearchDialogOpenChangeRatio < 0.25 &&
          CraftingHubLayout.SearchResultChangeRatio > 0 &&
          CraftingHubLayout.SearchResultChangeRatio < 0.25,
        "crafting search state transitions use bounded fixed-ROI visual thresholds");
    Check(CraftingHubLayout.IsStableTitle(
              new System.Drawing.Rectangle(370, 620, 60, 28),
              new System.Drawing.Rectangle(374, 622, 62, 28)) &&
          !CraftingHubLayout.IsStableTitle(
              new System.Drawing.Rectangle(370, 620, 60, 28),
              new System.Drawing.Rectangle(610, 620, 55, 28)),
        "crafting hub category requires same-card two-frame title stability");

    var response = MabinogiMobileCli.Parse("get_craftable_items", new CliProcessOutput(0,
        JsonSerializer.Serialize(new
        {
            craftingUnlocked = true,
            items = new object[]
            {
                new { DisplayName = "야채볶음", Craftable = false, ProducedPerCraft = 1, Category = "음식 제작대",
                    MissingIngredients = new[]{ new { DisplayName="감자", Required=8L, Owned=6L } } },
                new { DisplayName = "상급 회복 물약", Craftable = true, ProducedPerCraft = 5, Category = "아이템",
                    MissingIngredients = Array.Empty<object>() },
                new { DisplayName = "분류 없는 결과물", Craftable = true, ProducedPerCraft = 2,
                    MissingIngredients = Array.Empty<object>() }
            }
        }), ""));

    var items = CraftingQueries.ParseCatalog(response);
    var duplicateResponse = MabinogiMobileCli.Parse("get_craftable_items", new CliProcessOutput(0,
        "{\"items\":[{\"DisplayName\":\"동일 이름\",\"Craftable\":false,\"ProducedPerCraft\":1},{\"DisplayName\":\"동일 이름\",\"Craftable\":true,\"ProducedPerCraft\":1}]}", ""));
    var duplicateItems = CraftingQueries.ParseCatalog(duplicateResponse);
    Check(duplicateItems.Count == 2, "different recipes with the same display name are not silently discarded");
    try { CraftingQueries.Exact(duplicateItems, "동일 이름"); throw new Exception("ambiguous recipe accepted"); }
    catch (InvalidOperationException) { Check(true, "ambiguous same-name recipes cannot authorize screen crafting"); }
    Check(items.Count == 3, "craftable catalog parses every item");
    Check(items[0].Category == CraftingCategory.Food && items[1].Category == CraftingCategory.Item,
        "food and item category hints are recognized");
    Check(items[2].Category == CraftingCategory.Unknown, "unknown category is preserved instead of dropped");
    Check(CraftingQueries.ResolveUiCategory(items[0]) == CraftingCategory.Food &&
          CraftingQueries.ResolveUiCategory(items[1]) == CraftingCategory.Item &&
          CraftingQueries.ResolveUiCategory(items[2]) != CraftingCategory.Unknown,
        "search-only crafting selection resolves an in-game station category for every listed row");
    Check(CraftingQueries.ForUiCategory(items, CraftingCategory.Food).Select(x => x.DisplayName).SequenceEqual(new[] { "야채볶음" }) &&
          CraftingQueries.ForUiCategory(items, CraftingCategory.Item).Select(x => x.DisplayName).SequenceEqual(new[] { "상급 회복 물약" }) &&
          !CraftingQueries.ForUiCategory(items, CraftingCategory.Food).Any(x => x.Category != CraftingCategory.Food) &&
          !CraftingQueries.ForUiCategory(items, CraftingCategory.Item).Any(x => x.Category != CraftingCategory.Item),
        "food and item UI categories are strictly separated and unknown entries stay hidden");

    // Live CLI can omit category or label every recipe generically as Item.
    // User-reported 카레라이스 and other reviewed food names must still
    // navigate 제작 → 음식, while ordinary item-table recipes remain Item.
    string[] reviewedFoodNames =
    {
        "카레라이스", "토마토 리소토", "알리오 올리오", "미트 파르미자나",
        "샤와르마", "포크 인 밀크", "콘치즈", "사과 수플레",
        "넛츠 초코 비스킷", "바삭한 초코 카나페", "토마토 크림 생선 커넬",
        "파이크퍼치 필레 리소토", "홍차", "가든 샐러드",
        "대만찬: 거센 바람의 페어리 플로스",
        "대만찬: 꺼지지 않는 불의 뵈프 부르기뇽"
    };
    Check(reviewedFoodNames.All(CraftingQueries.IsVerifiedFoodRecipe) &&
          !CraftingQueries.IsVerifiedFoodRecipe("짜먹는 간식") &&
          !CraftingQueries.IsVerifiedFoodRecipe("여행자 간식") &&
          !CraftingQueries.IsVerifiedFoodRecipe("숙련 캠프파이어 키트"),
        "all reviewed food exceptions are categorized without misclassifying item-table recipes");

    var mislabelledFood = new CraftableItem("카레라이스", true, 1, null,
        Array.Empty<CraftingIngredient>(), CraftingCategory.Item);
    var unknownFood = new CraftableItem("알리오 올리오", false, 1, null,
        Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown);
    var itemCraft = new CraftableItem("숙련 캠프파이어 키트", true, 1, null,
        Array.Empty<CraftingIngredient>(), CraftingCategory.Item);
    Check(CraftingQueries.ResolveUiCategory(mislabelledFood) == CraftingCategory.Food &&
          CraftingQueries.ResolveUiCategory(unknownFood) == CraftingCategory.Food &&
          CraftingQueries.ResolveUiCategory(itemCraft) == CraftingCategory.Item &&
          CraftingQueries.ForUiCategory(
              new[] { mislabelledFood, unknownFood, itemCraft }, CraftingCategory.Food)
              .Select(x => x.DisplayName)
              .SequenceEqual(new[] { "카레라이스", "알리오 올리오" }),
        "curry rice and ambiguous foods always open Food card even with generic Item metadata");

    var metadataSparse = new[]
    {
        new CraftableItem("야채볶음", true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown),
        new CraftableItem("감자 샐러드", true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown),
        new CraftableItem("상급 회복 물약", true, 5, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown)
    };
    var fallbackFood = CraftingQueries.ForUiCategory(metadataSparse, CraftingCategory.Food).Select(x => x.DisplayName).ToArray();
    var fallbackItem = CraftingQueries.ForUiCategory(metadataSparse, CraftingCategory.Item).Select(x => x.DisplayName).ToArray();
    Check(fallbackFood.SequenceEqual(new[] { "야채볶음", "감자 샐러드" }) &&
          fallbackItem.SequenceEqual(new[] { "상급 회복 물약" }) &&
          !fallbackFood.Intersect(fallbackItem, StringComparer.Ordinal).Any(),
        "metadata-sparse live catalog fallback keeps food/item tabs populated without overlap");
    var sparseCoverage = CraftingQueries.CategoryCoverage(metadataSparse);
    Check(sparseCoverage.UsesFallback && sparseCoverage.Unknown == 3,
        "category coverage reports fallback when live CLI omits category metadata");

    Check(CraftingQueries.Exact(items, "야채볶음").MissingIngredients.Single().DisplayName == "감자",
        "exact crafting match never picks a partial name");
    try
    {
        CraftingQueries.Exact(new[]
        {
            new CraftableItem("통나무", true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown),
            new CraftableItem("단단한 통나무", true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown),
            new CraftableItem("부드러운 통나무", true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Unknown)
        }, "황금 통나무");
        throw new Exception("missing exact item accepted");
    }
    catch (InvalidOperationException)
    {
        Check(true, "partial-name results cannot authorize a different exact item");
    }

    Check(CraftingQueries.RequiredCrafts(100, 1) == 100 &&
          CraftingQueries.NextBatchCrafts(100) == 10,
        "100 output uses ten-craft maximum batches");
    Check(CraftingQueries.RequiredCrafts(21, 5) == 5 &&
          CraftingQueries.NextBatchCrafts(5) == 5,
        "produced-per-craft and final remainder are calculated");
    Check(CraftingQueries.NextBatchCrafts(17) == 10 &&
          CraftingQueries.NextBatchCrafts(7) == 7,
        "batch is capped at ten and remainder stays exact");

    var cabbageDecision = QuestMaterialNameMatcher.Decide(
        "양배",
        new[] { "면", "밀", "밥", "쌀", "양배추", "양털", "양파", "콩" });
    Check(cabbageDecision.Name == "양배추" &&
          cabbageDecision.AmbiguousCandidates.Count == 0,
        "full-string quest material matching corrects 양배 to 양배추 without substring false positives");

    var potatoDecision = QuestMaterialNameMatcher.Decide(
        "감사",
        new[] { "감자", "양파", "허브" });
    Check(potatoDecision.Name == "감자",
        "full-string quest material matching corrects 감사 to 감자");

    var ambiguousDecision = QuestMaterialNameMatcher.Decide(
        "감사",
        new[] { "감자", "감초" });
    Check(ambiguousDecision.Name is null &&
          ambiguousDecision.AmbiguousCandidates.Count == 2,
        "quest material matching refuses true full-string ties");

    var onionRecipeDecision = QuestMaterialNameMatcher.Decide(
        "양합",
        new[] { "감자", "양파", "양배추", "허브" });
    Check(onionRecipeDecision.Name == "양파" &&
          onionRecipeDecision.AmbiguousCandidates.Count == 0,
        "pre-read recipe candidates correct 양합 to 양파 without global 양털 ambiguity");

    var queryArgs = MabinogiMobileCli.BuildFilteredQueryArguments("get_craftable_items", "야채볶음");
    Check(queryArgs.Count == 2 && queryArgs[0] == "get_craftable_items" && queryArgs[1].StartsWith("base64:"),
        "Korean craftable filter uses guarded base64 query");
    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(queryArgs[1][7..]));
    Check(decoded == "야채볶음", "craftable filter round-trips exact Korean name");

    int launches = 0;
    Check(AcquisitionMethodPolicy.IsLifeSkill("추천 생활 스킬 벌목") &&
          !AcquisitionMethodPolicy.IsLifeSkill("던전 전리품 채집") &&
          !AcquisitionMethodPolicy.IsLifeSkill("상점 구매") &&
          !AcquisitionMethodPolicy.IsLifeSkill("선택하세요"),
        "acquisition requires positive life-skill evidence and rejects paid or dungeon rows");
    foreach (string wanted in new[] { "달걀", "황금 달걀", "통나무", "단단한 통나무", "부드러운 통나무" })
    {
        var overlapping = new[] { "황금 달걀", "부드러운 통나무", "단단한 통나무", "달걀", "통나무" }
            .Select(x => new CraftableItem(x, true, 1, null, Array.Empty<CraftingIngredient>(), CraftingCategory.Item)).ToArray();
        Check(CraftingQueries.Exact(overlapping, wanted).DisplayName == wanted, "exact overlapping name: " + wanted);
    }
    var log = new AppLog(Path.Combine(AppContext.BaseDirectory, "crafting-test.log"), 1000000);
    var noteCli = new MabinogiMobileCli(log, true, (command, ct) => Task.FromResult(new CliProcessOutput(0,
        command == "status" ? "{\"pipe\":\"connected\"}" :
        "{\"commands\":[{\"Command\":\"execute_crafting\",\"Description\":\"Craft a recipe\",\"Note\":\"Running this command consumes 5 정령의 날개.\"}]}", "")));
    var noteCapabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(noteCli, Array.Empty<string>());
    Check(noteCapabilities["execute_crafting"].Note?.Contains("consumes 5", StringComparison.Ordinal) == true,
        "cost Note is preserved even when Metadata is absent");
    var cli = new MabinogiMobileCli(log, true, (_, _) =>
    {
        launches++;
        return Task.FromResult(new CliProcessOutput(0, "{}", ""));
    });
    Check((await cli.QueryAsync("execute_crafting")).State == "blocked" && launches == 0,
        "execute_crafting cannot cross the read-only CLI process boundary");

    await CraftingFoodMaterialPolicyTests.RunAsync(Check);
    await CraftingFoodMaterialIntegrationTests.RunAsync(Check, log);
    await CraftingFlowTests.RunAsync(Check, log);
    await BulkGatheringFlowTests.RunAsync(Check);
    if (args.Length == 1 && args[0] == "--live-readonly") await LiveReadOnlyTests.RunAsync(Check, log);
    else if (args.Length == 1) await LiveSnapshotTests.RunAsync(args[0], Check, log);
    Console.WriteLine($"PASS {checks} crafting checks");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
