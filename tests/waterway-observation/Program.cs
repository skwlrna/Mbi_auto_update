using System.Text.Json;
using DungeonVisionBot;

// Synthetic recognition adapter only: these tests do NOT validate real images.
static void Check(bool ok, string label)
{
    if (!ok) throw new Exception(label);
    Console.WriteLine("PASS " + label);
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
    TemplateMatcher.Hits["cue"] = new(true, new(20, 20), .99);
    for (int i = 0; i < symbols.Length; i++)
        TemplateMatcher.Hits["priest_" + symbols[i]] = new(true, new(100 + i * 100, 500), .99);
    TemplateMatcher.Hits["boss_poison"] = new(true, new(400, 100), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("6/6") == true, "all six priests observed before targeting");
    TemplateMatcher.Hits["player_poison"] = new(true, new(400, 300), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("(500,500)") == true, "boss/player match selects current priest");
    TemplateMatcher.Hits["priest_poison"] = new(true, new(320, 620), .99);
    Check(handler.TryPlan(frame)?.Description.Contains("(320,620)") == true, "camera relocation refreshes candidate");
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
    Console.WriteLine("Waterway observation: 9 PASS (synthetic recognition, no input)");
}
finally
{
    Directory.Delete(root, recursive: true); // only this test's unique temporary directory
}

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
