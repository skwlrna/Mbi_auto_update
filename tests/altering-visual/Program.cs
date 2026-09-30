using DungeonVisionBot;
using System.Drawing.Imaging;

try
{
if (args.Length != 5) { Console.WriteLine("Usage: image clientX clientY text roiKind"); return; }
using var original = new Bitmap(args[0]);
using var frame = original.Clone(new Rectangle(int.Parse(args[1]), int.Parse(args[2]), 800, 1000), PixelFormat.Format24bppRgb);
var roi = args[4] switch
{
    "header" => new Rectangle(0, 15, 450, 110),
    "cards" => new Rectangle(20, 350, 760, 550),
    "popup" => new Rectangle(270, 585, 360, 45),
    "button" => new Rectangle(180, 860, 490, 130),
    "collect" => new Rectangle(0, 260, 170, 110),
    _ => new Rectangle(0, 0, 800, 1000)
};
var ocr = new OcrRecognizer();
var results = await ocr.FindAlteringLabelsAsync(frame, roi, args[3], default, cardCandidate: args[4] == "cards");
if (results.Count == 0 && args[4] == "popup" && FishingAutomation.AlteringText.UniqueOcrAlias(args[3], new[]{args[3]}) is string alias)
    results = await ocr.FindAlteringLabelsAsync(frame, roi, alias, default);
foreach (var r in results) Console.WriteLine($"MATCH {r.ReadText} {r.Bounds}");
if (results.Count == 0) { foreach(var r in await ocr.ReadLinesAsync(frame,roi,3,default)) Console.WriteLine("RAW "+r.ReadText+" "+r.Bounds); throw new Exception("Expected label not recognized"); }
if (args[4] == "button")
{
    var button = results.Single();
    var costRoi = new Rectangle(button.Bounds.Left - 25, button.Bounds.Top - 5, button.Bounds.Width + 25, button.Bounds.Height + 10);
    var costs = await ocr.FindAlteringLabelsAsync(frame, costRoi, "5", default);
    if (costs.Count != 1 || costs[0].Bounds.Right >= button.Bounds.Left) { foreach(var r in await ocr.ReadLinesAsync(frame,costRoi,4,default)) Console.WriteLine("COST "+r.ReadText+" "+r.Bounds); throw new Exception("5-wing cost not recognized"); }
    Console.WriteLine("MATCH cost=5");
}

}
catch(Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
