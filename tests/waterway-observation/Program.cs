using System.Text.Json;
using DungeonVisionBot;

// Synthetic recognition adapter only: these tests do NOT validate real images.
static void Check(bool ok, string label)
{
    if (!ok) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
static async Task ExecutorTests()
{
    var moves = new[] { new WaterwayMarkExecutor.Motion("D", 10, 0), new WaterwayMarkExecutor.Motion("A", -10, 0) };
    async Task<(WaterwayMarkExecutor.Result Result, int Inputs)> Run(
        bool authorized, Func<int, long, WaterwayMarkExecutor.Observation?> observe,
        IReadOnlyList<WaterwayMarkExecutor.Motion>? calibration = null, int clockStep = 90)
    {
        long clock = 10000;
        int inputs = 0, captures = 0;
        var result = await WaterwayMarkExecutor.RunAsync(token =>
        {
            clock += clockStep;
            return Task.FromResult(observe(captures++, clock));
        }, (key, token) => { inputs++; return Task.CompletedTask; },
            calibration ?? moves, authorized, () => clock, CancellationToken.None);
        return (result, inputs);
    }
    static WaterwayMarkExecutor.Observation Frame(long clock, int x = 100, bool range = false,
        bool success = false, bool failure = false, string symbol = "fire") =>
        new(symbol, x, 500, 140, 500, clock, range, success, failure);
    var dry = await Run(false, (_, t) => Frame(t));
    Check(dry.Result.State == "DryRun" && dry.Inputs == 0, "dry run emits no physical input");
    var complete = await Run(true, (n, t) => n < 2 ? Frame(t) :
        n < 4 ? Frame(t, 110) : n < 6 ? Frame(t, 120, true) : Frame(t, 120, true, true));
    Check(complete.Result.State == "SuccessCue" && complete.Inputs == 2,
        "fake transport moves one tap per fresh pair then awaits success cue");
    var stalled = await Run(true, (_, t) => Frame(t));
    Check(stalled.Result.State == "Stopped" && stalled.Inputs == 1, "no progress stops after one tap");
    var mismatch = await Run(true, (n, t) => Frame(t, symbol: n == 0 ? "fire" : "ice"));
    Check(mismatch.Result.State == "Stopped" && mismatch.Inputs == 0, "changed symbol prevents input");
    var missing = await Run(true, (_, _) => null);
    Check(missing.Inputs == 0 && missing.Result.State == "Stopped", "lost feet or target prevents input");
    var stale = await Run(true, (_, t) => Frame(t - 1000));
    Check(stale.Inputs == 0, "stale recognition prevents input");
    var jitter = await Run(true, (n, t) => Frame(t, n == 0 ? 100 : 125));
    Check(jitter.Inputs == 0, "camera jump prevents input");
    var absentCalibration = await Run(true, (_, t) => Frame(t), Array.Empty<WaterwayMarkExecutor.Motion>());
    Check(absentCalibration.Inputs == 0, "no calibration cannot guess WASD direction");
    var badCalibration = await Run(true, (_, t) => Frame(t), new[] { new WaterwayMarkExecutor.Motion("SPACE", 10, 0), new WaterwayMarkExecutor.Motion("D", double.NaN, 0) });
    Check(badCalibration.Inputs == 0, "invalid and nonmovement calibration rejected");
    var failure = await Run(true, (_, t) => Frame(t, failure: true));
    Check(failure.Result.State == "Failed" && failure.Inputs == 0, "failure cue stops input");
    var outcome = await Run(true, (_, t) => new("", -1, -1, -1, -1, t, false, true, false));
    Check(outcome.Result.State == "SuccessCue" && outcome.Inputs == 0, "result cue remains readable after head glyph disappears");
    var singleOutcome = await Run(true, (n, t) => n == 0 ? new("", -1, -1, -1, -1, t, false, true, false) : Frame(t));
    Check(singleOutcome.Result.State == "Stopped" && singleOutcome.Inputs == 0, "one-frame outcome never authorizes movement");
    var inRange = await Run(true, (_, t) => Frame(t, range: true));
    Check(inRange.Inputs == 0 && inRange.Result.State == "Stopped", "range cue alone never claims success and times out");
    var lateCapture = await Run(true, (_, t) => Frame(t), clockStep: 1600);
    Check(lateCapture.Inputs == 0, "deadline crossing during captures prevents input");
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    int cancelledInputs = 0;
    try
    {
        await WaterwayMarkExecutor.RunAsync(_ => Task.FromResult<WaterwayMarkExecutor.Observation?>(Frame(0)),
            (_, _) => { cancelledInputs++; return Task.CompletedTask; }, moves, true, () => 0, cancel.Token);
        throw new Exception("cancel must stop executor");
    }
    catch (OperationCanceledException) { Check(cancelledInputs == 0, "F10 cancellation prevents input"); }
    Console.WriteLine("Waterway executor: 15 PASS (fake input transport only)");
}
string root = Path.Combine(Path.GetTempPath(), "waterway-observation-" + Guid.NewGuid());
Directory.CreateDirectory(Path.Combine(root, "config"));
try
{
    var options = new ScatteredWaterwayMechanics.Options
    {
        Enabled = true, ObserveOnly = false, AllowKeyboardActions = true
    };
    options.Mark.CueTemplate = "cue";
    options.Mark.BossRoi = new() { Width = 800, Height = 200 };
    options.Mark.PlayerRoi = new() { Y = 200, Width = 800, Height = 200 };
    options.Mark.PriestRoi = new() { Y = 400, Width = 800, Height = 600 };
    string[] symbols = { "fire", "ice", "dark", "lightning", "poison", "unknown_sixth" };
    foreach (var symbol in symbols)
    {
        options.Mark.BossMarkers[symbol] = "boss_" + symbol;
        options.Mark.PlayerMarkers[symbol] = "player_" + symbol;
        options.Mark.PriestMarkers[symbol] = "priest_" + symbol;
    }
    foreach (string file in options.Mark.BossMarkers.Values
        .Concat(options.Mark.PlayerMarkers.Values).Concat(options.Mark.PriestMarkers.Values).Append("cue"))
        File.WriteAllText(Path.Combine(root, file), "synthetic placeholder");
    File.WriteAllText(Path.Combine(root, "config", "waterway_mechanics.json"), JsonSerializer.Serialize(options));
    var handler = ScatteredWaterwayMechanics.Load(root, null)!;
    Check(handler.ObserveOnly, "unsafe configuration cannot enable inputs");
    Check(!handler.TryGetValidatedKeys(new("wave", "test", "test", new[] { "W" }, true), out var keys)
        && keys.Length == 0, "even claimed input evidence cannot bypass observation lock");
    using var frame = new Bitmap(800, 1000);
    Check(handler.ReadMarkExecution(frame) is null, "execution experiment disabled by default");
    TemplateMatcher.Hits["cue"] = new(true, new(20, 20), .99);
    for (int i = 0; i < symbols.Length; i++)
        TemplateMatcher.Hits["priest_" + symbols[i]] = new(true, new(100 + i * 100, 500), .99);
    TemplateMatcher.Hits["boss_poison"] = new(true, new(400, 100), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("6/6") == true, "all six priests observed before targeting");
    TemplateMatcher.Hits["player_poison"] = new(true, new(400, 300), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("(500,500)") == true, "boss/player match selects current priest");
    TemplateMatcher.Hits["priest_poison"] = new(true, new(320, 620), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("(320,620)") == true, "camera relocation refreshes candidate");
    options.Mark.ExperimentalExecution = true;
    options.Mark.PlayerFeetTemplate = "player_feet";
    options.Mark.PriestFeetTemplate = "priest_feet";
    options.Mark.PlayerFeetSearch = new() { X = -20, Y = 30, Width = 40, Height = 80 };
    options.Mark.PriestFeetSearch = new() { X = -20, Y = 30, Width = 40, Height = 80 };
    File.WriteAllText(Path.Combine(root, "player_feet"), "synthetic placeholder");
    File.WriteAllText(Path.Combine(root, "priest_feet"), "synthetic placeholder");
    File.WriteAllText(Path.Combine(root, "config", "waterway_mechanics.json"), JsonSerializer.Serialize(options));
    var execution = ScatteredWaterwayMechanics.Load(root, null)!;
    Check(execution.ReadMarkExecution(frame) is null, "glyph coordinates cannot replace missing feet");
    TemplateMatcher.Hits["player_feet"] = new(true, new(400, 350), .99);
    TemplateMatcher.Hits["priest_feet"] = new(true, new(320, 670), .99);
    var feet = execution.ReadMarkExecution(frame);
    Check(feet?.PlayerY == 350 && feet.PriestY == 670, "execution reads separately located feet near selected glyphs");
    TemplateMatcher.Hits["priest_feet"] = new(true, new(500, 550), .99);
    Check(execution.ReadMarkExecution(frame) is null, "other priest feet outside selected glyph region rejected");
    TemplateMatcher.Hits["player_fire"] = new(true, new(400, 300), .99);
    Check(handler.TryPlan(frame)?.Stage == "투창 대기", "ambiguous player glyph blocks target");
    TemplateMatcher.Hits.Remove("player_fire");
    TemplateMatcher.Hits["boss_fire"] = new(true, new(400, 100), .99);
    Check(handler.TryPlan(frame) is null, "ambiguous boss blocks observation");
    TemplateMatcher.Hits.Remove("boss_fire");
    TemplateMatcher.Hits.Remove("boss_poison");
    Check(handler.TryPlan(frame) is null, "ambiguous boss invalidates previous cached symbol");
    TemplateMatcher.Hits["boss_poison"] = new(true, new(400, 100), .99);
    handler.ResetRound();
    foreach (var symbol in symbols) TemplateMatcher.Hits.Remove("priest_" + symbol);
    Check(handler.TryPlan(frame)?.Stage == "투창 대기", "round reset erases old priest positions");
    Console.WriteLine("Waterway observation: 13 PASS (synthetic recognition, no input)");
}
finally
{
    Directory.Delete(root, recursive: true); // only this test's unique temporary directory
}
await ExecutorTests();

namespace DungeonVisionBot
{
    internal sealed class RectDef
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Rectangle ToRectangle() => new(X, Y, Width, Height);
    }
    internal sealed record DetectionResult(bool Found, Point Center, double Score)
    {
        public static DetectionResult NotFound => new(false, default, 0);
    }
    internal static class WindowCapture
    {
        public static Rectangle ClampRoi(Rectangle roi, Size size) => Rectangle.Intersect(new(Point.Empty, size), roi);
    }
    internal sealed class TemplateMatcher
    {
        public TemplateMatcher(string root) { }
        internal static readonly Dictionary<string, DetectionResult> Hits = new();
        public DetectionResult Find(Bitmap frame, Rectangle roi, string path, double threshold) =>
            Hits.TryGetValue(path, out var hit) && roi.Contains(hit.Center) && hit.Score >= threshold
                ? hit : DetectionResult.NotFound;
    }
}
