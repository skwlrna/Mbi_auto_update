using DungeonVisionBot;
using FishingAutomation;
using System.Text.Json;

internal static class CraftingFoodBatchPolicyTests
{
    internal static async Task RunAsync(Action<bool, string> check, AppLog log)
    {
        CraftingFoodRecipeCatalog.TryGet("호박 수프", out var soup);
        var batch = soup.ForBatch(10);
        var stock = new Dictionary<string, long>(StringComparer.Ordinal)
        { ["호박"] = 4, ["감자"] = 8, ["양파"] = 34, ["생강"] = 20, ["생크림"] = 50 };
        var missing = CraftingFoodBatchPolicy.Deficits(batch, stock);
        check(missing.Count == 3 && missing[0].DisplayName == "호박" &&
              missing[0].Required == 100 && missing.All(x => x.RowY == 0),
            "live soup 4/100, 8/60, 34/60 deficits come from recipe and fresh CLI without names or row OCR");
        var supported = new HashSet<string>(new[] { "호박", "감자", "양파" }, StringComparer.Ordinal);
        var row = CraftingFoodBatchPolicy.FindGatherRow("호박", missing,
            new[] { new CraftingQuestDeficit("", 4, 100, 711) }, supported);
        check(row?.Row.RowY == 711 && row.Targets.Single().DisplayName == "호박",
            "blank OCR name still binds pumpkin row using required count and CLI stock");
        var equal = new[] { new CraftingQuestDeficit("감자", 0, 60, 0), new CraftingQuestDeficit("양파", 0, 60, 0) };
        row = CraftingFoodBatchPolicy.FindGatherRow("양파", equal,
            new[] { new CraftingQuestDeficit("", 0, 60, 711) }, supported);
        check(row?.Targets.Count == 2 && row.Targets.Any(x => x.DisplayName == "양파"),
            "identical counters track both supported named inventories without guessing row identity");
        var mixed = new[] { equal[0], new CraftingQuestDeficit("소금", 0, 60, 0) };
        check(CraftingFoodBatchPolicy.FindGatherRow("감자", mixed,
                new[] { new CraftingQuestDeficit("", 0, 60, 711) }, supported) is null,
            "ambiguous purchased or processing input cannot authorize generic gathering");
        check(CraftingFoodBatchPolicy.FindGatherRow("호박", missing,
                new[] { new CraftingQuestDeficit("", 0, 1, 908) }, supported) is null &&
              CraftingFoodBatchPolicy.FindGatherRow("호박", missing, Array.Empty<CraftingQuestDeficit>(), supported) is null,
            "station counter and missing ratio evidence never authorize a row click");
        bool rejected = false;
        try { CraftingFoodBatchPolicy.Deficits(batch, new Dictionary<string, long>()); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "unavailable CLI snapshot is not treated as zero inventory");

        foreach (bool failRow in new[] { false, true })
        {
            var inventory = soup.ForBatch(10).ToDictionary(x => x.Name, x => x.Amount * 5, StringComparer.Ordinal);
            inventory["호박"] = 204; inventory["감자"] = 128; inventory["양파"] = 154;
            inventory["호박 수프"] = 0;
            var screen = new RecipeScreen(inventory, soup, failRow);
            string Read(string command) => command switch
            {
                "get_items" => JsonSerializer.Serialize(inventory.Select(x => new
                    { DisplayName = x.Key, Count = x.Value, Location = "inventory" })),
                "get_craftable_items" => JsonSerializer.Serialize(new { items = new[] { new
                    { DisplayName = "호박 수프", Craftable = true, ProducedPerCraft = 1, MissingIngredients = Array.Empty<object>() } } }),
                "get_gatherable_items" => JsonSerializer.Serialize(new { items = supported.Select(x => new { DisplayName = x, ToolOk = true }) }),
                _ => throw new Exception("Unexpected CLI action/query: " + command)
            };
            var cli = new MabinogiMobileCli(log, true, (cmd, _) => Task.FromResult(new CliProcessOutput(0, Read(cmd), "")));
            cli.RunFilteredQuery = (args, _) => Task.FromResult(new CliProcessOutput(0, Read(args[0]), ""));
            var engine = new CraftingAutomation(new CraftingCliData(cli), new GatheringCliData(cli),
                screen, null!, (_, _) => Task.CompletedTask);
            bool rowError = false;
            try { await engine.RunAsync(new(CraftingCategory.Food, "호박 수프", 50, 1), CancellationToken.None); }
            catch (CraftingQuestRowRecognitionException) { rowError = true; }
            if (!failRow)
                check(engine.CompletedCrafts == 50 && inventory["호박 수프"] == 50 &&
                      screen.Created == 5 && screen.Recoveries == 0 && screen.Gathers > 0,
                    "real engine completes 50 soup crafts after third-batch CLI shortages with no OCR-name recovery or quest replay");
            else
                check(rowError && engine.CompletedCrafts == 20 && screen.Created == 3 && screen.Recoveries == 0,
                    "row-position failure after 20 crafts preserves progress and does not recreate identical quest three times");
        }
    }

    private sealed class RecipeScreen(Dictionary<string, long> inventory, CraftingFoodRecipe recipe, bool failRow) : ICraftingScreen
    {
        private IReadOnlyList<CraftingFoodIngredient> _batch = Array.Empty<CraftingFoodIngredient>();
        internal int Created, Recoveries, Gathers;
        public string InputMode => "test";
        public IReadOnlyList<CraftingFoodIngredient>? RegisteredFoodBatch => _batch;
        public event Action<string>? Log { add { } remove { } }
        public Task CreateQuestAsync(CraftingPlan plan, int count, CancellationToken ct)
        { Created++; _batch = recipe.ForBatch(count); return Task.CompletedTask; }
        public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan plan, CancellationToken ct)
            => Task.FromResult(CraftingFoodBatchPolicy.Deficits(_batch, inventory));
        public Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
        {
            if (failRow) throw new CraftingQuestRowRecognitionException("row geometry unavailable; name OCR is not required");
            Gathers++; inventory[deficit.DisplayName] = deficit.Required;
            return Task.CompletedTask;
        }
        public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct)
        { Recoveries++; return Task.CompletedTask; }
        public Task CloseOverlayAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ReturnToStationAndCraftAsync(CraftingPlan plan, int count, CancellationToken ct)
        {
            foreach (var x in _batch) inventory[x.Name] -= x.Amount;
            inventory[plan.DisplayName] += count;
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
