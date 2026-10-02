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
    Check(CraftingQuestText.IsDirectStage("• 바로 제작 진행") &&
          !CraftingQuestText.IsDirectStage("제작 진행"),
        "ready-material quest uses the observed direct crafting stage");
    Check(CraftingQuestText.IsStationStage("다목적 제작대에서 제작 0/10") &&
          !CraftingQuestText.IsStationStage("다목적 제작대 사용 중"),
        "station progress stage cannot match crafting-busy state");
    Check(CraftingQuestText.IsStationStage("• 약품 제작대에서 제작") &&
          !CraftingQuestText.IsStationStage("바로 제작 진행"),
        "observed bandage quest omits ratio and stays separate from instant crafting");

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
    Check(CraftingHubLayout.ProductListSettleDelayMs >= 1000,
        "crafting item/food list settles before the magnifier is clicked");
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
    Check(CraftingQueries.ForUiCategory(items, CraftingCategory.Food).Count() == 2 &&
          CraftingQueries.ForUiCategory(items, CraftingCategory.Item).Count() == 2,
        "unknown catalog entries remain visible so UI never omits craftables");

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
