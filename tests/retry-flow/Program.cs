using System.Drawing;
using System.Diagnostics;
namespace FishingAutomation
{
 internal static class LootStats
 {
  public const string HallucinationStone="h",DevouringStone="d",AbyssStone="a",RuneEngraving10="r",RuneEngraving10Plus="r+",RuneBinding10="b",RuneBinding10Plus="b+",MorCorsairCoat="c",MorCorsairGloves="g",MorCorsairBoots="boot",MorCorsairTricorne="hat";
  public static string GetDisplayName(string s)=>s;
  public static void RecordRound(IEnumerable<string> s){}
 }
}
namespace DungeonVisionBot
{
 internal record struct DetectionResult(bool Found,Rectangle Bounds,double Score,string? ReadText)
 {public Point Center=>new(Bounds.X+Bounds.Width/2,Bounds.Y+Bounds.Height/2);public static DetectionResult NotFound=>new(false,Rectangle.Empty,0,null);}
 class OcrRecognizer
 {
  public static IReadOnlyList<DetectionResult> Lines=Array.Empty<DetectionResult>();
  public Task<DetectionResult> FindCompactLabelAsync(Bitmap b,Rectangle r,string t,CancellationToken ct)=>Task.FromResult(DetectionResult.NotFound);
  public Task<IReadOnlyList<DetectionResult>> ReadLinesAsync(Bitmap b,Rectangle r,int scale,CancellationToken ct)=>Task.FromResult(Lines);
 }
 class Detector {public bool Hallucination;public Task<DetectionResult> DetectAsync(string id,Bitmap b,CancellationToken ct)=>Task.FromResult(Hallucination && id=="abyss_loot_hallucination_stone" ? new DetectionResult(true,new(100,300,40,40),.95,null) : DetectionResult.NotFound);}
 class Settings {public int PollIntervalMs=1,ClickSettleMs=1;}
 class Step {public string Target="abyss_touch_screen";}
 class Definition {public List<Step> Steps=new(){new()};}
 class Input {public int Clicks;public long At;public void ClickClientPoint(nint h,Point p){Clicks++;At=Stopwatch.GetTimestamp();}}
 static class NativeMethods {public static void SetForegroundWindow(nint h){}}
 class RestartCycleException:Exception{}
 internal sealed partial class ScenarioEngine
 {
  readonly string _baseDir="abyss";readonly Settings _settings=new();readonly Definition _scenario=new();readonly Detector _detector=new();readonly Input _input=new();nint _hwnd;
  enum AbyssFlowState {ResultConfirmed,RetryClicked,Reentering,CombatClearWait}
  void AbyssTransitionTo(AbyssFlowState s,string r){}void AbyssRequireState(AbyssFlowState s,string r){}
  void DiagnosticObserveDetection(string s,DetectionResult r){}void DiagnosticObserveClick(string s,Point p){}void DiagnosticPersistFailure(string s){}string DiagnosticSummary()=>"test";string AbyssFlowStateText()=>"test";
  public event Action<string>? Log;
  int captures;bool disappear;long detectedAt;
  Task<nint> ResolveRequiredGameWindowAsync(CancellationToken ct)=>Task.FromResult((nint)1);
  Task<bool> DetectAbyssOutsideWorkflowAsync(Bitmap b,CancellationToken ct)=>throw new Exception("retry transition must not query outside HUD");
  Task<bool> HasAbyssCombatEvidenceAsync(Bitmap b,CancellationToken ct)=>Task.FromResult(true);
  Task<bool> ConfirmHallucinationLootAsync(Bitmap a,Bitmap b,Dictionary<string,DetectionResult> x,Dictionary<string,DetectionResult> y,CancellationToken ct)=>Task.FromResult(false);
  Task<Bitmap> CaptureGameWindowAsync(CancellationToken ct)
  {
   ct.ThrowIfCancellationRequested();captures++;
   if(captures==2)detectedAt=Stopwatch.GetTimestamp();
   var b=new Bitmap(800,1000);using var g=Graphics.FromImage(b);g.Clear(Color.Black);
   if(_input.Clicks==0&&(!disappear||captures<=2))g.FillRectangle(Brushes.Lime,new Rectangle(160,905,480,85));
   return Task.FromResult(b);
  }
  static void Check(bool v,string s){if(!v)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static async Task Main()
  {
   Check(!AbyssOutsidePolicy.CanAccept(false,4,false,false),"outside HUD cannot count without explicit exit");
   Check(AbyssOutsidePolicy.CanAccept(true,3,false,false),"outside HUD accepted only after exit is armed");
   Check(!AbyssOutsidePolicy.CanAccept(true,4,true,false),"clear screen blocks outside acceptance");
   Check(!AbyssOutsidePolicy.CanAccept(true,4,false,true),"touch prompt blocks outside acceptance");
   var e=new ScenarioEngine();e._detector.Hallucination=true;
   using(var frame=new Bitmap(800,1000))Check((await e.DetectAbyssLootAsync(frame,default)).Count==0,"production template winner cannot bypass protected-item gate");
   e=new();OcrRecognizer.Lines=new[]{new DetectionResult(true,new(150,300,150,20),1,"허상의 마력석")};
   using(var frame=new Bitmap(800,1000))Check((await e.DetectAbyssLootAsync(frame,default)).Count==0,"production OCR fallback cannot bypass protected-item gate");
   OcrRecognizer.Lines=Array.Empty<DetectionResult>();
   e=new ScenarioEngine(){_abyssLootCountedForCurrentResult=true};
   using(var ct=new CancellationTokenSource(8000))await e.RetryAbyssResultAsync(ct.Token);
   Check(e._input.Clicks==1&&Stopwatch.GetElapsedTime(e.detectedAt,e._input.At).TotalMilliseconds>=995,"production retry waits one second and clicks once");
   e=new(){_abyssLootCountedForCurrentResult=true,disappear=true};
   using(var ct=new CancellationTokenSource(1800)){try{await e.RetryAbyssResultAsync(ct.Token);}catch(OperationCanceledException){}}
   Check(e._input.Clicks==0,"production retry does not click vanished button");
   e=new(){_abyssLootCountedForCurrentResult=true};
   using(var ct=new CancellationTokenSource(600)){try{await e.RetryAbyssResultAsync(ct.Token);}catch(OperationCanceledException){}}
   Check(e._input.Clicks==0,"production retry cancellation during one-second wait sends no click");
  }
 }
}
