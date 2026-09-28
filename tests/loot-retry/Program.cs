using System.Diagnostics;
using System.Drawing;
namespace DungeonVisionBot;
internal readonly record struct DetectionResult(bool Found, Rectangle Bounds, double Score, string? ReadText);
internal static class Program
{
    static int passed;
    static void Check(bool value,string label)
    { if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);passed++; }
    static async Task Main()
    {
        var icon=new DetectionResult(true,new Rectangle(100,300,45,45),.93,null);
        var name=new DetectionResult(true,new Rectangle(160,310,170,25),1,"허상의 마력석");
        var size=new Size(800,1000);
        bool Confirm(DetectionResult a,DetectionResult b,DetectionResult[] x,DetectionResult[] y)
            =>HallucinationLootPolicy.Confirm(a,b,x,y,size,size);
        Check(Confirm(icon,icon,new[]{name},new[]{name}),"stable icon + exact adjacent name accepted");
        Check(!Confirm(icon,icon,Array.Empty<DetectionResult>(),Array.Empty<DetectionResult>()),"icon alone rejected");
        Check(!Confirm(icon with {Found=false},icon with {Found=false},new[]{name},new[]{name}),"OCR-only cannot count protected item");
        Check(!Confirm(icon,icon,new[]{name},Array.Empty<DetectionResult>()),"single-frame name rejected");
        Check(!Confirm(icon,icon with {Score=.84},new[]{name},new[]{name}),"weak second icon rejected");
        Check(!Confirm(icon with {Score=double.NaN},icon,new[]{name},new[]{name}),"nonfinite score rejected");
        Check(!Confirm(icon,icon with {Bounds=new(500,600,45,45)},new[]{name},new[]{name}),"moving icon rejected");
        var wrong=name with {ReadText="포식의 마력석"};
        Check(!Confirm(icon,icon,new[]{wrong},new[]{wrong}),"similar stone name rejected");
        wrong=name with {ReadText="허상의 마력석 미획득"};
        Check(!Confirm(icon,icon,new[]{wrong},new[]{wrong}),"substring/sentence rejected");
        wrong=name with {Bounds=new(160,650,170,25)};
        Check(!Confirm(icon,icon,new[]{wrong},new[]{wrong}),"name on unrelated row rejected");
        Check(HallucinationLootPolicy.IsExactName("허상의마력석 ×1"),"whitespace and quantity allowed");
        Check(!HallucinationLootPolicy.IsExactName("허상 마력석"),"fuzzy name rejected");
        Check(!HallucinationLootPolicy.Confirm(icon,icon,new[]{name},new[]{name},size,new(800,900)),"changed frame dimensions rejected");
        Check(!HallucinationLootPolicy.IsExactName("허상의 마력석 0"),"zero quantity rejected");
        var clock=Stopwatch.StartNew();await RetryClickDelay.WaitAsync(default);
        Check(clock.ElapsedMilliseconds>=995,"retry cannot proceed immediately (one second wait)");
        using var cts=new CancellationTokenSource(40);bool reachedClick=false,cancelled=false;
        try{await RetryClickDelay.WaitAsync(cts.Token);reachedClick=true;}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&!reachedClick,"stop during retry delay prevents continuation");
        Console.WriteLine($"PASS {passed} loot/retry production-policy regressions");
    }
}
