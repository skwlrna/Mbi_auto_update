using FishingAutomation;

internal static class BulkGatheringFlowTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (long initial in new long[] { 0, 7 })
        {
            long stock = initial;
            int seeds = 0, hundreds = 0;
            var waits = new List<long>();
            var engine = new InventoryBulkGatheringAutomation(
                _ => Task.FromResult(stock), _ => Task.FromResult(stock),
                _ => { seeds++; stock++; return Task.CompletedTask; },
                _ => { hundreds++; return Task.CompletedTask; },
                (before, minimum, ct) => { waits.Add(minimum); if (minimum == 100) stock += 105; return Task.CompletedTask; });
            await engine.RunAsync(new("통나무", 201), CancellationToken.None);
            check(hundreds == 2 && stock - initial >= 201 && seeds == (initial == 0 ? 1 : 0),
                $"bulk engine repeats 100 quests and accepts overshoot from initial {initial}");
            check(initial != 0 || waits.SequenceEqual(new long[] { 1, 100, 100 }),
                "zero inventory seed naturally stops before switching to 100 quests");
        }
        long missingStock = 0;
        int started = 0;
        var missingSeed = new InventoryBulkGatheringAutomation(
            _ => Task.FromResult(missingStock), _ => Task.FromResult(missingStock),
            _ => Task.CompletedTask, _ => { started++; return Task.CompletedTask; },
            (_, _, _) => Task.CompletedTask);
        try { await missingSeed.RunAsync(new("달걀", 100), CancellationToken.None); throw new Exception("missing seed accepted"); }
        catch (InvalidOperationException) { check(started == 0, "missing initial item blocks inventory search"); }
    }
}
