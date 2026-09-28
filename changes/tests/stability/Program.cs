using System.Drawing;
namespace DungeonVisionBot;
record DetectionResult(bool Found) { public Point Center=>new(1,1); public Rectangle Bounds=>new(0,0,5,5); public static DetectionResult NotFound=>new(false); }
class ScenarioStep { public string TimeoutClickTarget="leave",TimeoutFollowupClickTarget="exit"; public int TimeoutClickTargetWaitSeconds=2,TimeoutFollowupWaitSeconds=2,TimeoutRestartDelaySeconds=0; }
class ScenarioDefinition { public List<ScenarioStep> Steps=new(){new()}; }
class RestartCycleException:Exception {}
class Settings { public int PollIntervalMs=1,ClickSettleMs=1; }
static class NativeMethods { public static void SetForegroundWindow(nint h) {} }
class Detector
{
 public HashSet<string> Hits=new();
 public Task<DetectionResult> DetectAsync(string id,Bitmap frame,CancellationToken ct)
 {ct.ThrowIfCancellationRequested();return Task.FromResult(new DetectionResult(Hits.Contains(id)));}
}
class Input {public int Clicks,Keys;public Action? AfterClick; public void ClickClientPoint(nint h,Point p){Clicks++;AfterClick?.Invoke();} public void TapScanCode(ushort s)=>Keys++; public void ObserveFrame(nint h,Size s){} }
class Capture {public Bitmap CaptureClient(nint h)=>new(1,1);}
internal sealed partial class ScenarioEngine
{
 nint _hwnd; readonly Settings _settings=new();readonly Detector _detector=new();readonly Input _input=new();readonly Capture _capture=new();
 public event Action<string>? Log; bool IsAbyss=>true;int _resumeStepIndex;int _abyssClearTitleFallbackConsecutive;bool AbyssClearTitleFallbackPending=>false;
 bool _abyssLootCountedForCurrentResult;int AbyssCombatStepIndex=>4;int clearCalls;bool outside;bool result; int captures;
 Task<nint> ResolveRequiredGameWindowAsync(CancellationToken ct)=>Task.FromResult((nint)1);
 Task<Bitmap> CaptureGameWindowAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();captures++;return Task.FromResult(new Bitmap(1,1));}
 Task<DetectionResult> DetectAbyssResultRetryAsync(Bitmap b,CancellationToken ct)=>Task.FromResult(new DetectionResult(result));
 Task<DetectionResult> DetectAbyssConfirmedClearAsync(Bitmap b,CancellationToken ct)=>Task.FromResult(DetectionResult.NotFound);
 Task<bool> DetectAbyssOutsideWorkflowAsync(Bitmap b,CancellationToken ct)=>Task.FromResult(outside);
 Task AdvanceAbyssClearScreenAsync(DetectionResult r,CancellationToken ct){clearCalls++;return Task.CompletedTask;}
 Task WaitForAbyssHomeAfterNormalExitAsync(CancellationToken ct){outside=true;return Task.CompletedTask;}
 int FindDungeonStepIndex(string s)=>s=="abyss_enter"?3:s=="abyss_result_retry"?5:0;
 void DiagnosticPersistFailure(string s){}
 static int passed;
 static void Check(bool v,string m){if(!v)throw new Exception(m);Console.WriteLine("PASS "+m);passed++;}
 static ScenarioEngine Expired()=>new(){_abyssCombatStartedAt=Environment.TickCount64-600_001};
 static async Task Main()
 {
  var e=Expired();e._detector.Hits.Add("abyss_touch_screen");await e.ExitAbyssAfterCombatTimeoutAsync(new(),default);
  Check(e.clearCalls==1&&e._input.Clicks==0,"late clear vetoes exit");
  e=Expired();e.result=true;await e.ExitAbyssAfterCombatTimeoutAsync(new(),default);Check(e._input.Clicks==0,"result vetoes exit");
  e=Expired();e._detector.Hits.UnionWith(new[]{"leave","exit"});e._input.AfterClick=()=>e._detector.Hits.Add("abyss_touch_screen");
  await e.ExitAbyssAfterCombatTimeoutAsync(new(),default);Check(e._input.Clicks==1&&e.clearCalls==1,"clear before confirmation cancels exit");
  e=Expired();e._detector.Hits.UnionWith(new[]{"leave","exit"});
  try{await e.ExitAbyssAfterCombatTimeoutAsync(new(),default);throw new Exception("restart missing");}catch(RestartCycleException){}
  Check(e._input.Clicks==2&&e.captures>=4&&e.outside&&e._abyssCombatStartedAt==null,"two-frame exit and outside confirmation");
  e=new(){_abyssCombatStartedAt=Environment.TickCount64};try{await e.ExitAbyssAfterCombatTimeoutAsync(new(),default);throw new Exception("early exit");}catch(InvalidOperationException){}
  Check(e._input.Clicks==0,"early timeout prohibited");
  e=Expired();using(var c=new CancellationTokenSource()){c.Cancel();try{await e.ExitAbyssAfterCombatTimeoutAsync(new(),c.Token);}catch(OperationCanceledException){} }
  Check(e._input.Clicks==0,"cancel prevents input");
  e=Expired();long? clock=e._abyssCombatStartedAt;e._networkResumeStep=4;e.CompleteNetworkRestartRequest("test");e.AbyssEnterCombatClearWait("resume");
  Check(e._abyssCombatStartedAt==clock&&e._resumeStepIndex==4,"same-round reconnect retains monotonic clock");
  e._networkReturnedOutside=true;e._networkResumeStep=0;e.CompleteNetworkRestartRequest("outside");Check(e._abyssCombatStartedAt==null,"confirmed outside clears clock");
  e=Expired();e._abyssFlowState=AbyssFlowState.Reentering;e.AbyssEnterCombatClearWait("new round");Check(e._abyssCombatStartedAt==null,"verified new round resets clock");
  e=new();using var frame=new Bitmap(1,1);Check(await e.ClassifyNetworkReturnAsync(frame,default)==-1,"blank/loading is not reconnect success");
  e._detector.Hits.Add("abyss_leave_dungeon");Check(await e.ClassifyNetworkReturnAsync(frame,default)==4,"combat reconnect routes to combat");
  e.result=true;Check(await e.ClassifyNetworkReturnAsync(frame,default)==5,"result reconnect takes priority");
  e=new();e._detector.Hits.Add("abyss_enter");Check(await e.ClassifyNetworkReturnAsync(frame,default)==3,"entry reconnect routes to entry");
  e=new();using(var c=new CancellationTokenSource(600)){try{await e.StartAbyssCombatClockAsync(c.Token);}catch(OperationCanceledException){}}
  Check(e._abyssCombatStartedAt==null,"loading cannot start combat clock");
  e=new();e._detector.Hits.Add("abyss_leave_dungeon");await e.StartAbyssCombatClockAsync(default);Check(e._abyssCombatStartedAt.HasValue&&e.captures>=3,"positive combat requires three frames");
  e=Expired();e._networkResumeStep=5;e._abyssLootCountedForCurrentResult=true;e.CompleteNetworkRestartRequest("same result");Check(e._abyssLootCountedForCurrentResult,"reconnect preserves loot latch");
  e=new();e.AbyssEnterCombatClearWait("combat");e.AbyssTransitionTo(AbyssFlowState.ResultConfirmed,"two result detections");
  Check(e.GetAbyssFlowState()==AbyssFlowState.ResultConfirmed,"recovery can accept verified result after missed clear screen");
  Console.WriteLine($"PASS {passed} production regressions");
 }
}
