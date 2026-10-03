using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class GatheringScreen : IGatheringScreen
{
    private readonly AppSettings _settings;
    private readonly string _debugDir;
    private readonly MabinogiMobileCli _cli;
    private readonly IGatheringData _data;
    private readonly GatheringVision _vision = new();
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("채집-보조");
    internal string InputMode => _ui.InputMode;
    internal event Action<string>? Log;

    internal GatheringScreen(nint hwnd, AppSettings settings, string debugDir, MabinogiMobileCli cli, IGatheringData data)
    {
        _settings=settings; _debugDir=debugDir; _cli=cli; _data=data;
        _ui = new ProductionUiRuntime(hwnd, settings, debugDir, "gathering");
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[자동채집][상태] {stage} · {detail}");
    }
    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);
    private async Task<bool> AtMaterialAsync(GatheringPlan plan,CancellationToken ct)
    {
        using var frame=Capture(ct);
        return await _vision.FindMaterialAsync(frame,plan.DisplayName,ct) is not null;
    }
    public async Task StartAsync(GatheringPlan plan,CancellationToken ct)
    {
        plan.Validate();
        _stage.Move(ProductionStage.OpenHub, $"{plan.DisplayName} 획득 경로 준비");
        if(!await AtMaterialAsync(plan,ct) && plan.SourceRecipe is AlteringPlan source)
        {
            var recipes=AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync(ct));
            var selected=recipes.Where(x=>x.DisplayName==source.DisplayName).ToArray();
            if(selected.Length<source.RecipeOrdinal) throw new InvalidOperationException("시작 가공 품목을 목록에서 확인하지 못했습니다.");
            source=source with { ProducedPerWork=selected[source.RecipeOrdinal-1].ProducedPerWork,RecipeCount=selected.Length,
                VerifiedOcrAlias=AlteringText.UniqueOcrAlias(source.DisplayName,recipes.Select(x=>x.DisplayName)),AllowPaidButton=false };
            _stage.Move(ProductionStage.Search, $"{source.DisplayName}의 {plan.DisplayName} 재료");
            using(var navigator=new AlteringScreen(_ui.WindowHandle,_settings,_debugDir)) await navigator.OpenRecipeAsync(source,ct);
            await ClickIngredientAsync(plan,ct);
        }
        if(!await AtMaterialAsync(plan,ct))
        {
            using var frame=Capture(ct);
            Fail(frame,"선택한 채집 재료의 상세 화면을 확인하지 못했습니다. 재료 상세/구하는 방법을 열거나 시작 가공 품목을 설정하세요.");
        }
        _stage.Move(ProductionStage.Detail, plan.DisplayName);
        using(var frame=Capture(ct))
        {
            if(await _vision.FirstPlaceAsync(frame,ct) is null) await ClickMethodAsync(plan,ct);
        }
        long beforeCount=await _data.ItemCountAsync(plan.DisplayName,ct);
        _stage.Move(ProductionStage.Travel, $"{plan.DisplayName} 첫 획득처 이동");
        await ClickFirstPlaceAsync(plan,ct);

        long lastCount=beforeCount;
        for(int attempt=0;attempt<120;attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var state=await _data.ActivityAsync(ct);

            // Do not force get_items while the game explicitly reports pure travel.
            // This is the same loading guard used by crafting material acquisition.
            if(state.IsAutoTraveling && !state.IsGathering && !state.IsFishing)
            {
                if(attempt==0 || attempt%10==0)
                    Log?.Invoke($"[자동채집] 이동 중 · 재고 CLI 조회 생략 · {plan.DisplayName}");
                await Task.Delay(1000,ct);
                continue;
            }

            long currentCount=await _data.ItemCountAsync(plan.DisplayName,ct);
            long gained=Math.Max(0,currentCount-beforeCount);

            if(currentCount!=lastCount)
            {
                Log?.Invoke($"[자동채집] {plan.DisplayName} 수량 변화 확인 · {beforeCount} → {currentCount} · +{gained}/{plan.TargetQuantity}");
                lastCount=currentCount;
            }

            // Inventory is the source of truth. Some live get_activity responses remain
            // Compass/None for the whole route even though auto travel and gathering
            // actually complete. If the requested new quantity is present, let the
            // outer gathering session verify it and continue back to production.
            if(gained>=plan.TargetQuantity)
            {
                Log?.Invoke($"[자동채집] 목표 수량 확보 확인 · {plan.DisplayName} +{gained} · activity 상태와 무관하게 성공 처리");
                _stage.Move(ProductionStage.VerifyInventory, $"{plan.DisplayName} +{gained}");
                return;
            }

            // When gathering itself is observable, return immediately and let
            // GatheringAutomation monitor quantity/progress as before.
            if(state.IsGathering || state.IsFishing)
            {
                Log?.Invoke($"[자동채집] 채집 상태 확인 · Gathering={state.IsGathering}, Fishing={state.IsFishing}");
                return;
            }

            if(!state.IsSafeField)
                throw new InvalidOperationException("이동/채집 대기 중 게임 상태가 바뀌어 정지합니다.");

            await Task.Delay(1000,ct);
        }

        long finalCount=await _data.ItemCountAsync(plan.DisplayName,ct);
        using var failure=Capture(ct);
        Fail(failure,$"첫 번째 장소 선택 후 이동/채집 또는 목표 수량 확보를 확인하지 못했습니다. {plan.DisplayName} {beforeCount}→{finalCount}, 목표 +{plan.TargetQuantity}. 다른 버튼을 누르지 않고 정지합니다.");
    }
    private async Task ClickIngredientAsync(GatheringPlan plan,CancellationToken ct)
    {
        // Ingredient names in the live processing detail sheet are rendered in a dim
        // gray style. Use the same high-contrast OCR path that material-detail titles
        // use instead of requiring bright recipe-card text.
        var ingredientArea = new Rectangle(120, 690, 560, 200);
        var materialsHeaderArea = new Rectangle(100, 680, 600, 210);

        for(int pass=0;pass<2;pass++)
        {
            using var frame=Capture(ct);
            var materialsHeader = await _vision.FindExactAsync(
                frame, materialsHeaderArea, "필요한 재료", ct, dim:true);
            var label = await _vision.FindExactAsync(
                frame, ingredientArea, plan.DisplayName, ct, dim:true);

            if(materialsHeader is null || label is null)
                Fail(frame,$"가공 품목의 필요한 재료에서 {plan.DisplayName}을 확인하지 못했습니다.");

            if(pass==0)
            {
                await Task.Delay(180,ct);
                continue;
            }

            Log?.Invoke($"[자동채집] 필요한 재료 확인 · {plan.DisplayName} · 어두운 재료명 OCR 2프레임 확인");
            _ui.ClickFresh(label.Value.Center, ct);
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
            _ui.ClickFresh(link!.Value.Center, ct);
        }
        await Task.Delay(350,ct);
    }
    private async Task ClickFirstPlaceAsync(GatheringPlan plan,CancellationToken ct)
    {
        Rectangle? firstBounds=null;
        string? firstText=null;

        for(int pass=0;pass<2;pass++)
        {
            using var frame=Capture(ct);
            if(await _vision.FindMaterialAsync(frame,plan.DisplayName,ct) is null)
                Fail(frame,"장소 목록의 대상 재료가 바뀌었습니다.");

            var first=await _vision.FirstPlaceAsync(frame,ct);
            if(first is null)
                Fail(frame,"장소 목록의 맨 위 항목을 확인하지 못했습니다.");

            if(pass==0)
            {
                firstBounds=first.Value.Bounds;
                firstText=first.Value.Text;
                await Task.Delay(180,ct);
                continue;
            }

            if(firstBounds is null || !GatheringNavigationPolicy.IsStableFirstRow(firstBounds.Value,first.Value.Bounds))
                Fail(frame,"첫 번째 장소 행 위치가 변경되어 입력을 정지합니다.");

            string display = string.IsNullOrWhiteSpace(first.Value.Text) ? firstText ?? "첫 번째 장소" : first.Value.Text;
            Log?.Invoke("[자동채집] 첫 번째 장소 선택: "+display+" · 위치 재확인 완료 · 일반 이동");

            // Always click the second, freshest frame. The recommendation badge,
            // place-name OCR string and displayed distance do not affect selection.
            _ui.ClickFresh(new(
                first.Value.Bounds.Left+first.Value.Bounds.Width/2,
                first.Value.Bounds.Top+first.Value.Bounds.Height/2), ct);
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
            _ui.TapFresh(0x39, ct); // Space, as shown on the supplied stop control.
        }
    }
    private void Fail(Bitmap frame,string message)
        => throw _ui.Failure(frame, message);

    public void Dispose()=>_ui.Dispose();
}
