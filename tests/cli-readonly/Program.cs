using FishingAutomation;
using System.Text.Json;

int checks = 0;
void Check(bool passed, string label)
{
    if (!passed) throw new Exception(label);
    checks++;
}
var log = new AppLog(Path.Combine(AppContext.BaseDirectory, "cli-test.log"), 1024 * 1024);
var lines = new List<string>();
log.Line += lines.Add;
int launches = 0;
foreach (bool zeroWing in new[] { true, false })
{
    var cli = new MabinogiMobileCli(log, zeroWing, (_, _) =>
    {
        launches++;
        return Task.FromResult(new CliProcessOutput(0, "{}", ""));
    });
    foreach (string command in new[] { "execute_gathering", "execute_altering", "execute_crafting", "complete_altering_work", "stop_action", "get_items extra", "STATUS", "status;execute_crafting", "" })
        Check((await cli.QueryAsync(command)).State == "blocked", "forbidden command reached runner");
}
Check(launches == 0, "blocked commands launched a process");
foreach (string reason in new[] { "game_off", "option_off" })
    foreach (int exit in new[] { 0, 1, 5 })
        Check(MabinogiMobileCli.Parse("status", new(exit, $"{{\"pipe\":\"disconnected\",\"reason\":\"{reason}\"}}", "")).State == reason, "connection reason lost");
Check(MabinogiMobileCli.Parse("status", new(5, "{\"pipe\":\"disconnected\"}", "")).State == "disconnected", "disconnect lost");
Check(!MabinogiMobileCli.Parse("get_activity", new(0, "{\"status\":\"rejected\"}", "")).Success, "zero exit rejected response accepted");
Check(!MabinogiMobileCli.Parse("get_activity", new(0, "{\"body\":{\"error\":\"failure\"}}", "")).Success, "body error accepted");
Check(!MabinogiMobileCli.Parse("get_activity", new(2, "{}", "")).Success, "nonzero exit accepted");
Check(!MabinogiMobileCli.Parse("get_items", new(0, "not json", "")).Success, "bad JSON accepted");
Check(!MabinogiMobileCli.Parse("get_items", new(0, "{}", "")).Success, "wrong inventory shape accepted");
Check(!MabinogiMobileCli.Parse("status", new(0, "{}", "")).Success, "missing connectivity accepted");
Check(!MabinogiMobileCli.Parse("get_activity", new(0, "null", "")).Success, "null accepted");
var unicode = MabinogiMobileCli.Parse("get_items", new(0, "[{\"DisplayName\":\"\\uB9C8\\uB098\\uC11D\",\"Count\":3}]", ""));
Check(unicode.Success && unicode.Data!.Value[0].GetProperty("DisplayName").GetString() == "마나석", "Unicode decoding failed");
var detached = unicode.Data!.Value.Clone();
Check(detached[0].GetProperty("Count").GetInt32() == 3, "JSON lifetime invalid");
var defaults = new MabinogiMobileCli(log);
Check(defaults.ZeroWingMode, "ZeroWingMode default is not true");
Check(JsonSerializer.Deserialize<AutomationConfig>("{}")!.ZeroWingMode, "legacy config default is not true");
Check(!JsonSerializer.Deserialize<AutomationConfig>("{\"ZeroWingMode\":false}")!.ZeroWingMode, "explicit config value lost");
var unavailable = new MabinogiMobileCli(log, true, (_, _) => throw new IOException());
Check((await unavailable.StatusAsync()).State == "disconnected", "missing executable did not return disconnected");
var timedOut = new MabinogiMobileCli(log, true, (_, _) => throw new OperationCanceledException());
Check((await timedOut.StatusAsync()).Error == "timeout", "timeout lost");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await defaults.StatusAsync(cancelled.Token); throw new Exception("cancellation ignored"); }
catch (OperationCanceledException) { checks++; }
Check(lines.Count > 0 && lines.All(x => x.Contains("[CLI]")), "existing logger integration failed");

var wrappedCapabilities = MabinogiMobileCli.Parse("capabilities", new(0,
    "{\"status\":\"accepted\",\"body\":\"{\\\"commands\\\":[{\\\"Command\\\":\\\"execute_gathering\\\",\\\"Metadata\\\":{\\\"requiresConfirm\\\":\\\"false\\\"}}]}\"}", ""));
Check(wrappedCapabilities.Success && wrappedCapabilities.Data!.Value.GetProperty("commands")[0].GetProperty("Command").GetString() == "execute_gathering",
    "string body capabilities were not decoded");
string expectedBody = "base64:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"displayName\":\"철 광석\"}"));
Check(MabinogiMobileCli.BodyArgument("{\"displayName\":\"철 광석\"}") == expectedBody, "base64 body argument mismatch");

var actionLaunches = new List<(string Command, string? Body)>();
var actionCli = new MabinogiMobileCli(log, true, (command, body, _) =>
{
    if (command == "capabilities")
        return Task.FromResult(new CliProcessOutput(0,
            "{\"commands\":[{\"Command\":\"execute_gathering\",\"Metadata\":{\"requiresConfirm\":\"false\"}},{\"Command\":\"execute_altering\",\"Metadata\":{\"requiresConfirm\":\"true\"}},{\"Command\":\"complete_altering_work\",\"Metadata\":{\"requiresConfirm\":\"true\"}}]}", ""));
    actionLaunches.Add((command, body));
    return Task.FromResult(new CliProcessOutput(0, "{\"status\":\"accepted\",\"body\":{\"message\":\"ok\"}}", ""));
});
Check((await actionCli.ExecuteGatheringAsync("철 광석")).Success, "zero-wing unconfirmed gathering action did not execute");
Check(actionLaunches.Count == 1 && actionLaunches[0].Command == "execute_gathering" &&
    actionLaunches[0].Body == expectedBody, "gathering action body or command mismatch");
Check(!(await actionCli.ExecuteAlteringAsync("강철괴", false)).Success && actionLaunches.Count == 1,
    "confirmation-required altering bypassed explicit cost approval");
Check((await actionCli.ExecuteAlteringAsync("강철괴", true)).Success && actionLaunches.Count == 2,
    "approved altering action did not execute");
Check((await actionCli.CompleteAlteringWorkAsync("강철괴")).Success && actionLaunches.Count == 3,
    "completed altering work action did not execute");

int blockedGatherLaunches = 0;
var blockedGather = new MabinogiMobileCli(log, true, (command, body, _) =>
{
    if (command == "capabilities")
        return Task.FromResult(new CliProcessOutput(0,
            "{\"commands\":[{\"Command\":\"execute_gathering\",\"Metadata\":{\"requiresConfirm\":true}}]}", ""));
    blockedGatherLaunches++;
    return Task.FromResult(new CliProcessOutput(0, "{\"message\":\"unexpected\"}", ""));
});
var blockedGatherResult = await blockedGather.ExecuteGatheringAsync("철 광석");
Check(!blockedGatherResult.Success && blockedGatherResult.Error == "confirmation_required" && blockedGatherLaunches == 0,
    "ZeroWingMode allowed confirmation-required execute_gathering");

var recipes = AlteringQueries.ParseRecipes(MabinogiMobileCli.Parse("get_alterable_items", new(0,
    "{\"items\":[{\"DisplayName\":\"목재+\",\"Alterable\":true,\"ProducedPerWork\":3},{\"DisplayName\":\"강철괴\",\"Alterable\":false,\"ProducedPerWork\":3,\"Reason\":\"not_enough_ingredient\",\"MissingIngredients\":[{\"DisplayName\":\"철괴\",\"Required\":3,\"Owned\":2}]}]}", "")));
Check(recipes.Count == 2 && recipes[0].DisplayName == "목재+" && recipes[0].ProducedPerWork == 3, "recipe name and produced amount lost");
Check(!recipes[1].Alterable && recipes[1].MissingIngredients[0].Owned == 2, "missing materials lost");
var works = AlteringQueries.ParseWorks(MabinogiMobileCli.Parse("get_altering_works", new(0,
    "{\"completedCount\":1,\"works\":[{\"DisplayName\":\"강철괴\",\"FacilityName\":\"금속 가공 시설\",\"State\":\"Completed\",\"IsCompleted\":true,\"RemainingSeconds\":0},{\"DisplayName\":\"강철괴\",\"FacilityName\":\"금속 가공 시설\",\"State\":\"NotStarted\",\"IsCompleted\":false,\"RemainingSeconds\":300}]}", "")));
Check(works.Count == 2 && works[0].IsCompleted && works[1].State == "NotStarted", "work states conflated");
try
{
    AlteringQueries.ParseWorks(MabinogiMobileCli.Parse("get_altering_works", new(0, "{\"completedCount\":1,\"works\":[]}", "")));
    throw new Exception("inconsistent completed count accepted");
}
catch (InvalidDataException) { checks++; }
try
{
    AlteringQueries.ParseRecipes(new("get_alterable_items", false, "disconnected", null, 5, "game_off"));
    throw new Exception("failed recipe query accepted");
}
catch (InvalidDataException) { checks++; }

if (args.Contains("--live"))
{
    // Only allowlisted reads are ever launched here. Never launch the macro UI.
    foreach (string command in new[] { "status", "capabilities", "get_items", "get_activity", "get_current_environment", "get_alterable_items", "get_altering_works" })
    {
        var result = await defaults.QueryAsync(command);
        Console.WriteLine($"LIVE {command}: success={result.Success} state={result.State} exit={result.ExitCode}");
        Check(result.Success && result.Data is not null, $"Live {command} failed: {result.State}/{result.Error}");
        if (command == "get_alterable_items") Check(AlteringQueries.ParseRecipes(result).Count > 0, "live recipe parsing failed");
        if (command == "get_altering_works") { AlteringQueries.ParseWorks(result); checks++; }
    }
}
Console.WriteLine($"PASS {checks} checks");
