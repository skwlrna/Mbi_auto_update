using FishingAutomation;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

static AlteringWork Work(long seconds, bool completed = false)
    => new("가죽", "가죽 가공 시설",
        completed ? "Completed" : "InProgress", completed, seconds);

Assert(!AbyssAlteringSchedulePolicy.CanRun(Array.Empty<AlteringWork>()),
    "no work: never enter abyss");
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(89) }),
    "short remaining work: wait normally");
Assert(AbyssAlteringSchedulePolicy.CanRun(new[] { Work(90) }),
    "90s remaining: safe to yield");
Assert(AbyssAlteringSchedulePolicy.CanRun(new[] { Work(700), Work(180), Work(0, true) }),
    "completed slots may remain with other running slots");
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(500), Work(30) }),
    "shortest remaining active job controls handoff");
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(-1) }),
    "invalid timer: no excursion");
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(0, true) }),
    "all slots complete: process before abyss");

// Run the real coordinator with pure fake data: the input owner never overlaps.
var plan = new AlteringPlan("가죽 가공 시설", "가죽", 1, 1, false);
var simulatedWorks = new List<AlteringWork> { Work(200) };
var events = new List<string>();
var coordinator = new MultiAlteringCoordinator();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await coordinator.RunAsync(
    new[] { plan },
    async (_, _, token) =>
    {
        events.Add("processing");
        token.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        return true;
    },
    token => Task.FromResult<IReadOnlyList<AlteringWork>>(simulatedWorks.ToArray()),
    (_, _) => throw new Exception("Unexpected ordinary delay when abyss was eligible"),
    timeout.Token,
    onIdleAbyssRound: async token =>
    {
        token.ThrowIfCancellationRequested();
        Assert(!events.Contains("processing"), "no processing input during abyss turn");
        events.Add("abyss");
        simulatedWorks.Clear(); // Simulate verified exit followed by refreshed CLI.
        await Task.CompletedTask;
    });
Assert(events.SequenceEqual(new[] { "abyss", "processing" }),
    "abyss finishes and yields before processing next action");

var cfg = JsonDocument.Parse(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "../../../../source/FishingAutomation/abyss/config/scenario.json")));
Assert(cfg.RootElement.GetProperty("Repeat").GetBoolean(),
    "packaged ordinary abyss scenario still repeats forever");
Console.WriteLine("Combined scheduler regressions passed.");
