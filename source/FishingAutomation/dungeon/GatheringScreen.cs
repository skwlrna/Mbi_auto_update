using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class GatheringScreen : IGatheringScreen
{
    private readonly nint _hwnd;
    private readonly AppSettings _settings;
    private readonly string _debugDir;
    private readonly MabinogiMobileCli _cli;
    private readonly IGatheringData _data;
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly GatheringVision _vision = new();
    internal string InputMode => _input.ModeName;
    internal event Action<string>? Log;

    internal GatheringScreen(nint hwnd, AppSettings settings, string debugDir, MabinogiMobileCli cli, IGatheringData data)
    {
        _hwnd=hwnd; _settings=settings; _debugDir=debugDir; _cli=cli; _data=data;
        _input=new GuardedInputController(new InterceptionInput(settings.InterceptionMouseDevice,settings.InterceptionKeyboardDevice));
    }
    private Bitmap Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _input.SetCancellation(ct);
        if(!WindowTools.IsRequiredGameWindow(_hwnd)) throw new InvalidOperationException("게임 창이 변경되어 채집 입력을 정지합니다.");
        NativeMethods.SetForegroundWindow(_hwnd);
        var frame=_capture.CaptureClient(_hwnd);
        try { _input.ObserveFrame(_hwnd,frame.Size); return frame; }
        catch { frame.Dispose(); throw; }
    }
    private async Task<bool> AtMaterialAsync(GatheringPlan plan,CancellationToken ct)
    {
        using var frame=Capture(ct);
        return await _vision.FindMaterialAsync(frame,plan.DisplayName,ct) is not null;
    }
    public async Task StartAsync(GatheringPlan plan,CancellationToken ct)
    {
        plan.Validate();
        if(!await AtMaterialAsync(plan,ct) && plan.SourceRecipe is AlteringPlan source)
        {
            var recipes=AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync(ct));
            var selected=recipes.Where(x=>x.DisplayName==source.DisplayName).ToArray();
            if(selected.Length<source.RecipeOrdinal) throw new InvalidOperationException("시작 가공 품목을 목록에서 확인하지 못했습니다.");
            source=source with { ProducedPerWork=selected[source.RecipeOrdinal-1].ProducedPerWork,RecipeCount=selected.Length,
                VerifiedOcrAlias=AlteringText.UniqueOcrAlias(source.DisplayName,recipes.Select(x=>x.DisplayName)),AllowPaidButton=false };
            using(var navigator=new AlteringScreen(_hwnd,_settings,_debugDir)) await navigator.OpenRecipeAsync(source,ct);
            await ClickIngredientAsync(plan,ct);
        }
        if(!await AtMaterialAsync(plan,ct))
        {
            using var frame=Capture(ct);
            Fail(frame,"선택한 채집 재료의 상세 화면을 확인하지 못했습니다. 재료 상세/구하는 방법을 열거나 시작 가공 품목을 설정하세요.");
        }
        using(var frame=Capture(ct))
        {
            if(await _vision.FirstPlaceAsync(frame,ct) is null) await ClickMethodAsync(plan,ct);
        }
        await ClickFirstPlaceAsync(plan,ct);
        for(int attempt=0;attempt<30;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var state=await _data.ActivityAsync(ct);
            if(state.IsAutoTraveling || state.IsGathering || state.IsFishing) return;
            if(!state.IsSafeField) throw new InvalidOperationException("이동 시작 중 게임 상태가 바뀌어 정지합니다.");
            await Task.Delay(1000,ct);
        }
        using var failure=Capture(ct);
        Fail(failure,"첫 번째 장소 선택 후 이동/채집 시작을 확인하지 못했습니다. 다른 버튼을 누르지 않고 정지합니다.");
    }
    private async Task ClickIngredientAsync(GatheringPlan plan,CancellationToken ct)
    {
        for(int pass=0;pass<2;pass++)
        {
            using var frame=Capture(ct);
            var label=await _vision.FindExactAsync(frame,new(150,700,510,170),plan.DisplayName,ct);
            if(label is null || await _vision.FindExactAsync(frame,new(100,690,580,200),"필요한 재료",ct) is null)
                Fail(frame,"가공 품목에서 선택한 채집 재료를 확인하지 못했습니다.");
            if(pass==0){await Task.Delay(180,ct);continue;}
            _input.ClickClientPoint(_hwnd,label!.Value.Center);
        }
        await Task.Delay(350,ct);
    }
    private async Task ClickMethodAsync(GatheringPlan plan,CancellationToken ct)
    {
        for(int pass=0;pass<2;pass++)
        {
            using var frame=Capture(ct);
            var link=await _vision.FindExactAsync(frame,GatheringVision.MethodLink,"구하는 방법",ct);
            if(link is null || await _vision.FindMaterialAsync(frame,plan.DisplayName,ct) is null)
                Fail(frame,"선택한 재료의 구하는 방법 버튼을 확인하지 못했습니다.");
            if(pass==0){await Task.Delay(180,ct);continue;}
            _input.ClickClientPoint(_hwnd,link!.Value.Center);
        }
        await Task.Delay(350,ct);
    }
    private async Task ClickFirstPlaceAsync(GatheringPlan plan,CancellationToken ct)
    {
        string? name=null;
        for(int pass=0;pass<2;pass++)
        {
            using var frame=Capture(ct);
            if(await _vision.FindMaterialAsync(frame,plan.DisplayName,ct) is null) Fail(frame,"장소 목록의 대상 재료가 바뀌었습니다.");
            var first=await _vision.FirstPlaceAsync(frame,ct);
            if(first is null) Fail(frame,"장소 목록의 맨 위 항목을 확인하지 못했습니다.");
            if(pass==0){name=first!.Value.Text;await Task.Delay(180,ct);continue;}
            if(AlteringText.Normalize(name!)!=AlteringText.Normalize(first!.Value.Text)) Fail(frame,"첫 번째 장소가 변경되어 입력을 정지합니다.");
            Log?.Invoke("[자동채집] 첫 번째 장소 선택: "+name+" · 일반 이동");
            _input.ClickClientPoint(_hwnd,new(first.Value.Bounds.Left+first.Value.Bounds.Width/2,first.Value.Bounds.Top+first.Value.Bounds.Height/2));
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        for(int pass=0;pass<2;pass++)
        {
            var state=await _data.ActivityAsync(ct);
            if(!state.IsGathering && !state.IsAutoTraveling && !state.IsFishing) return;
            using var frame=Capture(ct);
            if(state.MainButtonState!="Stop" || !GatheringVision.HasStopButton(frame))
                Fail(frame,"채집/이동 정지 버튼을 확인하지 못했습니다. 게임에서 직접 정지하세요.");
            if(pass==0){await Task.Delay(150,ct);continue;}
            _input.TapScanCode(0x39); // Space, as shown on the supplied stop control.
        }
    }
    private void Fail(Bitmap frame,string message)
    {
        Directory.CreateDirectory(_debugDir);
        string path=Path.Combine(_debugDir,"gathering-last-failure.png");
        frame.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        throw new InvalidOperationException(message+" 진단: "+path);
    }
    public void Dispose()=>_input.Dispose();
}
