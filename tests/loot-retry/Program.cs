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
        var icon2=icon with {Score=.92,Bounds=new Rectangle(102,301,45,45)};
        var icon3=icon with {Score=.91,Bounds=new Rectangle(101,299,45,45)};
        Check(StrictLootPolicy.IsStable(icon,icon2,icon3,size,size,size),"strict loot accepts stable 3-frame icon");
        Check(!StrictLootPolicy.IsStable(icon,icon2,icon3 with {Score=.83},size,size,size),"strict loot rejects weak frame");
        Check(!StrictLootPolicy.IsStable(icon,icon2,icon3 with {Bounds=new Rectangle(180,430,45,45)},size,size,size),"strict loot rejects moving icon");
        Check(StrictLootPolicy.ExactNameMatches("포식의 마력석","포식의 마력석 ×1"),"strict loot exact name allows quantity one");
        Check(!StrictLootPolicy.ExactNameMatches("포식의 마력석","허상의 마력석"),"strict loot rejects wrong item name");
        Check(!StrictLootPolicy.ExactNameMatches("모르 코르셰어 코트","모르 코르셰어 글러브"),"strict loot rejects similar equipment name");
        Check(StrictLootPolicy.HasExactAdjacentName("허상의 마력석",icon,new[]{name},size),"strict loot exact adjacent name accepted");
        Check(!StrictLootPolicy.HasExactAdjacentName("포식의 마력석",icon,new[]{name},size),"strict loot wrong adjacent name rejected");
        var farName=name with {Bounds=new Rectangle(500,700,170,25)};
        Check(!StrictLootPolicy.HasExactAdjacentName("허상의 마력석",icon,new[]{farName},size),"strict loot unrelated-row name rejected");
        var frameA=new Dictionary<string,DetectionResult>{{"winner",icon with {Score=.92}},{"other",icon with {Score=.80}}};
        var frameB=new Dictionary<string,DetectionResult>{{"winner",icon with {Score=.91}},{"other",icon with {Score=.82}}};
        var frameC=new Dictionary<string,DetectionResult>{{"winner",icon with {Score=.90}},{"other",icon with {Score=.84}}};
        Check(StrictLootPolicy.IsUnambiguousWinner("winner",frameA,frameB,frameC),"strict loot clear winner accepted");
        frameC["other"]=icon with {Score=.88};
        Check(!StrictLootPolicy.IsUnambiguousWinner("winner",frameA,frameB,frameC),"strict loot ambiguous similar icon rejected");
        var clock=Stopwatch.StartNew();await RetryClickDelay.WaitAsync(default);
        Check(clock.ElapsedMilliseconds>=995,"retry cannot proceed immediately (one second wait)");
        using var cts=new CancellationTokenSource(40);bool reachedClick=false,cancelled=false;
        try{await RetryClickDelay.WaitAsync(cts.Token);reachedClick=true;}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&!reachedClick,"stop during retry delay prevents continuation");
        Console.WriteLine($"PASS {passed} loot/retry production-policy regressions");
    }
}
