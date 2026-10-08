using FishingAutomation;
using System.Text.Json;
using System.Text;

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
    foreach (string command in new[] { "execute_gathering", "execute_altering", "execute_crafting", "complete_altering_work", "get_items extra", "STATUS", "status;execute_crafting", "" })
        Check((await cli.QueryAsync(command)).State == "blocked", "forbidden command reached runner");
}
Check(launches == 0, "blocked commands launched a process");

var actionCalls = new List<string[]>();
var actionCli = new MabinogiMobileCli(log, true,
    (_, _) => Task.FromResult(new CliProcessOutput(0, "{}", "")),
    (arguments, _) =>
    {
        actionCalls.Add(arguments.ToArray());
        return Task.FromResult(new CliProcessOutput(0, "{\"result\":\"accepted\"}", ""));
    });
foreach (var result in new[]
{
    await actionCli.ExecuteGatheringAsync("철 광석"),
    await actionCli.ExecuteAlteringAsync("강철괴"),
    await actionCli.CompleteAlteringWorkAsync("강철괴"),
    await actionCli.StopActionAsync()
})
    Check(result.State == "blocked", "typed CLI action was not blocked");
Check(actionCalls.Count == 0, "blocked typed CLI action reached process runner");

foreach (bool zeroWing in new[] { true, false })
{
    var strictCli = new MabinogiMobileCli(log, zeroWing,
        (_, _) => Task.FromResult(new CliProcessOutput(0, "{}", "")),
        (_, _) => { launches++; return Task.FromResult(new CliProcessOutput(0, "{}", "")); });
    Check((await strictCli.ExecuteGatheringAsync("달걀")).State == "blocked",
        "five-wing execute_gathering is blocked for every config mode");
    Check((await strictCli.ExecuteAlteringAsync("목재+")).State == "blocked", "paid altering cannot launch");
    Check((await strictCli.StopActionAsync()).State == "blocked", "free screen stop cannot use CLI action");
}
Check(launches == 0, "no typed action crossed the process boundary");

var filteredCalls = new List<string[]>();
actionCli.RunFilteredQuery = (arguments, _) =>
{
    filteredCalls.Add(arguments.ToArray());
    return Task.FromResult(new CliProcessOutput(0, "{\"items\":[{\"DisplayName\":\"달걀\",\"ToolOk\":true}]}", ""));
};
Check((await actionCli.GetGatherableItemsAsync("달걀")).Success, "read-only filter search failed");
Check(filteredCalls.Count == 1 && filteredCalls[0][0] == "get_gatherable_items" &&
    Encoding.UTF8.GetString(Convert.FromBase64String(filteredCalls[0][1][7..])) == "달걀",
    "exact Korean filter body preserved");
foreach (string bad in new[] { "", "\n달걀", new string('a', 257) })
    Check((await actionCli.GetGatherableItemsAsync(bad)).State == "blocked", "invalid filter reaches runner");
Check(filteredCalls.Count == 1 && actionCalls.Count == 0, "filter queries never use action runner");

var wrappedStatus = MabinogiMobileCli.Parse("status", new(0,
    "{\"status\":\"accepted\",\"body\":\"{\\\"pipe\\\":\\\"connected\\\"}\"}", ""));
Check(wrappedStatus.Success && wrappedStatus.Data!.Value.GetProperty("pipe").GetString() == "connected",
    "JSON string body was not decoded");

var preflightCli = new MabinogiMobileCli(log, true, (command, _) =>
{
    string stdout = command switch
    {
        "status" => "{\"status\":\"accepted\",\"body\":{\"pipe\":\"connected\"}}",
        "capabilities" => "{\"status\":\"accepted\",\"body\":{\"loading\":false,\"commands\":[{\"Command\":\"get_my_info\",\"Metadata\":{}},{\"Command\":\"get_currencies\",\"Metadata\":{}},{\"Command\":\"execute_gathering\",\"Metadata\":{\"requiresConfirm\":\"true\"}},{\"Command\":\"stop_action\",\"Metadata\":{\"requiresConfirm\":false}}]}}",
        _ => "{}"
    };
    return Task.FromResult(new CliProcessOutput(0, stdout, ""));
});
var caps = await CliAutomationGuards.EnsureCapabilitiesAsync(preflightCli,
    new[] { "get_my_info", "get_currencies", "execute_gathering", "stop_action" });
Check(caps.Count == 4 && caps["execute_gathering"].RequiresConfirm && !caps["stop_action"].RequiresConfirm,
    "capabilities or requiresConfirm parsing failed");
try
{
    await CliAutomationGuards.EnsureCapabilitiesAsync(preflightCli, new[] { "execute_altering" });
    throw new Exception("missing capability accepted");
}
catch (InvalidOperationException) { checks++; }

var identity = CliAutomationGuards.ParseIdentity(MabinogiMobileCli.Parse("get_my_info", new(0,
    "{\"CharacterId\":\"char-1\",\"CharacterName\":\"테스트\",\"AccountCode\":\"account-1\",\"RealmName\":\"서버A\"}", "")));
Check(identity.ComparableFields == 4 && identity.Strength == "강함", "identity fields not parsed");
var sameIdentity = new CliIdentityContext("char-1", "테스트", "account-1", "서버A");
var changedIdentity = new CliIdentityContext("char-2", "테스트", "account-1", "서버A");
Check(identity.Matches(sameIdentity) && !identity.Matches(changedIdentity), "identity change guard failed");

// Production CLI may return scoped fields; never mistake Account.Id for the
// character's ID, or accept inconsistent nested/top-level identity fields.
var nested = CliAutomationGuards.ParseIdentity(MabinogiMobileCli.Parse("get_my_info", new(0,
    """{"Character":{"Id":"char-7","Nickname":"테스트"},"Account":{"Id":"account-7"},"Server":{"Name":"서버7"}}""", "")));
Check(nested == new CliIdentityContext("char-7", "테스트", "account-7", "서버7"),
    "nested CLI character/account/server identity not parsed");
var accountAndName = CliAutomationGuards.ParseIdentity(MabinogiMobileCli.Parse("get_my_info", new(0,
    """{"CharacterName":"테스트","AccountCode":"account-7"}""", "")));
Check(accountAndName.HasDurableMultiIdentity && accountAndName.CharacterId is null &&
    accountAndName.RealmName is null, "account+name fallback blocked");
Check(!new CliIdentityContext(null, "테스트", null, null).HasDurableMultiIdentity &&
    !new CliIdentityContext(null, null, "account-7", null).HasDurableMultiIdentity,
    "weak identity could bypass durable batch guard");
try
{
    _ = CliAutomationGuards.ParseIdentity(MabinogiMobileCli.Parse("get_my_info", new(0,
        """{"CharacterId":"char-1","Character":{"Id":"char-2"}}""", "")));
    throw new Exception("conflicting nested character IDs accepted");
}
catch (InvalidDataException) { checks++; }

int identityReads = 0;
var stableIdentityCli = new MabinogiMobileCli(log, true, (command, _) =>
{
    if (command != "get_my_info") throw new Exception("unexpected CLI identity command");
    identityReads++;
    string content = identityReads == 1
        ? """{"CharacterName":"테스트"}"""
        : """{"CharacterName":"테스트","AccountCode":"account-7"}""";
    return Task.FromResult(new CliProcessOutput(0, content, ""));
});
var stable = await CliIdentityGuard.CaptureForMultiAlteringAsync(stableIdentityCli, default);
Check(stable.Baseline == accountAndName && identityReads >= 3,
    "stable multi identity should require two matching strong snapshots");

int changedReads = 0;
var changedIdentityCli = new MabinogiMobileCli(log, true, (_, _) =>
    Task.FromResult(new CliProcessOutput(0,
        ++changedReads == 1
            ? """{"CharacterName":"테스트","AccountCode":"account-7"}"""
            : """{"CharacterName":"다른캐릭터","AccountCode":"account-7"}""", "")));
try
{
    _ = await CliIdentityGuard.CaptureForMultiAlteringAsync(changedIdentityCli, default);
    throw new Exception("mid-preflight character swap accepted");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("바뀌었습니다")) { checks++; }

int interruptedIdentityReads = 0;
var interruptedCli = new MabinogiMobileCli(log, true, (_, _) =>
{
    string snapshot = ++interruptedIdentityReads switch
    {
        1 => """{"CharacterId":"char-1","CharacterName":"테스트"}""",
        2 => """{"CharacterName":"테스트"}""",
        _ => """{"CharacterId":"char-2","CharacterName":"테스트"}"""
    };
    return Task.FromResult(new CliProcessOutput(0, snapshot, ""));
});
try
{
    _ = await CliIdentityGuard.CaptureForMultiAlteringAsync(interruptedCli, default);
    throw new Exception("identity swap across weak frame accepted");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("바뀌었습니다")) { checks++; }

var currenciesBefore = CliAutomationGuards.ParseCurrencies(MabinogiMobileCli.Parse("get_currencies", new(0,
    "[{\"DisplayName\":\"정령의 날개\",\"Amount\":100},{\"DisplayName\":\"골드\",\"Amount\":5000}]", "")));
var currenciesAfter = CliAutomationGuards.ParseCurrencies(MabinogiMobileCli.Parse("get_currencies", new(0,
    "[{\"DisplayName\":\"정령의 날개\",\"Amount\":95},{\"DisplayName\":\"골드\",\"Amount\":5100}]", "")));
var currencyChanges = CliAutomationGuards.CurrencyChanges(currenciesBefore, currenciesAfter);
Check(currencyChanges.Any(x => x.Contains("정령의 날개") && x.Contains("-5")) &&
      currencyChanges.Any(x => x.Contains("골드") && x.Contains("+100")),
    "currency delta tracking failed");
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

int visibleBeforePolling = lines.Count;
var quietPollingCli = new MabinogiMobileCli(log, true, (command, _) =>
{
    string stdout = command switch
    {
        "get_items" => "[]",
        "get_my_info" => "{}",
        "get_altering_works" => "{}",
        "get_activity" => "{}",
        _ => "{}"
    };
    return Task.FromResult(new CliProcessOutput(0, stdout, ""));
});
await quietPollingCli.GetItemsAsync();
await quietPollingCli.GetMyInfoAsync();
await quietPollingCli.GetAlteringWorksAsync();
await quietPollingCli.GetActivityAsync();
Check(lines.Count == visibleBeforePolling, "successful routine CLI polling leaked into on-screen log");
Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cli-test.log")).Contains("[CLI] get_items: connected"),
    "background CLI polling was not retained in the file log");

Check(lines.Count > 0 && lines.All(x => x.Contains("[CLI]")), "existing logger integration failed");

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
    foreach (string command in new[] { "status", "capabilities", "get_my_info", "get_currencies", "get_items", "get_activity", "get_current_environment", "get_alterable_items", "get_altering_works" })
    {
        var result = await defaults.QueryAsync(command);
        Console.WriteLine($"LIVE {command}: success={result.Success} state={result.State} exit={result.ExitCode}");
        Check(result.Success && result.Data is not null, $"Live {command} failed: {result.State}/{result.Error}");
        if (command == "capabilities") Check((await CliAutomationGuards.EnsureCapabilitiesAsync(defaults, new[] { "get_my_info", "get_currencies" })).Count > 0, "live capabilities parsing failed");
        if (command == "get_my_info") Check(CliAutomationGuards.ParseIdentity(result).ComparableFields > 0, "live identity parsing failed");
        if (command == "get_currencies") { CliAutomationGuards.ParseCurrencies(result); checks++; }
        if (command == "get_alterable_items") Check(AlteringQueries.ParseRecipes(result).Count > 0, "live recipe parsing failed");
        if (command == "get_altering_works") { AlteringQueries.ParseWorks(result); checks++; }
    }
}
Console.WriteLine($"PASS {checks} checks");
