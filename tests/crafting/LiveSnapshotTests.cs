using FishingAutomation;
using System.Text.Json;

internal static class LiveSnapshotTests
{
    internal static async Task RunAsync(string directory, Action<bool, string> check, AppLog log)
    {
        int actions = 0;
        string Read(string command) => command == "status" ? "{\"pipe\":\"connected\"}" : File.ReadAllText(Path.Combine(directory, command + ".json"));
        var cli = new MabinogiMobileCli(log, true, (command, ct) =>
        {
            if (command.StartsWith("execute_", StringComparison.Ordinal)) actions++;
            return Task.FromResult(new CliProcessOutput(0, Read(command), ""));
        });
        cli.RunFilteredQuery = (query, ct) => Task.FromResult(new CliProcessOutput(0, Read(query[0]), ""));
        var catalog = await new CraftingCliData(cli).CatalogAsync(CancellationToken.None);
        using var raw = JsonDocument.Parse(Read("get_craftable_items"));
        check(catalog.Count == raw.RootElement.GetProperty("items").GetArrayLength(), "live snapshot preserves every crafting recipe row");
        foreach (var group in catalog.GroupBy(x => x.DisplayName).Where(x => x.Count() > 1))
        {
            try { CraftingQueries.Exact(catalog, group.Key); throw new Exception("live ambiguous recipe accepted"); }
            catch (InvalidOperationException) { }
        }
        check(true, "all live duplicate recipe groups reject automatic exact selection");
        var inventory = new CraftingCliData(cli);
        using var rawInventory = JsonDocument.Parse(Read("get_items"));
        foreach (string name in new[] { "달걀", "황금 달걀", "통나무", "단단한 통나무", "부드러운 통나무" })
        {
            var exactRows = rawInventory.RootElement.EnumerateArray().Where(x => x.GetProperty("DisplayName").GetString() == name).ToArray();
            long expected = exactRows.Sum(x => x.GetProperty("Count").GetInt64());
            long expectedBag = exactRows.Where(x => x.GetProperty("Location").GetString() == "inventory").Sum(x => x.GetProperty("Count").GetInt64());
            check(await inventory.ItemCountAsync(name, CancellationToken.None) == expected &&
                  await inventory.InventoryOnlyCountAsync(name, CancellationToken.None) == expectedBag,
                "live exact inventory and storage separation: " + name);
        }
        var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(cli, new[] { "get_craftable_items" });
        check(capabilities["execute_crafting"].Note?.Contains("consumes 5", StringComparison.Ordinal) == true &&
              capabilities["execute_gathering"].Note?.Contains("consumes 5", StringComparison.Ordinal) == true,
            "live capability Note preserves both five-wing action costs");
        check((await cli.QueryAsync("execute_crafting")).State == "blocked" &&
              (await cli.QueryAsync("execute_gathering")).State == "blocked" && actions == 0,
            "live-data validation cannot launch paid action commands");
        var gathering = new GatheringCliData(cli);
        check((await gathering.CatalogAsync(CancellationToken.None)).Count > 0 &&
              (await gathering.ActivityAsync(CancellationToken.None)).IsSafeField,
            "live gathering catalog and activity schema are compatible");
    }
}
