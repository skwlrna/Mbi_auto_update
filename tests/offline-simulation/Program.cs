using DungeonVisionBot;
using FishingAutomation;

// Purely simulated observations. This process has no screen capture, game CLI,
// networking, keyboard/mouse, or native input references. Every policy is
// compiled directly from production sources rather than reimplemented.
int assertions = 0;

void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException("OFFLINE_SIMULATION_FAIL: " + description);
    assertions++;
    Console.WriteLine("PASS: " + description);
}

AutomationStartReadiness Preflight(
    string mode, bool window = true, bool input = true,
    bool ocr = true, bool templates = true, bool scenario = true)
    => AutomationStartPreflightPolicy.Evaluate(
        mode, window, input, ocr, templates, scenario);

// Scenario A: a missing window blocks every macro mode, even if every other
// simulated signal looks good.
foreach (string mode in new[] { "낚시", "던전", "어비스", "가공", "채집", "제작", "스케줄러" })
{
    Check(Preflight(mode).CanStart, $"{mode}: nominal readiness");
    Check(!Preflight(mode, window: false).CanStart,
        $"{mode}: missing game window vetoes start");
}

// Scenario B: validate mode-specific vetoes without promoting advisory OCR
// warnings to permission for arbitrary input.
Check(!Preflight("낚시", input: false).CanStart,
    "Fishing: missing input device vetoes start");
Check(Preflight("스케줄러", input: false).CanStart &&
      Preflight("스케줄러", input: false).Warnings.Length == 1,
    "Scheduler: missing input driver is warning; per-mode guards remain responsible");
Check(!Preflight("어비스", scenario: false).CanStart &&
      !Preflight("던전", scenario: false).CanStart,
    "Dungeon and Abyss: missing scenario config blocks start");
Check(Preflight("제작", scenario: false).CanStart,
    "Crafting: unrelated dungeon config does not block start");
Check(Preflight("가공", ocr: false, templates: false).CanStart &&
      Preflight("가공", ocr: false, templates: false).Warnings.Length == 2,
    "Production: missing OCR/templates emits two warnings, not false proof of safety");

// Scenario C: scripted mock inventory snapshots. A high/stale quantity alone
// cannot complete auto-gather. At least two fresh stable reads are necessary.
var gatherFrames = new[]
{
    (Stock: 32L, StableReads: 0, Complete: false),
    (Stock: 100L, StableReads: 1, Complete: false),
    (Stock: 100L, StableReads: 2, Complete: true)
};
foreach (var frame in gatherFrames)
{
    bool actual = CraftingGatherCompletionPolicy.HasVerifiedTarget(
        "감자", frame.Stock, 100L, frame.StableReads);
    Check(actual == frame.Complete,
        $"Gather: stock={frame.Stock}, freshReads={frame.StableReads} -> complete={frame.Complete}");
}
Check(!CraftingGatherCompletionPolicy.HasVerifiedTarget(
        "", 100, 100, 10),
    "Gather: unidentified material never counts as verified completion");
Check(!CraftingGatherCompletionPolicy.HasVerifiedTarget(
        "감자", 100, 0, 2),
    "Gather: zero required quantity is invalid completion evidence");
Check(!CraftingGatherCompletionPolicy.MustStopUnnamed("", 7, false) &&
      CraftingGatherCompletionPolicy.MustStopUnnamed("", 8, false),
    "Gather: nameless idle without activity stops after bounded eight polls");
Check(!CraftingGatherCompletionPolicy.MustStopUnnamed(null, 4, true) &&
      CraftingGatherCompletionPolicy.MustStopUnnamed(null, 5, true),
    "Gather: nameless idle after activity stops after bounded five polls");
Check(!CraftingGatherCompletionPolicy.MustStopUnnamed("감자", 100, true),
    "Gather: a known ingredient cannot be stopped by unnamed-item guard");

// Scenario D: a stale detail screen may be reopened only once, at the
// confirmed facility and only without a visible move button.
Check(AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false, facilityVisible: true,
        moveButtonVisible: false, retryAlreadyUsed: false),
    "Altering: one bounded retry from confirmed facility");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false, facilityVisible: true,
        moveButtonVisible: false, retryAlreadyUsed: true),
    "Altering: second retry blocked");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false, facilityVisible: false,
        moveButtonVisible: false, retryAlreadyUsed: false),
    "Altering: no facility evidence blocks retry");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false, facilityVisible: true,
        moveButtonVisible: true, retryAlreadyUsed: false),
    "Altering: a move button blocks retry");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: true, facilityVisible: true,
        moveButtonVisible: false, retryAlreadyUsed: false),
    "Altering: already-visible detail blocks retry");

// Scenario E: false outside-HUD signals must not be interpreted as dungeon exit.
Check(!AbyssOutsidePolicy.CanAccept(false, 4, false, false),
    "Abyss: HUD markers alone cannot confirm an exit");
Check(!AbyssOutsidePolicy.CanAccept(true, 2, false, false),
    "Abyss: too few markers cannot confirm outside");
Check(!AbyssOutsidePolicy.CanAccept(true, 4, true, false),
    "Abyss: clear-screen overlay vetoes outside");
Check(!AbyssOutsidePolicy.CanAccept(true, 4, false, true),
    "Abyss: touch prompt vetoes outside");
Check(AbyssOutsidePolicy.CanAccept(true, 3, false, false),
    "Abyss: explicit exit plus three markers confirms outside");

Console.WriteLine($"OFFLINE_MACRO_SIMULATION_OK ({assertions} checks; real game actions: 0)");
