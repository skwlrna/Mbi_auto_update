using DungeonVisionBot;
using System.Drawing.Imaging;

try
{
if (args.Length != 5) { Console.WriteLine("Usage: image clientX clientY text roiKind"); return; }
using var original = new Bitmap(args[0]);
using var frame = args[4].StartsWith("gather-") ? new Bitmap(800,1000,PixelFormat.Format24bppRgb) : original.Clone(new Rectangle(int.Parse(args[1]), int.Parse(args[2]), 800, 1000), PixelFormat.Format24bppRgb);
if(args[4].StartsWith("gather-"))
{
    using(var g=Graphics.FromImage(frame)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(original,new Rectangle(0,0,800,1000));}
    var vision = new GatheringVision();
    if(args[4]=="gather-first")
    {
        var place=await vision.FirstPlaceAsync(frame,default);
        if(place is null) throw new Exception("First place row not recognized");
        Console.WriteLine("PASS first place: "+place.Value.Text+" "+place.Value.Bounds);
        var title=await vision.FindMaterialAsync(frame,args[3],default);
        if(title is null) throw new Exception("Selected resource title not recognized");
        Console.WriteLine("PASS resource: "+title.Value.ReadText);
    }
    else if(args[4]=="gather-negative")
    {
        if(GatheringVision.HasStopButton(frame)) throw new Exception("Non-gathering screen accepted as stop control");
        if(await vision.FirstPlaceAsync(frame,default) is not null) throw new Exception("Obtain link accepted as place list heading");
        Console.WriteLine("PASS non-list/non-stop screen rejected");
    }
    else if(args[4]=="gather-stop")
    {
        if(!GatheringVision.HasStopButton(frame)) throw new Exception("Guarded green stop control not recognized");
        Console.WriteLine("PASS green stop control");
    }
    else
    {
        var result=await vision.FindExactAsync(frame,args[4]=="gather-stop"?GatheringVision.StopKey:GatheringVision.MethodLink,args[4]=="gather-stop"?"Space":"구하는 방법",default);
        if(result is null) { foreach(var line in await new OcrRecognizer().ReadLinesAsync(frame,GatheringVision.StopKey,3,default)) Console.WriteLine("STOP RAW "+line.ReadText+" "+line.Bounds); throw new Exception("Expected gathering control not recognized"); }
        Console.WriteLine("PASS "+result.Value.ReadText+" "+result.Value.Bounds);
    }
    return;
}
var ocr = new OcrRecognizer();
if (args[4] == "facilities")
{
    foreach (string facility in FishingAutomation.AlteringPlan.Facilities)
    {
        string title = facility.Replace(" 시설", "");
        var area = FishingAutomation.AlteringFacilityLayout.TitleArea(title);
        var labels = await ocr.FindAlteringFacilityTitlesAsync(frame, title, default);
        if (labels.Count != 1 || !area.Contains(labels[0].Bounds))
            throw new Exception($"Facility title is missing or ambiguous: {title} ({labels.Count})");
        Console.WriteLine($"PASS {title}: one title {labels[0].Bounds}");
    }
    return;
}
var roi = args[4] switch
{
    "header" => new Rectangle(0, 15, 450, 110),
    "cards" => new Rectangle(20, 350, 760, 550),
    "popup" => new Rectangle(270, 585, 360, 45),
    "button" => new Rectangle(180, 860, 490, 130),
    "collect" => new Rectangle(0, 260, 170, 110),
    _ => new Rectangle(0, 0, 800, 1000)
};
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
