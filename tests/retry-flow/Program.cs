using System.Drawing;
using System.Diagnostics;
namespace FishingAutomation
{
 internal static class LootStats
 {
  public const string HallucinationStone="h",DevouringStone="d",AbyssStone="a",RuneEngraving10="r",RuneEngraving10Plus="r+",RuneBinding10="b",RuneBinding10Plus="b+",MorCorsairCoat="c",MorCorsairGloves="g",MorCorsairBoots="boot",MorCorsairTricorne="hat";
  public static string GetDisplayName(string s)=>s;
  public static void RecordRound(IEnumerable<string> s){}
  public static Dictionary<string,int> Recorded = new();
  public static void RecordAmounts(IReadOnlyDictionary<string,int> amounts)
  { foreach(var x in amounts) Recorded[x.Key]=Recorded.GetValueOrDefault(x.Key)+x.Value; }
 }
}
namespace DungeonVisionBot
{
 internal record struct DetectionResult(bool Found,Rectangle Bounds,double Score,string? ReadText)
 {public Point Center=>new(Bounds.X+Bounds.Width/2,Bounds.Y+Bounds.Height/2);public static DetectionResult NotFound=>new(false,Rectangle.Empty,0,null);}
 class OcrRecognizer
 {
  public static bool Recruiting;
  public static bool ClearTitle, ClearTouch, OutsideClear, ForceRetryOcrMiss;
  public static IReadOnlyList<DetectionResult> Lines=Array.Empty<DetectionResult>();
  public Task<DetectionResult> FindCompactLabelAsync(Bitmap b,Rectangle r,string t,CancellationToken ct)
  {
   // Isolated harness: a real solid green middle result button exposes the
   // exact '다시 하기' text; recruitment OCR exists only on recruitment frames.
   if ((t=="던전 클리어" && ClearTitle) || (t=="화면을 터치해 주세요" && ClearTouch))
    return Task.FromResult(new DetectionResult(true,OutsideClear ? new Rectangle(0,0,20,20) : r,1,t));
   bool retry = t=="다시 하기" && !ForceRetryOcrMiss && b.GetPixel(Math.Min(400,b.Width-1),Math.Min(930,b.Height-1)).G>=200;
   bool recruiting = Recruiting && t=="우연한 만남";
   return Task.FromResult(retry || recruiting ? new DetectionResult(true,r,1,t) : DetectionResult.NotFound);
  }
  public Task<IReadOnlyList<DetectionResult>> ReadLinesAsync(Bitmap b,Rectangle r,int scale,CancellationToken ct)=>Task.FromResult<IReadOnlyList<DetectionResult>>(Recruiting ? new[]{new DetectionResult(true,r,1,"모든 플레이어가 모이면 자동 입장합니다.")} : Lines);
 }
 class Detector {public bool Hallucination; public Dictionary<string,DetectionResult> Hits=new(); public Task<DetectionResult> DetectAsync(string id,Bitmap b,CancellationToken ct)=>Task.FromResult(Hits.TryGetValue(id,out var hit) ? hit : Hallucination && id=="abyss_loot_hallucination_stone" ? new DetectionResult(true,new(100,300,40,40),.95,null) : DetectionResult.NotFound);}
 class Settings {public int PollIntervalMs=1,ClickSettleMs=1;}
 class Step {public string Target="abyss_touch_screen";}
 class Definition {public List<Step> Steps=new(){new()};}
 class Input {public int Clicks;public long At;public void ClickClientPoint(nint h,Point p){Clicks++;At=Stopwatch.GetTimestamp();}}
 static class NativeMethods {public static void SetForegroundWindow(nint h){}}
 static class WindowTools { public static Rectangle GetClientScreenRect(nint hwnd)=>new(0,0,800,1000); }
 class RestartCycleException:Exception{}
 internal sealed partial class ScenarioEngine
 {
  readonly string _baseDir="abyss";readonly Settings _settings=new();readonly Definition _scenario=new();readonly Detector _detector=new();readonly Input _input=new();nint _hwnd;
  enum AbyssFlowState {TouchClicked,ResultConfirmed,ClearConfirmed,RetryClicked,Reentering,Recruiting,CombatClearWait}
  AbyssFlowState flow;
  long? _abyssCombatStartedAt;
  bool mockActiveCombat;
  private static readonly Rectangle AbyssFinishedTimeRoi = new(320,222,270,80);
  private static readonly Rectangle AbyssFinishedLootRoi = new(295,450,265,100);
  private sealed class AbyssResumeVerifiedCombatException:Exception{}
  Task<bool> HasAbyssActiveCombatHudAsync(Bitmap frame,CancellationToken ct)=>Task.FromResult(mockActiveCombat);
  void AbyssRestoreVerifiedCombatFromPrematureResult(string reason)
  {
   if(flow!=AbyssFlowState.ResultConfirmed||!_abyssCombatStartedAt.HasValue)
    throw new InvalidOperationException("mock combat reversal requires result and timer");
   flow=AbyssFlowState.CombatClearWait;
  }
  void AbyssTransitionTo(AbyssFlowState s,string r){flow=s;}void AbyssRequireState(AbyssFlowState s,string r){}
  // The production V3.3.19 rescue uses methods supplied by other partial
  // files. This isolated retry harness needs equivalent stubs to compile.
  AbyssFlowState GetAbyssFlowState()=>flow;
  void AbyssRestoreVerifiedClearTouchFromPrematureResult(string reason)
  {
   if(flow!=AbyssFlowState.ResultConfirmed)throw new InvalidOperationException("unexpected mock flow");
   flow=AbyssFlowState.ClearConfirmed;
  }
  Task AdvanceAbyssClearScreenAsync(DetectionResult touch,CancellationToken ct)
  {
   if(flow!=AbyssFlowState.ClearConfirmed)throw new InvalidOperationException("mock touch flow");
   flow=AbyssFlowState.ResultConfirmed;
   return Task.CompletedTask;
  }
  void DiagnosticObserveDetection(string s,DetectionResult r){}void DiagnosticObserveClick(string s,Point p){}void DiagnosticPersistFailure(string s){}string DiagnosticSummary()=>"test";string AbyssFlowStateText()=>"test";
  // The production competition modal handler lives in a different partial
  // file. This isolated retry harness only models its absent-screen result.
  bool _retryCompetitionConfirmationClicked;
  Task<bool> TryAcknowledgeRetryCompetitionMissionCompleteAsync(
      Bitmap frame, CancellationToken ct) => Task.FromResult(false);
  public event Action<string>? Log;
  int captures;bool disappear;long detectedAt;
  Stopwatch? recruitmentClock;
  bool loading;
  Task<nint> ResolveRequiredGameWindowAsync(CancellationToken ct)=>Task.FromResult((nint)1);
  Task<bool> DetectAbyssOutsideWorkflowAsync(Bitmap b,CancellationToken ct)=>throw new Exception("retry transition must not query outside HUD");
  Task<bool> HasAbyssCombatEvidenceAsync(Bitmap b,CancellationToken ct)=>Task.FromResult(!loading || recruitmentClock!.Elapsed >= TimeSpan.FromSeconds(31));
  Task<bool> ConfirmHallucinationLootAsync(Bitmap a,Bitmap b,Dictionary<string,DetectionResult> x,Dictionary<string,DetectionResult> y,CancellationToken ct)=>Task.FromResult(false);
  Task<Bitmap> CaptureGameWindowAsync(CancellationToken ct)
  {
   ct.ThrowIfCancellationRequested();captures++;
   if(recruitmentClock != null) OcrRecognizer.Recruiting = !loading && recruitmentClock.Elapsed < TimeSpan.FromSeconds(31);
   if(captures==2)detectedAt=Stopwatch.GetTimestamp();
   var b=new Bitmap(800,1000);using var g=Graphics.FromImage(b);g.Clear(Color.Black);
   if(recruitmentClock==null&&_input.Clicks==0&&(!disappear||captures<=2))
   {
    g.FillRectangle(Brushes.Goldenrod,350,105,100,100);
    g.FillRectangle(Brushes.Lime,180,922,140,56);
    g.FillRectangle(Brushes.Lime,330,922,140,56);
    g.FillRectangle(Brushes.Lime,480,922,140,56);
   }
   return Task.FromResult(b);
  }
  static void Check(bool v,string s){if(!v)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static async Task Main(string[] args)
  {
   using(var frame=new Bitmap(800,1000))
   {
    var reader=new ScenarioEngine();
    OcrRecognizer.ClearTouch=true;
    var pair=await reader.ReadAbyssClearEvidenceAsync(frame,default);
    Check(pair.Touch.Found && !pair.Title.Found,"touch prompt accepted without animated title");
    Check(!AbyssClearEvidencePolicy.Confirmed(AbyssClearEvidencePolicy.Next(0,false,true)),"one prompt frame never clicks");
    Check(AbyssClearEvidencePolicy.Confirmed(AbyssClearEvidencePolicy.Next(1,false,true)),"two prompt frames without title confirm clear");
    Check(AbyssClearEvidencePolicy.Next(1,true,false)==0,"missing prompt resets counter");
    OcrRecognizer.OutsideClear=true;
    pair=await reader.ReadAbyssClearEvidenceAsync(frame,default);
    Check(!pair.Touch.Found,"out of bounds prompt rejected");
    OcrRecognizer.OutsideClear=false;OcrRecognizer.ClearTouch=false;
    var strongTitle=new DetectionResult(true,new Rectangle(240,190,210,70),.94,"clear");
    reader._detector.Hits["abyss_dungeon_clear_visual"]=strongTitle;
    pair=await reader.ReadAbyssClearEvidenceAsync(frame,default);
    Check(pair.Title.Found && !pair.Touch.Found,"high-confidence title fallback despite missing OCR");
    Check(!AbyssClearEvidencePolicy.Confirmed(AbyssClearEvidencePolicy.Next(0,false,pair.Title.Found)),"one title frame cannot click");
    Check(AbyssClearEvidencePolicy.SameTitle(strongTitle,new DetectionResult(true,new Rectangle(244,192,210,70),.91,"clear")),"two stationary title frames agree");
    Check(!AbyssClearEvidencePolicy.SameTitle(strongTitle,new DetectionResult(true,new Rectangle(410,400,210,70),.91,"clear")),"moved title is not stable");
    Check(AbyssClearEvidencePolicy.TitleTouchPoint(frame.Size).Found,"bounded portrait safe click");
    reader._detector.Hits["abyss_dungeon_clear_visual"]=new DetectionResult(true,new Rectangle(240,190,210,70),.65,"weak");
    pair=await reader.ReadAbyssClearEvidenceAsync(frame,default);
    Check(!pair.Title.Found,"weak title cannot click");
    reader._detector.Hits.Clear();
    using(var g=Graphics.FromImage(frame))
    {
     g.Clear(Color.Black);g.FillRectangle(Brushes.Goldenrod,350,105,100,100);
     g.FillRectangle(Brushes.Lime,180,922,140,56);
     g.FillRectangle(Brushes.Lime,330,922,140,56);
     g.FillRectangle(Brushes.Lime,480,922,140,56);
    }
    reader.mockActiveCombat=true;OcrRecognizer.ForceRetryOcrMiss=true;
    Check((await reader.DetectAbyssResultRetryAsync(frame,default)).Found,"reward proof independent of false combat flag and missing OCR");
    reader._detector.Hits["abyss_exit_confirm_title"]=new(true,new(100,100,100,100),1,null);
    Check(!(await reader.DetectAbyssResultRetryAsync(frame,default)).Found,"exit modal blocks underlying reward click");
    reader._detector.Hits.Clear();
    using(var g=Graphics.FromImage(frame))g.FillRectangle(Brushes.Black,350,105,100,100);
    Check(!(await reader.DetectAbyssResultRetryAsync(frame,default)).Found,"buttons alone do not establish rewards");
    OcrRecognizer.ForceRetryOcrMiss=false;
   }
   using (var shiftedAbyssResult = new Bitmap(800, 1000))
   {
    using var g = Graphics.FromImage(shiftedAbyssResult);
    g.Clear(Color.Black);
    g.FillRectangle(Brushes.Goldenrod, 350, 105, 100, 100);
    g.FillRectangle(Brushes.Lime, 180, 921, 140, 57);
    g.FillRectangle(Brushes.Lime, 330, 921, 148, 57);
    g.FillRectangle(Brushes.Lime, 487, 921, 133, 57);
    Check(AbyssRewardVisualPolicy.TryDetect(shiftedAbyssResult, out var retry, out _)
     && retry.Contains(400, 950), "Abyss accepts right gap shifted seven reference pixels");
    g.FillRectangle(Brushes.Lime, 478, 921, 9, 57);
    Check(!AbyssRewardVisualPolicy.TryDetect(shiftedAbyssResult, out _, out _),
     "Abyss rejects merged buttons within the gap search range");
   }
   using (var dungeonResult = new Bitmap(800, 1000))
   {
    using var greenBackground = new SolidBrush(Color.FromArgb(42, 76, 57));
    using var greenButton = new SolidBrush(Color.FromArgb(10, 179, 112));
    using var g = Graphics.FromImage(dungeonResult);
    g.Clear(greenBackground.Color);
    g.FillRectangle(Brushes.Goldenrod, 350, 105, 100, 100);
    g.FillRectangle(greenButton, 180, 921, 140, 57);
    g.FillRectangle(greenButton, 330, 921, 140, 57);
    g.FillRectangle(greenButton, 480, 921, 140, 57);
    Check(!AbyssRewardVisualPolicy.TryDetect(dungeonResult, out _, out _),
     "existing Abyss gap rule rejects green background");
    Check(AbyssRewardVisualPolicy.TryDetectDungeon(dungeonResult, out var dungeonRetry, out _)
     && dungeonRetry.Contains(400, 950), "dungeon retry accepts separated buttons on green background");
    g.FillRectangle(greenButton, 320, 921, 10, 57);
    Check(!AbyssRewardVisualPolicy.TryDetectDungeon(dungeonResult, out _, out _),
     "merged dungeon buttons fail contrast");
    g.FillRectangle(greenBackground, 180, 921, 150, 57);
    g.FillRectangle(greenBackground, 470, 921, 150, 57);
    Check(!AbyssRewardVisualPolicy.TryDetectDungeon(dungeonResult, out _, out _),
     "single dungeon button fails reward proof");
   }
   if(args.Contains("--visual-confirmed-only")) return;
   if(args.Contains("--clear-only")) return;
   Check(!AbyssOutsidePolicy.CanAccept(false,4,false,false),"outside HUD cannot count without explicit exit");
   Check(AbyssOutsidePolicy.CanAccept(true,3,false,false),"outside HUD accepted only after exit is armed");
   Check(!AbyssOutsidePolicy.CanAccept(true,4,true,false),"clear screen blocks outside acceptance");
   Check(!AbyssOutsidePolicy.CanAccept(true,4,false,true),"touch prompt blocks outside acceptance");
   var waiting=new ScenarioEngine(){recruitmentClock=Stopwatch.StartNew()};
   using(var ct=new CancellationTokenSource(40000))await waiting.WaitForAbyssRetryTransitionAsync(ct.Token);
   Check(waiting.recruitmentClock.Elapsed.TotalSeconds>=31 && waiting.flow==AbyssFlowState.CombatClearWait && waiting._input.Clicks==0,"recruitment beyond 30 seconds waits without clicks, then accepts combat");
   OcrRecognizer.Recruiting=false;
   var loadingWait=new ScenarioEngine(){recruitmentClock=Stopwatch.StartNew(),loading=true};
   using(var ct=new CancellationTokenSource(40000))await loadingWait.WaitForAbyssRetryTransitionAsync(ct.Token);
   Check(loadingWait.recruitmentClock.Elapsed.TotalSeconds>=31 && loadingWait.flow==AbyssFlowState.CombatClearWait && loadingWait._input.Clicks==0,"loading without recruitment OCR beyond 30 seconds waits then accepts combat without clicks");
   var cancelled=new ScenarioEngine(){recruitmentClock=Stopwatch.StartNew(),loading=true};
   bool stopped=false;
   using(var ct=new CancellationTokenSource(700)){try{await cancelled.WaitForAbyssRetryTransitionAsync(ct.Token);}catch(OperationCanceledException){stopped=true;}}
   Check(stopped && cancelled._input.Clicks==0 && cancelled.flow!=AbyssFlowState.CombatClearWait,"unknown loading remains cancellable and cannot imply combat");
   var e=new ScenarioEngine();e._detector.Hallucination=true;
   using(var frame=new Bitmap(800,1000))Check((await e.DetectAbyssLootAsync(frame,default)).Count==0,"production template winner cannot bypass protected-item gate");
   e=new();OcrRecognizer.Lines=new[]{new DetectionResult(true,new(150,300,150,20),1,"허상의 마력석")};
   using(var frame=new Bitmap(800,1000))Check((await e.DetectAbyssLootAsync(frame,default)).Count==0,"production OCR fallback cannot bypass protected-item gate");
   OcrRecognizer.Lines=Array.Empty<DetectionResult>();
   e=new ScenarioEngine(){_abyssLootCountedForCurrentResult=true};
   using(var ct=new CancellationTokenSource(15000))await e.RetryAbyssResultAsync(ct.Token);
   Check(e._input.Clicks==1&&Stopwatch.GetElapsedTime(e.detectedAt,e._input.At).TotalMilliseconds>=4995,"production retry waits five seconds and clicks once");
   e=new(){_abyssLootCountedForCurrentResult=true,disappear=true};
   using(var ct=new CancellationTokenSource(1800)){try{await e.RetryAbyssResultAsync(ct.Token);}catch(OperationCanceledException){}}
   Check(e._input.Clicks==0,"production retry does not click vanished button");
   e=new(){_abyssLootCountedForCurrentResult=true};
   using(var ct=new CancellationTokenSource(600)){try{await e.RetryAbyssResultAsync(ct.Token);}catch(OperationCanceledException){}}
   Check(e._input.Clicks==0,"production retry cancellation during five-second wait sends no click");
   var responses=new Queue<string>(new[]{"[]", "[{\"DisplayName\":\"허상의 마력석\",\"Count\":3,\"Location\":\"inventory\"}]", "[{\"DisplayName\":\"허상의 마력석\",\"Count\":3,\"Location\":\"inventory\"}]", "[{\"DisplayName\":\"허상의 마력석\",\"Count\":3,\"Location\":\"inventory\"}]"});
   e=new();e._inventoryLoot=new FishingAutomation.InventoryLootTracker(_=>Task.FromResult(FishingAutomation.MabinogiMobileCli.Parse("get_items",new(0,responses.Dequeue(),""))),new Dictionary<string,string>{{"h","허상의 마력석"}});
   await e.BeginInventoryLootRoundAsync(default);
   using(var ct=new CancellationTokenSource(15000))await e.RetryAbyssResultAsync(ct.Token);
   Check(FishingAutomation.LootStats.Recorded.GetValueOrDefault("h")==3,"production retry records actual CLI quantity before clicking");
   Check(e._input.Clicks==1&&!e._inventoryNextRoundClicked&&!e._abyssLootCountedForCurrentResult,"production retry commits next baseline only after combat transition");

  }
 }
}
