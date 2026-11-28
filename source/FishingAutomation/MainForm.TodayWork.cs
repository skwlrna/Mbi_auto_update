using System.Text.Json;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed record TodayRun(string Kind, string Name, DateTimeOffset ChangedAt, string State, long? Count = null)
    {
        internal string Summary => $"{Name} · {State}" +
            (Count is long count ? $" · {(Kind == "가공" ? "수령" : "획득")} 확인 {count:N0}개" : "");
    }

    private sealed record TodayWorkSummary(string Altering, string Abyss, string Gathering, string[] GatheringDetails);

    private static readonly string TodayWorkPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MabiAuto", "today-work.json");
    private readonly List<TodayRun> _todayRuns = ReadTodayRuns();
    private TodayRun? _todayAlteringRun;
    private TodayRun? _todayGatheringRun;

    private static List<TodayRun> ReadTodayRuns()
    {
        try
        {
            if (new FileInfo(TodayWorkPath) is { Exists: true, Length: <= 65536 })
                return (JsonSerializer.Deserialize<List<TodayRun>>(File.ReadAllText(TodayWorkPath)) ?? [])
                    .Where(run => run is not null).TakeLast(50).ToList();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return [];
    }

    private void SaveTodayRun(TodayRun? run, string state, long? count = null)
    {
        if (run is null) return;
        _todayRuns.Add(run with { ChangedAt = DateTimeOffset.Now, State = state, Count = count ?? run.Count });
        if (_todayRuns.Count > 50) _todayRuns.RemoveAt(0);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TodayWorkPath)!);
            string temp = TodayWorkPath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_todayRuns));
            File.Move(temp, TodayWorkPath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private PhoneAlteringOrder[] ReadTodayPhoneOrders(DateTime date)
    {
        try
        {
            var queue = _phoneAlteringQueue;
            if (queue is null && File.Exists(PhoneAlteringPath))
                queue = JsonSerializer.Deserialize<PhoneAlteringQueue>(File.ReadAllText(PhoneAlteringPath));
            return queue?.Orders.Where(order => order.ChangedAt?.LocalDateTime.Date == date).ToArray() ?? [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private string PhoneOrderSummary(PhoneAlteringOrder order)
    {
        string detail = _phoneAlteringLiveDetails.GetValueOrDefault(order.Number, order.Detail);
        string state = _phoneAlteringQueue is null && order.State == "실행 중" ? "확인 필요" : order.State;
        return $"{order.Name} · {(order.Total ? "보유 목표" : "추가 요청")} {order.Quantity:N0}개 · {state}" +
            (string.IsNullOrWhiteSpace(detail) ? "" : $" · {detail}");
    }

    private TodayWorkSummary GetTodayWorkSummary()
    {
        DateTime date = DateTime.Today;
        var runs = _todayRuns.Where(run => run.ChangedAt.LocalDateTime.Date == date).ToArray();
        var orders = ReadTodayPhoneOrders(date);
        var order = orders.LastOrDefault(x => x.State == "실행 중") ??
            orders.OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Number).FirstOrDefault();
        var altering = _todayAlteringRun ?? runs.LastOrDefault(run => run.Kind == "가공");
        var gathering = _todayGatheringRun ?? runs.LastOrDefault(run => run.Kind == "채집");
        bool showOrder = order is not null && (altering is null || order.State == "실행 중" ||
            (_todayAlteringRun is null && order.ChangedAt >= altering.ChangedAt));
        string alteringText = showOrder ? PhoneOrderSummary(order!) : altering?.Summary ?? "오늘 가공 기록 없음";

        var (rounds, rewards, retryMisses) = AbyssRoundHistory.TodayCounts(date);
        string abyss = rounds == 0 ? "오늘 기록된 판 없음" : $"기록 {rounds}판 · 보상 확인 {rewards}판";
        if (rounds == 50) abyss += " · 최근 50판 기준";
        if (retryMisses > 0) abyss += $" · 재입장 미확인 {retryMisses}판";
        if (ActiveAbyssWait is not null) abyss += " · 진행 중";

        var gatherings = runs.Where(run => run.Kind == "채집").Reverse()
            .Select(run => $"{run.ChangedAt.ToLocalTime():HH:mm} · {run.Summary}");
        if (_todayGatheringRun is { } active) gatherings = gatherings.Prepend(active.Summary);
        return new TodayWorkSummary(alteringText, abyss, gathering?.Summary ?? "오늘 채집 기록 없음",
            gatherings.Take(10).ToArray());
    }

    private void ShowTodayProductionDetails(string kind)
    {
        string[] rows = kind == "채집" ? GetTodayWorkSummary().GatheringDetails :
            ReadTodayPhoneOrders(DateTime.Today).Reverse().Select(PhoneOrderSummary)
                .Concat(_todayRuns.Where(run => run.Kind == kind && run.ChangedAt.LocalDateTime.Date == DateTime.Today)
                    .Reverse().Select(run => run.Summary)).Take(20).ToArray();
        if (kind == "가공" && _todayAlteringRun is { } active)
            rows = rows.Prepend(active.Summary).ToArray();
        ShowWorkHistoryDialog($"오늘 {kind} 상세", rows);
    }
}
