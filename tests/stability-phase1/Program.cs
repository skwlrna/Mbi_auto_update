using FishingAutomation;

int checks = 0;
void Check(bool ok, string reason)
{
    if (!ok) throw new InvalidOperationException("STABILITY_PHASE1_FAIL: " + reason);
    checks++;
}
var clock = DateTimeOffset.Parse("2026-11-03T00:00:00+00:00");
var log = new List<MacroStabilityEvent>();
MacroStabilitySupervisor New(TimeSpan? timeout = null, int recoveries = 2)
    => new(() => clock, log.Add, timeout ?? TimeSpan.FromSeconds(10), recoveries);

// UI admission is a single-owner policy even while async startup is pending.
var run = New();
Check(run.TryBegin(MacroStableMode.Crafting), "first crafting admission");
Check(!run.TryBegin(MacroStableMode.Abyss), "cross-mode input ownership denied");
Check(run.Owner == MacroStableMode.Crafting &&
      run.State == MacroStableState.Starting, "rejected start keeps owner");
run.ObserveRuntime(MacroStableMode.Crafting, busy: true);
Check(run.State == MacroStableState.Running, "observed crafting worker runs");
run.ObserveRuntime(MacroStableMode.Abyss, busy: true);
run.ObserveRuntime(MacroStableMode.Abyss, busy: true);
Check(log.Count(x => x.Code == "OWNER_CONFLICT_OBSERVED") == 1,
    "one collision diagnostic without owner promotion");

// Activity is not proof of success, and errors never clear a stale warning.
clock = clock.AddSeconds(11);
Check(run.Tick() && run.State == MacroStableState.Quiet, "quiet watchdog warns");
Check(!run.Tick(), "quiet watchdog does not flood repeated warnings");
run.ObserveActivity("[제작] 오류: 알 수 없는 상태");
Check(run.State == MacroStableState.Quiet, "error is not recovery evidence");
run.ObserveActivity("[제작] 화면 처리 중");
Check(run.State == MacroStableState.Running, "local activity clears transient warning");
Check(log.Any(x => x.Code == "ACTIVITY_RESUMED_NO_INPUT" &&
                   x.RecoveryObservations == 1), "local-only bounded recovery recorded");

clock = clock.AddSeconds(11);
Check(run.Tick(), "second quiet interval identified");
run.ObserveActivity("[제작] 화면 처리 중");
Check(run.State == MacroStableState.Running, "second local recovery");
clock = clock.AddSeconds(11);
Check(run.Tick(), "third quiet interval identified");
run.ObserveActivity("[제작] 화면 처리 중");
Check(run.State == MacroStableState.ReviewRequired,
    "third quiet recovery requires review rather than unbounded retry");
Check(!run.Tick(), "review-required cannot silently return to running");
run.RequestStop();
run.ObserveActivity("[제작] 화면 처리 중");
Check(run.State == MacroStableState.StopRequested,
    "F10 stop cannot be undone by later log messages");
Check(!run.TryBegin(MacroStableMode.Abyss), "stop-in-progress rejects new owner");
run.ObserveRuntime(MacroStableMode.None, busy: false);
Check(run.Owner == MacroStableMode.Crafting, "one idle poll cannot release lease");
run.ObserveRuntime(MacroStableMode.None, busy: false);
Check(run.State == MacroStableState.Idle && run.Owner == MacroStableMode.None,
    "two idle polls release ownership");
Check(run.TryBegin(MacroStableMode.Abyss), "new owner allowed after actual exit");

// Dedicated multi-altering path may begin without passing through StartSelected.
// Scheduler is the parent owner for crafting -> altering -> Abyss idle excursion.
var direct = New();
direct.ObserveRuntime(MacroStableMode.MultiAltering, busy: true);
Check(direct.Owner == MacroStableMode.MultiAltering &&
      direct.State == MacroStableState.Running, "dedicated multi-altering adopted");
direct.RequestStop();
direct.ObserveRuntime(MacroStableMode.None, busy: false);
direct.ObserveRuntime(MacroStableMode.None, busy: false);
var promoted = New();
Check(promoted.TryBegin(MacroStableMode.Altering), "generic altering starts");
promoted.ObserveRuntime(MacroStableMode.MultiAltering, busy: true);
Check(promoted.Owner == MacroStableMode.MultiAltering,
    "multi-altering owner promotes only from generic altering");
var scheduler = New();
scheduler.ObserveRuntime(MacroStableMode.Scheduler, busy: true);
scheduler.ObserveRuntime(MacroStableMode.Crafting, busy: true);
scheduler.ObserveRuntime(MacroStableMode.MultiAltering, busy: true);
scheduler.ObserveRuntime(MacroStableMode.Abyss, busy: true);
Check(scheduler.Owner == MacroStableMode.Scheduler &&
      scheduler.State == MacroStableState.Running,
    "scheduler retains ownership of approved nested stages");
Check(MacroStabilitySupervisor.FromUi("기믹") == MacroStableMode.None,
    "gimmick is deliberately not a runtime mode");
Check(MacroStabilitySupervisor.FromUi("제작") == MacroStableMode.Crafting &&
      MacroStabilitySupervisor.FromUi("어비스") == MacroStableMode.Abyss &&
      MacroStabilitySupervisor.FromUi("스케줄러") == MacroStableMode.Scheduler,
    "existing UI modes mapped without new actions");

// Cancelled or invalid preflight is cleared only before work was observed.
var setup = New();
Check(setup.TryBegin(MacroStableMode.Crafting), "begin pending setup");
setup.AbortUnstarted();
Check(setup.State == MacroStableState.Idle, "preflight failure drops pending start");
Check(setup.TryBegin(MacroStableMode.Crafting), "new setup allowed");
setup.ObserveRuntime(MacroStableMode.Crafting, busy: true);
setup.AbortUnstarted();
Check(setup.Owner == MacroStableMode.Crafting,
    "abort cannot drop an already observed worker");

// Deterministic virtual-clock stress, no native calls or real-game state.
foreach (var mode in new[] {
    MacroStableMode.Crafting,
    MacroStableMode.MultiAltering,
    MacroStableMode.Abyss })
{
    for (int i = 0; i < 100; i++)
    {
        var sim = New(TimeSpan.FromSeconds(5));
        Check(sim.TryBegin(mode), "stress begin");
        sim.ObserveRuntime(mode, busy: true);
        clock = clock.AddSeconds(6);
        Check(sim.Tick(), "stress quiet detection");
        var status = mode switch {
            MacroStableMode.Crafting => "[제작] 읽기 완료",
            MacroStableMode.MultiAltering => "[다중가공] 새 조회",
            _ => "[어비스] 화면 확인"
        };
        sim.ObserveActivity(status);
        Check(sim.State == MacroStableState.Running, "stress read-only recovery");
        sim.RequestStop();
        sim.ObserveActivity(status);
        Check(sim.State == MacroStableState.StopRequested, "stress stop is terminal");
        sim.ObserveRuntime(MacroStableMode.None, busy: false);
        sim.ObserveRuntime(MacroStableMode.None, busy: false);
        Check(sim.State == MacroStableState.Idle, "stress cleanup");
    }
}
Console.WriteLine("STABILITY_PHASE1_OK (" + checks +
    " assertions; 300 deterministic runs; actual game input: 0)");
