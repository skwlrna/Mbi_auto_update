using FishingAutomation;

internal static class LiveReadOnlyTests
{
    internal static async Task RunAsync(Action<bool, string> check, AppLog log)
    {
        // Opt-in real game queries. No screen inputs or action commands are executed.
        var cli = new MabinogiMobileCli(log, true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var before = await CliAutomationGuards.CurrencySnapshotAsync(cli, ct);
        var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(cli,
            new[] { "get_items", "get_craftable_items", "get_gatherable_items", "get_activity" }, ct);
        check(capabilities["execute_crafting"].Note?.Contains("consumes 5", StringComparison.Ordinal) == true,
            "actual live CLI publishes crafting five-wing cost in Note");
        var crafting = new CraftingCliData(cli);
        var catalog = await crafting.CatalogAsync(ct);
        Console.WriteLine($"LIVE catalog rows={catalog.Count}, distinct names={catalog.Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count()}");
        check(catalog.Count > 0, "actual live crafting wrapper parses the full catalog");
        var exact = await crafting.ExactAsync("캠프파이어 키트", ct);
        check(exact.DisplayName == "캠프파이어 키트" && exact.ProducedPerCraft == 1,
            "actual Korean filtered crafting query resolves the exact recipe");
        foreach (string name in new[] { "달걀", "황금 달걀", "통나무", "단단한 통나무", "부드러운 통나무" })
        {
            long total = await crafting.ItemCountAsync(name, ct);
            long bag = await crafting.InventoryOnlyCountAsync(name, ct);
            check(total >= bag && bag >= 0, "actual live inventory wrapper: " + name);
            Console.WriteLine($"LIVE {name}: total={total}, bag={bag}");
        }
        var gathering = new GatheringCliData(cli);
        var gatherCatalog = await gathering.CatalogAsync(ct);
        check(gatherCatalog.Count > 0 && (await gathering.ActivityAsync(ct)).IsSafeField,
            "actual live gathering wrapper parses catalog and safe-field activity");
        check((await cli.QueryAsync("execute_crafting", ct)).State == "blocked" &&
              (await cli.QueryAsync("execute_gathering", ct)).State == "blocked",
            "actual connector blocks paid actions before launching a process");
        var after = await CliAutomationGuards.CurrencySnapshotAsync(cli, ct);
        check(before.TryGetValue("정령의 날개", out decimal wingsBefore) &&
              after.TryGetValue("정령의 날개", out decimal wingsAfter) && wingsBefore == wingsAfter,
            "actual live wing balance stays unchanged across validation");
    }
}
