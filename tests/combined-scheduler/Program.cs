using FishingAutomation;
using DungeonVisionBot;
using System.Drawing;
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

// A failed exit must never release the input lease back to production.
int callsAfterFailure = 0;
bool caughtExitFailure = false;
try
{
    var failureCoordinator = new MultiAlteringCoordinator();
    using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await failureCoordinator.RunAsync(
        new[] { plan },
        (_, _, _) =>
        {
            callsAfterFailure++;
            return Task.FromResult(false);
        },
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(new[] { Work(300) }),
        (_, _) => throw new Exception("Delay is not permitted on failure"),
        failureTimeout.Token,
        onIdleAbyssRound: _ => throw new InvalidOperationException("Unverified exit"));
}
catch (InvalidOperationException ex) when (ex.Message == "Unverified exit")
{
    caughtExitFailure = true;
}
Assert(caughtExitFailure && callsAfterFailure == 0,
    "failed abyss exit stops before any further processing action");

// Cancellation during the handoff also forbids subsequent input.
int callsAfterCancel = 0;
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
bool cancellationObserved = false;
try
{
    var cancelCoordinator = new MultiAlteringCoordinator();
    await cancelCoordinator.RunAsync(
        new[] { plan },
        (_, _, _) =>
        {
            callsAfterCancel++;
            return Task.FromResult(false);
        },
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(new[] { Work(300) }),
        (_, _) => throw new Exception("Delay is not permitted on cancellation"),
        cancel.Token,
        onIdleAbyssRound: _ =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        });
}
catch (OperationCanceledException)
{
    cancellationObserved = true;
}
Assert(cancellationObserved && callsAfterCancel == 0,
    "F10 cancellation inside abyss prevents another processing action");

var cfg = JsonDocument.Parse(File.ReadAllText(
    Path.Combine(Directory.GetCurrentDirectory(), "source/FishingAutomation/abyss/config/scenario.json")));
Assert(cfg.RootElement.GetProperty("Repeat").GetBoolean(),
    "packaged ordinary abyss scenario still repeats forever");
// User-supplied full-screen phone image (706 x 1536) provides independent
// *layout* evidence, not a Windows client capture. Normalized to 800 x 1000,
// the left "나가기" text begins left of the original x=160 OCR crop.
var leftCrop = AbyssResultLayout.ScheduledExitLabelRoi;
Assert(leftCrop.Contains(new Rectangle(120, 915, 90, 25)),
    "portrait screenshot's normalized full left exit label fits the OCR crop");
Assert(leftCrop.Right < 310, "left exit-label crop cannot include the middle retry label");
Assert(AbyssResultLayout.HasStableScheduledExitLabel(
        new Rectangle(120, 918, 93, 22), new Rectangle(122, 917, 89, 23),
        leftCrop, leftCrop),
    "two exact left exit labels in overlapping locations authorize the click");
Assert(!AbyssResultLayout.HasStableScheduledExitLabel(
        new Rectangle(120, 918, 93, 22), new Rectangle(230, 918, 60, 22),
        leftCrop, leftCrop),
    "two different left-side text locations do not authorize the click");
Assert(!AbyssResultLayout.HasStableScheduledExitLabel(
        new Rectangle(120, 918, 93, 22), new Rectangle(320, 918, 75, 22),
        leftCrop, leftCrop),
    "OCR in middle retry button must never authorize left exit");
Assert(!AbyssResultLayout.HasStableScheduledExitLabel(
        Rectangle.Empty, new Rectangle(120, 918, 93, 22), leftCrop, leftCrop),
    "missing first result text fails closed");

Console.WriteLine("Combined scheduler regressions passed.");
