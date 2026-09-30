using System.Text.Json;
using FishingAutomation;

int passed = 0;
void Check(bool value, string label)
{ if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
var names = new Dictionary<string, string>
{ ["stone"] = "허상의 마력석", ["rune"] = "룬결속 장식(★10)", ["plus"] = "룬결속 장식(★10)+" };
string Items(params (string Name, long Count, string Location)[] items)
    => JsonSerializer.Serialize(items.Select(x => new { DisplayName = x.Name, Count = x.Count, Location = x.Location }));
MabinogiCliResult Response(string json) => MabinogiMobileCli.Parse("get_items", new(0, json, ""));
InventoryLootTracker Tracker(params string[] json)
{
    var queue = new Queue<string>(json);
    return new(_ => Task.FromResult(Response(queue.Dequeue())), names);
}
string empty = "[]";
string five = Items(("허상의 마력석", 5, "inventory"));
string eight = Items(("허상의 마력석", 8, "inventory"));
var tracker = Tracker(empty, eight, eight);
Check(await tracker.BeginRoundAsync(default), "baseline captured");
var gained = await tracker.CompleteRoundAsync(default);
Check(gained.Confirmed && gained.Gains["stone"] == 8, "new item records exact quantity");
Check(!(await tracker.CompleteRoundAsync(default)).Confirmed, "same result and reconnect cannot count twice");
tracker = Tracker(five, eight, eight);
await tracker.BeginRoundAsync(default);
Check((await tracker.CompleteRoundAsync(default)).Gains["stone"] == 3, "stack quantity delta");
string split = Items(("허상의 마력석", 1, "inventory"), ("허상의 마력석", 2, "account_storage"), ("허상의 마력석", 2, "character_storage"));
tracker = Tracker(five, split, split);
await tracker.BeginRoundAsync(default);
var moved = await tracker.CompleteRoundAsync(default);
Check(moved.Confirmed && moved.Gains.Count == 0, "storage transfer is not a reward");
tracker = Tracker(eight, five, five);
await tracker.BeginRoundAsync(default);
Check((await tracker.CompleteRoundAsync(default)).Gains.Count == 0, "decrease cannot create a reward");
tracker = Tracker(five, five, five);
await tracker.BeginRoundAsync(default);
var same = await tracker.CompleteRoundAsync(default);
Check(same.Confirmed && same.Gains.Count == 0, "unchanged inventory confirms zero");
tracker = Tracker(empty, five, eight, eight);
await tracker.BeginRoundAsync(default);
Check((await tracker.CompleteRoundAsync(default)).Gains["stone"] == 8, "delayed reward rechecked until stable");
tracker = Tracker(empty, empty, five, eight);
await tracker.BeginRoundAsync(default);
var unstable = await tracker.CompleteRoundAsync(default);
Check(!unstable.Confirmed && unstable.Reason == "inventory_unstable", "unstable rewards remain unknown");
tracker = Tracker();
Check((await tracker.CompleteRoundAsync(default)).Reason == "missing_baseline", "attach during combat/result cannot invent baseline");
foreach (string invalid in new[] { "{}", "null", "not json", "[{\"DisplayName\":\"허상의 마력석\"}]", Items(("허상의 마력석", -1, "inventory")), Items(("허상의 마력석", 1, "unknown_storage")), "[{\"DisplayName\":\"허상의 마력석\",\"Count\":\"5\",\"Location\":\"inventory\"}]" })
{
    tracker = Tracker(invalid);
    Check(!await tracker.BeginRoundAsync(default), "invalid/partial snapshot rejected");
    Check(!(await tracker.CompleteRoundAsync(default)).Confirmed, "failed baseline cannot be treated as zero");
}
tracker = Tracker(empty, "{\"error\":\"disconnected\"}");
await tracker.BeginRoundAsync(default);
Check(!(await tracker.CompleteRoundAsync(default)).Confirmed, "post-round query failure remains unknown");
using (var doc = JsonDocument.Parse(Items(("룬결속 장식(<color=#FFC448>★10</color>)", 3, "inventory"), ("룬결속 장식(<color=#FFC448>★10</color>)+", 2, "account_storage"), ("허상의 마력석 조각", 9, "inventory"))))
{
    var parsed = InventoryLootTracker.Parse(doc.RootElement, names)!;
    Check(parsed["rune"] == 3 && parsed["plus"] == 2, "color markup stripped and plus variants kept distinct");
    Check(parsed["stone"] == 0, "similar names cannot create a reward");
}
using (var doc = JsonDocument.Parse(Items(("허상의 마력석", long.MaxValue, "inventory"), ("허상의 마력석", 1, "account_storage"))))
    Check(InventoryLootTracker.Parse(doc.RootElement, names) is null, "total quantity overflow rejected");
tracker = Tracker(empty, Items(("허상의 마력석", (long)int.MaxValue + 1, "inventory")), Items(("허상의 마력석", (long)int.MaxValue + 1, "inventory")));
await tracker.BeginRoundAsync(default);
Check(!(await tracker.CompleteRoundAsync(default)).Confirmed, "counter delta overflow rejected");
tracker = Tracker(empty, five, five, eight, Items(("허상의 마력석", 10, "inventory")), Items(("허상의 마력석", 10, "inventory")));
await tracker.BeginRoundAsync(default); await tracker.CompleteRoundAsync(default);
Check(await tracker.PrepareNextRoundAsync(default), "next baseline prepared before retry");
Check(!(await tracker.CompleteRoundAsync(default)).Confirmed, "prepared retry cannot unlatch current result");
tracker.CommitNextRound();
Check((await tracker.CompleteRoundAsync(default)).Gains["stone"] == 2, "new round uses freshly prepared baseline");
tracker = Tracker(empty, five, five, "not json");
await tracker.BeginRoundAsync(default); await tracker.CompleteRoundAsync(default);
Check(!await tracker.PrepareNextRoundAsync(default), "failed next baseline rejected");
tracker.CommitNextRound();
Check((await tracker.CompleteRoundAsync(default)).Reason == "missing_baseline", "failed retry baseline cannot carry old gains forward");
tracker = Tracker(five); await tracker.BeginRoundAsync(default); tracker.Invalidate();
Check((await tracker.CompleteRoundAsync(default)).Reason == "missing_baseline", "outside recovery discards stale baseline");
tracker = new(async ct => { await Task.Delay(Timeout.Infinite, ct); throw new Exception(); }, names);
Check(!await tracker.BeginRoundAsync(default), "query timeout leaves macro free to continue");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await tracker.BeginRoundAsync(cancel.Token); throw new Exception("Stop ignored"); }
    catch (OperationCanceledException) { passed++; }
}

var engine = new DungeonVisionBot.ScenarioEngine(Tracker(empty, eight, eight));
await engine.Begin(default); await engine.Count(default); await engine.Count(default);
Check(LootStats.Recorded.GetValueOrDefault("stone") == 8, "production integration forwards exact amounts once");
Check(engine.Messages.Any(x => x.Contains("+8")), "production log includes quantity");
engine = new(Tracker()); await engine.Count(default);
Check(engine.Messages.Any(x => x.Contains("확인 불가")) && !engine.Messages.Any(x => x.Contains("+0")), "unknown result is not reported as zero");

if (args.Contains("--live"))
{
    var log = new AppLog(Path.Combine(AppContext.BaseDirectory, "live-inventory.log"), 1024 * 1024);
    var cli = new MabinogiMobileCli(log);
    tracker = new(cli.GetItemsAsync, names);
    Check(await tracker.BeginRoundAsync(default), "LIVE inventory baseline parsed");
    var result = await tracker.CompleteRoundAsync(default);
    Check(result.Confirmed, "LIVE inventory comparison parsed");
    Console.WriteLine("LIVE comparison only; no dungeon inputs or persisted counters");
}
Console.WriteLine($"PASS {passed} inventory loot checks");

namespace FishingAutomation
{
    // Isolate persistence: tests must not touch the user's real daily/lifetime statistics.
    internal static class LootStats
    {
        internal static Dictionary<string, int> Recorded = new();
        internal static string GetDisplayName(string key) => key;
        internal static void RecordAmounts(IReadOnlyDictionary<string, int> amounts)
        { foreach (var x in amounts) Recorded[x.Key] = Recorded.GetValueOrDefault(x.Key) + x.Value; }
    }
}
namespace DungeonVisionBot
{
    internal sealed partial class ScenarioEngine
    {
        private bool _abyssLootCountedForCurrentResult;
        internal event Action<string>? Log;
        internal List<string> Messages = new();
        internal ScenarioEngine(InventoryLootTracker tracker) { _inventoryLoot = tracker; Log += Messages.Add; }
        internal Task Begin(CancellationToken ct) => BeginInventoryLootRoundAsync(ct);
        internal Task Count(CancellationToken ct) => CountInventoryLootAsync(ct);
    }
}
