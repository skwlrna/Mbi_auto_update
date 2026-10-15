using FishingAutomation;

static void Check(bool ok, string label)
{
    if (!ok) throw new Exception("PRE-FLIGHT FAIL: " + label);
    Console.WriteLine("PASS PRE-FLIGHT: " + label);
}

static AutomationStartReadiness Test(string mode, bool window = true, bool input = true,
    bool ocr = true, bool templates = true, bool config = true)
    => AutomationStartPreflightPolicy.Evaluate(mode, window, input, ocr, templates, config);

foreach (var mode in new[] { "낚시", "던전", "어비스", "가공", "채집", "제작", "스케줄러" })
{
    Check(Test(mode).CanStart, mode + ": clean environment accepted");
    Check(!Test(mode, window: false).CanStart, mode + ": no game window vetoes F9");
}

Check(!Test("낚시", input: false).CanStart, "fishing input driver unavailable vetoes start");
Check(Test("스케줄러", input: false).CanStart &&
      Test("스케줄러", input: false).Warnings.Length > 0,
    "scheduler retains own input route but exposes missing-input warning");
Check(!Test("어비스", config: false).CanStart &&
      !Test("던전", config: false).CanStart,
    "scenario configuration is mandatory only for dungeon/abyss");
Check(Test("가공", config: false).CanStart,
    "production does not inherit unrelated scenario config requirement");
Check(Test("어비스", ocr: false, templates: false).CanStart &&
      Test("어비스", ocr: false, templates: false).Warnings.Length == 2,
    "OCR and generic templates cannot overrule proven mode-specific visual/CLI route");
Check(Test("낚시", ocr: false).CanStart &&
      Test("낚시", ocr: false).Warnings.Length == 1,
    "OCR fallback warning, not global click permission or false stop");
Console.WriteLine("START_PREFLIGHT_POLICY_OK");
