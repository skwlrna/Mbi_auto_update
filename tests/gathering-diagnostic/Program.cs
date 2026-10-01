using FishingAutomation;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
foreach (string payload in new[] {
    "{\"items\":[{\"DisplayName\":\"달걀\",\"ToolOk\":false,\"secret\":\"private-account\"},{\"DisplayName\":\"달걀\"},{\"DisplayName\":\"큰 달걀\"}]}",
    "{\"items\":[]}", "{\"unexpected\":true}", "bad-json" })
{
    var calls = new List<string>(); var lines = new List<string>();
    var cli = new MabinogiMobileCli(new AppLog(Path.GetTempFileName(), 100000), true,
        (command, ct) => {
            calls.Add(command); Check(command == "capabilities", "unexpected read");
            return Task.FromResult(new CliProcessOutput(0, "{\"commands\":[{\"Command\":\"execute_gathering\"},{\"command\":\"stop_action\"},{\"Command\":\"get_gatherable_items\"}]}", ""));
        }, (args, ct) => throw new Exception("Action boundary reached"));
    cli.RunFilteredQuery = (args, ct) => {
        calls.Add(args[0]); Check(args[0] == "get_gatherable_items" && args.Count == 2, "unexpected filtered command");
        Check(Encoding.UTF8.GetString(Convert.FromBase64String(args[1][7..])) == "달걀", "selected filter lost");
        return Task.FromResult(new CliProcessOutput(0, payload, "private-stderr"));
    };
    await GatheringCliDiagnostic.RunAsync("달걀", cli.CapabilitiesAsync, (n, ct) => cli.GetGatherableItemsAsync(n, ct), lines.Add);
    Check(calls.SequenceEqual(new[] { "capabilities", "get_gatherable_items" }), "diagnostic must issue exactly two read commands");
    Check(lines.Count(l => l.Contains("[전체 명령]")) == 3, "full capability list");
    Check(!string.Join("", lines).Contains("private-"), "private payload leaked");
    Check(lines.Last().Contains("종료"), "must terminate");
    if (payload.Contains("secret")) Check(lines.Any(l => l.Contains("items=3") && l.Contains("exact=2") && l.Contains("secret")), "item count fields exact match");
}
var cancelled = new CancellationToken(true);
{
    var lines = new List<string>();
    string description = "설명\n가짜 로그\t\"인용\"";
    var metadata = new { requiresConfirm = "true", nested = new { note = "원문" } };
    var commands = new[] { "get_gatherable_items", "execute_gathering" }.Select(command => new {
        Command = command, Description = description, BodyExample = "{\"item\":\"달걀\"}",
        OutputExample = "결과", Note = "날개 확인", Metadata = metadata
    });
    JsonElement Data(object value) => JsonSerializer.SerializeToElement(value);
    var capData = Data(new { commands });
    var itemData = Data(new { items = Enumerable.Range(0, 12).Select(i => new { DisplayName = i == 0 ? "달걀" : $"달걀 {i}", ToolOk = i % 2 == 0 }) });
    var calls = new List<string>();
    await GatheringCliDiagnostic.RunAsync("달걀",
        ct => { calls.Add("capabilities"); return Task.FromResult(new MabinogiCliResult("capabilities", true, "connected", capData, 0, null)); },
        (n, ct) => { calls.Add("get_gatherable_items"); return Task.FromResult(new MabinogiCliResult("get_gatherable_items", true, "connected", itemData, 0, null)); }, lines.Add);
    Check(calls.SequenceEqual(new[] { "capabilities", "get_gatherable_items" }), "only reads allowed");
    var details = lines.Where(l => l.Contains("[명령 상세]")).ToArray();
    Check(details.Length == 2, "both command details required");
    foreach (var line in details) {
        Check(!line.Contains('\n') && !line.Contains('\t'), "log injection escaped");
        using var json = JsonDocument.Parse(line[line.IndexOf("{", StringComparison.Ordinal)..]);
        Check(json.RootElement.GetProperty("Description").GetString() == description, "description round trip");
        Check(json.RootElement.GetProperty("BodyExample").GetString() == "{\"item\":\"달걀\"}", "body round trip");
        Check(json.RootElement.GetProperty("OutputExample").GetString() == "결과", "output preserved");
        Check(json.RootElement.GetProperty("Note").GetString() == "날개 확인", "note preserved");
        Check(json.RootElement.GetProperty("Metadata").GetProperty("requiresConfirm").GetString() == "true", "confirmation metadata preserved");
    }
    Check(lines.Count(l => l.Contains("[품목 ")) == 12, "all items beyond structure limit logged");
    foreach (var entry in lines.Where(l => l.Contains("[품목 ")).Select((line, index) => (line, index))) {
        using var json = JsonDocument.Parse(entry.line[entry.line.IndexOf("{", StringComparison.Ordinal)..]);
        Check(json.RootElement.GetProperty("DisplayName").GetString() == (entry.index == 0 ? "달걀" : $"달걀 {entry.index}"), "each item name preserved");
        Check(json.RootElement.GetProperty("ToolOk").GetBoolean() == (entry.index % 2 == 0), "each tool state preserved");
    }
    Check(lines.Any(l => l.Contains("items=12") && l.Contains("exact=1")), "summary retained");
    Check(lines.Last().Contains("종료"), "ends immediately after reads");
}
try {
    await GatheringCliDiagnostic.RunAsync("달걀", ct => { ct.ThrowIfCancellationRequested(); throw new Exception(); },
        (n, ct) => throw new Exception("read after cancellation"), _ => {}, cancelled);
    throw new Exception("cancellation ignored");
} catch (OperationCanceledException) { checks++; }
Console.WriteLine($"GATHERING_DIAGNOSTIC_OK ({checks} checks)");
