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

// Regression for 2026-10-12 V3.1.87 live report: the game menu was
// visibly open, but step 1 searched only the CLOSED-menu ESC template
// until it timed out. Two independent frames of the Abyss item in the
// correct right-side menu must bypass the ESC click.
var menuA = new DetectionResult(true, new Rectangle(495, 315, 64, 68), 0.91, null);
var menuB = new DetectionResult(true, new Rectangle(497, 316, 64, 68), 0.90, null);
Assert(AbyssMenuEntryPolicy.IconEvidence(menuA.Bounds) &&
       AbyssMenuEntryPolicy.Stable(menuA, menuB),
    "already-open menu: two stable Abyss-icon frames skip ESC and advance");
Assert(!AbyssMenuEntryPolicy.Stable(DetectionResult.NotFound, menuA),
    "one menu frame cannot authorize skip or click");
Assert(!AbyssMenuEntryPolicy.Stable(menuA,
       new DetectionResult(true, new Rectangle(170, 315, 64, 68), 0.97, null)),
    "similar icon in field or wrong screen must not authorize menu click");
Assert(!AbyssMenuEntryPolicy.Stable(menuA,
       new DetectionResult(true, new Rectangle(489, 440, 64, 68), 0.99, null)),
    "other rows in the open menu are not Abyss");
Assert(AbyssMenuEntryPolicy.ContextEvidence(new Rectangle(498, 575, 48, 18)) &&
       !AbyssMenuEntryPolicy.ContextEvidence(new Rectangle(498, 360, 48, 18)),
    "OCR fallback requires independent Guild label below Abyss in menu");
Assert(AbyssMenuEntryPolicy.ShouldRunAnotherCycle(false, true, false, 1, 6),
    "non-repeating scheduled Abyss continues after verified entry recovery");
Assert(!AbyssMenuEntryPolicy.ShouldRunAnotherCycle(false, true, true, 1, 6),
    "verified outside HUD ends scheduled dungeon without restart");
Assert(!AbyssMenuEntryPolicy.ShouldRunAnotherCycle(false, true, false, 6, 6),
    "bounded recovery prevents endless scheduled menu retries");
Assert(!AbyssMenuEntryPolicy.ShouldRunAnotherCycle(false, false, false, 1, 6),
    "ordinary non-repeat mode never inherits scheduler-specific retry behavior");

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

// Departure is an efficiency gate, NOT an Abyss battle duration limit.
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(120) }, 180),
    "180s selectable departure gate rejects a shorter processing wait");
Assert(AbyssAlteringSchedulePolicy.CanRun(new[] { Work(200), Work(180) }, 180),
    "180s selectable departure gate accepts all active lanes");
Assert(!AbyssAlteringSchedulePolicy.CanRun(new[] { Work(200) }, -1),
    "invalid departure gate fails closed");

// The manager watchdog excludes every second spent inside Abyss, regardless
// of how long the dungeon takes. The server-owned processing countdown does
// not stop; the next manager read must query the real works anew.
DateTimeOffset pauseClock = new(2026, 10, 12, 1, 0, 0, TimeSpan.Zero);
var pauseWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => pauseClock);
var pauseWork = Work(400);
pauseWatch.Observe("가죽 가공 시설", new[] { pauseWork });
pauseClock = pauseClock.AddSeconds(100);
pauseWatch.PauseForAbyss();
pauseClock = pauseClock.AddMinutes(11); // variable dungeon length, longer than stall threshold
bool pausedWatchRejectedRead = false;
try { pauseWatch.Observe("가죽 가공 시설", new[] { pauseWork }); }
catch (InvalidOperationException) { pausedWatchRejectedRead = true; }
Assert(pausedWatchRejectedRead,
    "no processing manager/watchdog observation while Abyss owns the UI");
pauseWatch.ResumeAfterAbyssExit();
pauseWatch.Observe("가죽 가공 시설", new[] { pauseWork });
pauseClock = pauseClock.AddSeconds(19);
pauseWatch.Observe("가죽 가공 시설", new[] { pauseWork });
pauseClock = pauseClock.AddSeconds(2);
bool pauseWatchEventuallyStopped = false;
try { pauseWatch.Observe("가죽 가공 시설", new[] { pauseWork }); }
catch (InvalidOperationException ex)
{
    pauseWatchEventuallyStopped = ex.Message.Contains("다중가공 시설별 정체 감지");
}
Assert(pauseWatchEventuallyStopped,
    "11 minutes inside Abyss excluded, 101+20 seconds active stall still detected");

// Even a 1-hour Abyss excursion cannot trigger a false stall on the first
// fresh processing read after verified exit.
DateTimeOffset longClock = new(2026, 10, 12, 1, 0, 0, TimeSpan.Zero);
var longWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => longClock);
longWatch.Observe("가죽 가공 시설", new[] { pauseWork });
longWatch.PauseForAbyss();
longClock = longClock.AddHours(1);
longWatch.ResumeAfterAbyssExit();
longWatch.Observe("가죽 가공 시설", new[] { pauseWork });
Assert(true, "Abyss duration never becomes manager stall time after return");

// Fully completed output is ready for managed receipt, not stalled production.
// Reproduce the reported five completed slots unchanged across 133 active seconds.
DateTimeOffset receiptClock = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
var receiptWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => receiptClock);
var receiptWorks = Enumerable.Repeat(Work(0, true), 5).ToArray();
receiptWatch.Observe("가죽 가공 시설", receiptWorks);
receiptClock = receiptClock.AddSeconds(133);
bool completedReceiptRejected = false;
try { receiptWatch.Observe("가죽 가공 시설", receiptWorks); }
catch (InvalidOperationException) { completedReceiptRejected = true; }
Assert(!completedReceiptRejected,
    "five completed slots waiting 133 seconds remain eligible for managed receipt");

// Veto: completed slots must not mask a frozen unfinished job in the same lane.
var mixedReceiptWorks = receiptWorks.Concat(new[] { Work(45) }).ToArray();
receiptWatch.Observe("가죽 가공 시설", mixedReceiptWorks);
receiptClock = receiptClock.AddSeconds(133);
bool mixedReceiptStopped = false;
try { receiptWatch.Observe("가죽 가공 시설", mixedReceiptWorks); }
catch (InvalidOperationException ex)
{
    mixedReceiptStopped = ex.Message.Contains("다중가공 시설별 정체 감지");
}
Assert(mixedReceiptStopped,
    "five completed slots plus a frozen unfinished slot still veto processing after 133 seconds");

Assert(ScheduledAbyssReturnPolicy.RecoveryResultAction(true) ==
       AbyssRecoveryResultAction.ExitForProcessing,
    "scheduled recovery result must use LEFT exit, never middle retry");
Assert(ScheduledAbyssReturnPolicy.RecoveryResultAction(false) ==
       AbyssRecoveryResultAction.RetryDungeon,
    "ordinary Abyss recovery keeps its existing middle retry behavior");

// A non-repeating scenario can return without finishing after network restart,
// Timeout Smart Recovery, or RestartCycleException. Such a return is NOT proof
// that the character left Abyss.
bool silentRestartBlocked = false;
try
{
    ScheduledAbyssReturnPolicy.RequireVerifiedOutside(
        scheduledResultExit: true, outsideHudVerified: false, CancellationToken.None);
}
catch (InvalidOperationException ex)
{
    silentRestartBlocked = ex.Message.Contains("던전 밖 HUD 확인 기록이 없습니다");
}
Assert(silentRestartBlocked,
    "silent scheduled restart/reconnect/recovery return cannot release processing");
ScheduledAbyssReturnPolicy.RequireVerifiedOutside(
    scheduledResultExit: true, outsideHudVerified: true, CancellationToken.None);
Assert(true, "only verified outside HUD allows scheduled return");
ScheduledAbyssReturnPolicy.RequireVerifiedOutside(
    scheduledResultExit: false, outsideHudVerified: false, CancellationToken.None);
Assert(true, "ordinary unscheduled Abyss unaffected by scheduler proof");
using (var stopAfterExit = new CancellationTokenSource())
{
    stopAfterExit.Cancel();
    bool canceledEvenWithProof = false;
    try
    {
        ScheduledAbyssReturnPolicy.RequireVerifiedOutside(
            scheduledResultExit: true, outsideHudVerified: true, stopAfterExit.Token);
    }
    catch (OperationCanceledException) { canceledEvenWithProof = true; }
    Assert(canceledEvenWithProof,
        "F10 always prevents processing even after outside HUD proof");
}

// Run the real coordinator with pure fake data: the input owner never overlaps.
var plan = new AlteringPlan("가죽 가공 시설", "가죽", 1, 1, false);
var simulatedWorks = new List<AlteringWork> { Work(200) };
var events = new List<string>();
int readsAfterOutside = 0;
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
    token =>
    {
        if (events.Contains("abyss")) readsAfterOutside++;
        return Task.FromResult<IReadOnlyList<AlteringWork>>(simulatedWorks.ToArray());
    },
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
Assert(readsAfterOutside > 0,
    "confirmed outside exit causes a fresh server works read before processing");

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

// Regression for the real bug: previously a scheduled non-repeat Abyss engine
// could silently complete after a network restart. The callback must reject
// this and the manager must never read fresh processing jobs or press a key.
int readsAfterSilentReturn = 0;
int registrationsAfterSilentReturn = 0;
bool silentReturnStoppedCoordinator = false;
try
{
    var coordinatorAfterSilentReturn = new MultiAlteringCoordinator();
    using var timeoutAfterSilentReturn = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await coordinatorAfterSilentReturn.RunAsync(
        new[] { plan },
        (_, _, _) =>
        {
            registrationsAfterSilentReturn++;
            return Task.FromResult(false);
        },
        _ =>
        {
            readsAfterSilentReturn++;
            return Task.FromResult<IReadOnlyList<AlteringWork>>(new[] { Work(300) });
        },
        (_, _) => throw new Exception("Unexpected delay after unverified recovery"),
        timeoutAfterSilentReturn.Token,
        onIdleAbyssRound: token =>
        {
            ScheduledAbyssReturnPolicy.RequireVerifiedOutside(
                scheduledResultExit: true, outsideHudVerified: false, token);
            return Task.CompletedTask;
        });
}
catch (InvalidOperationException ex) when (
    ex.Message.Contains("던전 밖 HUD 확인 기록이 없습니다"))
{
    silentReturnStoppedCoordinator = true;
}
Assert(silentReturnStoppedCoordinator && readsAfterSilentReturn > 0 &&
       readsAfterSilentReturn < 4 && registrationsAfterSilentReturn == 0,
    "failed scheduled return blocks subsequent processing polling and registration");

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

// Fixed input targets must be independent of OCR glyph centers. The actual
// 2026-10-12 Windows 800x1000 runtime failure screenshot shows the left green
// "나가기" button at roughly x=180..319/y=922..977 and retry at x=330..469.
// Regression: (165,930) was *outside* the exit button and failed the click
// patch green check, even though all three result buttons had been detected.
Point fixedExit = AbyssResultLayout.FixedButtonCenter(true, new Size(800, 1000));
Point fixedRetry = AbyssResultLayout.FixedButtonCenter(false, new Size(800, 1000));
var observedExitButton = new Rectangle(180, 922, 140, 56);
var observedRetryButton = new Rectangle(330, 922, 140, 56);
var exitGreenPatch = new Rectangle(fixedExit.X - 24, fixedExit.Y - 24, 48, 48);
Assert(fixedExit == new Point(250, 950) && fixedRetry == new Point(400, 930),
    "live-capture-corrected exit center; ordinary retry center stays unchanged");
Assert(!observedExitButton.Contains(new Point(165, 930)),
    "old scheduled exit coordinate is outside the captured green button");
Assert(observedExitButton.Contains(fixedExit) && observedExitButton.Contains(exitGreenPatch),
    "scheduled exit target and entire 48x48 green patch lie inside captured exit button");
Assert(observedRetryButton.Contains(fixedRetry) && !observedRetryButton.Contains(fixedExit) &&
       !observedExitButton.Contains(fixedRetry),
    "exit and retry click positions stay in different buttons");
Assert(AbyssResultLayout.FixedButtonCenter(true, new Size(815, 920)) == new Point(255, 874) &&
       AbyssResultLayout.FixedButtonCenter(false, new Size(815, 920)) == new Point(408, 856),
    "fixed centers scale independently for real 815x920 game-client capture");
bool refusedInvalidAspect = false;
try { _ = AbyssResultLayout.FixedButtonCenter(true, new Size(706, 1536)); }
catch (InvalidOperationException) { refusedInvalidAspect = true; }
Assert(refusedInvalidAspect, "do not click unadjusted coordinates on an iPhone screenshot aspect");
bool refusedMissingClient = false;
try { _ = AbyssResultLayout.FixedButtonCenter(false, Size.Empty); }
catch (InvalidOperationException) { refusedMissingClient = true; }
Assert(refusedMissingClient, "missing client geometry blocks fixed click");


// Scheduler resume path must reuse the durable manual-resume gate, not sneak a
// new F9 ledger under an "이어하기" label. This is a source wiring contract;
// the Windows workflow also builds the real MainForm implementation.
string schedulerRunner = File.ReadAllText(
    Path.Combine(Directory.GetCurrentDirectory(),
        "source/FishingAutomation/MainForm.SchedulerRunner.cs"));
string schedulerResume = File.ReadAllText(
    Path.Combine(Directory.GetCurrentDirectory(),
        "source/FishingAutomation/MainForm.SchedulerResume.cs"));
Assert(schedulerRunner.Contains("ChooseSchedulerMultiAlteringAsync(") &&
       schedulerRunner.Contains("resume?.Directory, resume?.BatchId") &&
       schedulerRunner.Contains("resume?.ConfirmedSingleCharacter ?? false"),
    "scheduler forwards verified batch identity and character consent to multi-altering");
Assert(schedulerResume.Contains("MultiAlteringRecentResume.Latest(root)") &&
       schedulerResume.Contains("MultiAlteringRecentResume.VerifyAsync(") &&
       schedulerResume.Contains("DialogResult.Yes") &&
       schedulerResume.Contains("DialogResult.Cancel"),
    "scheduler requires explicit fresh/resume/cancel and verified latest manifest");
Assert(schedulerResume.Contains("token.ThrowIfCancellationRequested()") &&
       schedulerResume.Contains("craftingSelected") &&
       schedulerRunner.IndexOf("ChooseSchedulerMultiAlteringAsync(", StringComparison.Ordinal) <
       schedulerRunner.IndexOf("await StartCraftingAsync();", StringComparison.Ordinal),
    "F10 and user cancel block crafting, and crafting restarts only after resume selection");


// 2026-10-13 21:24 V3.2.1 live failure: a single long green 확인
// button remains under 협동 미션 완료 after the validated LEFT exit.
// The previous three-green-region detector must not treat the one
// continuous button as the ordinary 3-button retry result screen.
using (var missionFrame = new Bitmap(800, 1000))
using (var g = Graphics.FromImage(missionFrame))
{
    g.Clear(Color.FromArgb(12, 23, 29));
    using var green = new SolidBrush(Color.FromArgb(14, 176, 120));
    using var gold = new SolidBrush(Color.FromArgb(230, 182, 66));
    g.FillRectangle(green, 176, 922, 448, 56);
    g.FillRectangle(gold, 340, 110, 120, 115);
    Assert(AbyssMissionCompletePolicy.HasVisualEvidence(missionFrame),
        "long single confirm button + gold mission title accepts post-exit panel evidence");
    Assert(AbyssMissionCompletePolicy.ConfirmCenter(missionFrame.Size) == new Point(400, 950),
        "post-exit mission confirm uses fixed center of ONE green button");

    using var threeButtons = new Bitmap(800, 1000);
    using var three = Graphics.FromImage(threeButtons);
    three.Clear(Color.Black);
    three.FillRectangle(gold, 340, 110, 120, 115);
    three.FillRectangle(green, 180, 922, 140, 56);
    three.FillRectangle(green, 330, 922, 140, 56);
    three.FillRectangle(green, 480, 922, 140, 56);
    Assert(!AbyssMissionCompletePolicy.HasVisualEvidence(threeButtons),
        "three-button result layout rejected even with gold backdrop");

    g.FillRectangle(Brushes.Black, 340, 110, 120, 115);
    Assert(!AbyssMissionCompletePolicy.HasVisualEvidence(missionFrame),
        "green generic confirm alone must not authorize mission panel");
}
Assert(AbyssMissionCompletePolicy.IsStableTextEvidence(
        new Rectangle(337, 326, 97, 16), new Rectangle(339, 326, 95, 16),
        new Rectangle(381, 939, 41, 20), new Rectangle(382, 940, 39, 18),
        new Size(800, 1000), new Size(800, 1000), giftHeading: true),
    "two frames must agree on exact gift heading and exact 확인 label");
Assert(!AbyssMissionCompletePolicy.IsStableTextEvidence(
        new Rectangle(337, 326, 97, 16), Rectangle.Empty,
        new Rectangle(381, 939, 41, 20), new Rectangle(382, 940, 39, 18),
        new Size(800, 1000), new Size(800, 1000), giftHeading: true),
    "missing heading on second frame forbids confirmation click");
Assert(!AbyssMissionCompletePolicy.IsStableTextEvidence(
        new Rectangle(337, 326, 97, 16), new Rectangle(339, 326, 95, 16),
        new Rectangle(381, 939, 41, 20), Rectangle.Empty,
        new Size(800, 1000), new Size(800, 1000), giftHeading: true),
    "missing button label on second frame forbids confirmation click");
bool badMissionAspectBlocked = false;
try { _ = AbyssMissionCompletePolicy.ConfirmCenter(new Size(706, 1536)); }
catch (InvalidOperationException) { badMissionAspectBlocked = true; }
Assert(badMissionAspectBlocked, "mission confirm never clicks invalid client aspect");

string scheduleExitSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs"));
string sceneSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "source/FishingAutomation/dungeon/ScenarioEngine.cs"));
string sceneMissionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "source/FishingAutomation/dungeon/ScenarioEngine.AbyssMissionComplete.cs"));
Assert(scheduleExitSource.Contains("_scheduledExitClicked = true") &&
       sceneSource.Contains("TryAcknowledgeScheduledMissionCompleteAsync(frame, ct)") &&
       sceneMissionSource.Contains("VerifyAbyssClickClientGeometry") &&
       sceneMissionSource.Contains("_scheduledMissionConfirmationClicked = true") &&
       sceneMissionSource.Contains("FindCompactLabelAsync"),
    "post-exit mission confirm is one-shot, OCR-checked and guarded by exit proof");

Console.WriteLine("Combined scheduler regressions passed.");
