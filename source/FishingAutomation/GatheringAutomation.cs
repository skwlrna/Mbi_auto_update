namespace FishingAutomation;

internal sealed record GatheringPlan(string DisplayName, int TargetQuantity)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || TargetQuantity is < 1 or > 1000000)
            throw new InvalidDataException("채집 품목과 추가 수량을 확인하세요.");
    }
}

internal interface IGatheringData
{
    Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct);
    Task<GatheringActivity> ActivityAsync(CancellationToken ct);
    Task<(decimal Current, decimal Maximum)> WeightAsync(CancellationToken ct);
    Task<long> ItemCountAsync(string name, CancellationToken ct);
}

// This interface deliberately has no CLI action or paid-navigation operation.
// The screen implementation must follow the user's ordinary obtain-method route.
internal interface IGatheringScreen : IDisposable
{
    Task StartAsync(GatheringPlan plan, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

internal sealed class GatheringAutomation
{
    private readonly IGatheringData _data;
    private readonly IGatheringScreen _screen;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _noProgressLimit;
    internal event Action<string>? Log;
    internal long Gained { get; private set; }

    internal GatheringAutomation(IGatheringData data, IGatheringScreen screen,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int noProgressLimit = 150)
    { _data = data; _screen = screen; _delay = delay ?? Task.Delay; _noProgressLimit = noProgressLimit; }

    internal async Task RunAsync(GatheringPlan plan, CancellationToken ct)
    {
        plan.Validate(); Gained = 0;
        await CheckToolAsync(plan, ct);
        var initial = await _data.ActivityAsync(ct);
        if (!initial.IsSafeField || initial.IsGathering || initial.IsFishing || initial.IsAutoTraveling || initial.MainButtonState == "Stop")
            throw new InvalidOperationException("진행 중인 행동을 종료하고 필드에서 자동채집을 시작하세요.");
        await CheckWeightAsync(ct);
        long baseline = await _data.ItemCountAsync(plan.DisplayName, ct);
        bool started = false;
        Exception? failure = null;
        try
        {
            Log?.Invoke($"[자동채집] {plan.DisplayName} 추가 {plan.TargetQuantity}개 · 정령의 날개 사용 0개");
            // Set before input so a partially successful start is still stopped on failure.
            started = true;
            await _screen.StartAsync(plan, ct);
            int idlePolls = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var activity = await _data.ActivityAsync(ct);
                if (activity.IsFishing)
                    throw new InvalidOperationException("선택 품목이 낚시로 연결되었습니다. 현재 자동채집에서는 낚시를 지원하지 않아 정지합니다.");
                if (!activity.IsSafeField)
                    throw new InvalidOperationException("전투·사망·대화 등 채집을 계속할 수 없는 상태가 확인되어 정지합니다.");
                long count = await _data.ItemCountAsync(plan.DisplayName, ct);
                if (count < baseline) throw new InvalidOperationException("채집 중 대상 재료의 보유 수량이 감소해 수량을 확정할 수 없습니다.");
                long gained = count - baseline;
                if (gained < Gained) throw new InvalidOperationException("채집 중 재료가 소비되어 추가 수량 확인을 중단합니다.");
                idlePolls = gained > Gained ? 0 : idlePolls + 1;
                Gained = gained;
                Log?.Invoke($"[자동채집] {plan.DisplayName} +{Gained}/{plan.TargetQuantity}개");
                if (Gained >= plan.TargetQuantity) break;
                await CheckToolAsync(plan, ct);
                await CheckWeightAsync(ct);
                if (idlePolls >= _noProgressLimit)
                    throw new InvalidOperationException("일정 시간 채집 수량이 증가하지 않아 추가 조작 없이 정지합니다.");
                await _delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch(Exception ex) { failure = ex; throw; }
        finally
        {
            if (started)
            {
                // F10 cancels sampling but must still permit a short, guarded stop.
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    var state = await _data.ActivityAsync(stopCts.Token);
                    if (state.IsGathering || state.IsFishing || (state.IsAutoTraveling && state.IsSafeField))
                    {
                        await _screen.StopAsync(stopCts.Token);
                        bool stopped = false;
                        for(int attempt = 0; attempt < 4; attempt++)
                        {
                            state = await _data.ActivityAsync(stopCts.Token);
                            if (!state.IsGathering && !state.IsFishing && !state.IsAutoTraveling) { stopped = true; break; }
                            await _delay(TimeSpan.FromSeconds(1), stopCts.Token);
                        }
                        if(!stopped) throw new InvalidOperationException("채집/이동 정지를 확인하지 못했습니다. 게임의 정지 버튼을 확인하세요.");
                    }
                }
                catch(Exception stopError) when (failure is not null)
                {
                    Log?.Invoke("[자동채집] 정지 확인 실패: " + stopError.Message + " · 게임에서 채집/이동 상태를 확인하세요.");
                }
            }
        }
        Log?.Invoke($"[자동채집] 완료 · {plan.DisplayName} +{Gained}개 · 정령의 날개 사용 0개");
    }

    private async Task CheckToolAsync(GatheringPlan plan, CancellationToken ct)
    {
        var item = (await _data.CatalogAsync(ct)).SingleOrDefault(x => x.DisplayName == plan.DisplayName);
        if (item is null) throw new InvalidOperationException("현재 생활 레벨에서 선택한 품목을 채집할 수 없습니다.");
        if (!item.ToolOk) throw new InvalidOperationException("채집 도구가 없거나 내구도가 부족합니다.");
    }
    private async Task CheckWeightAsync(CancellationToken ct)
    {
        var weight = await _data.WeightAsync(ct);
        if (weight.Current >= weight.Maximum) throw new InvalidOperationException("가방 무게가 가득 차 채집을 정지합니다.");
    }
}
