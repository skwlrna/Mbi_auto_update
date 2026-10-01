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
try {
    await GatheringCliDiagnostic.RunAsync("달걀", ct => { ct.ThrowIfCancellationRequested(); throw new Exception(); },
        (n, ct) => throw new Exception("read after cancellation"), _ => {}, cancelled);
    throw new Exception("cancellation ignored");
} catch (OperationCanceledException) { checks++; }
Console.WriteLine($"GATHERING_DIAGNOSTIC_OK ({checks} checks)");
