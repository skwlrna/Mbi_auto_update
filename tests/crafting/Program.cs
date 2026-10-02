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

    var args = MabinogiMobileCli.BuildFilteredQueryArguments("get_craftable_items", "야채볶음");
    Check(args.Count == 2 && args[0] == "get_craftable_items" && args[1].StartsWith("base64:"),
        "Korean craftable filter uses guarded base64 query");
    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(args[1][7..]));
    Check(decoded == "야채볶음", "craftable filter round-trips exact Korean name");

    int launches = 0;
    var log = new AppLog(Path.Combine(AppContext.BaseDirectory, "crafting-test.log"), 1000000);
    var cli = new MabinogiMobileCli(log, true, (_, _) =>
    {
        launches++;
        return Task.FromResult(new CliProcessOutput(0, "{}", ""));
    });
    Check((await cli.QueryAsync("execute_crafting")).State == "blocked" && launches == 0,
        "execute_crafting cannot cross the read-only CLI process boundary");

    Console.WriteLine($"PASS {checks} crafting checks");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}