using System.Text;
using System.Text.RegularExpressions;

namespace FishingAutomation;

internal sealed record AlteringPlan(string FacilityName, string DisplayName, int TargetQuantity,
    int ProducedPerWork, bool AllowPaidButton, int RecipeOrdinal = 1)
{
    internal string? VerifiedOcrAlias { get; init; }
    internal int RecipeCount { get; init; } = 1;
    internal int RequiredWorks => checked((int)(((long)TargetQuantity + ProducedPerWork - 1) / ProducedPerWork));
    internal long ExpectedQuantity => (long)RequiredWorks * ProducedPerWork;
    internal long MaximumWings => AllowPaidButton ? (long)RequiredWorks * 5 : 0;
    internal void Validate()
    {
        if (!Facilities.Contains(FacilityName) || string.IsNullOrWhiteSpace(DisplayName) ||
            TargetQuantity is < 1 or > 1000000 || ProducedPerWork <= 0 || RecipeOrdinal < 1)
            throw new InvalidDataException("가공 설정이 올바르지 않습니다.");
    }
    internal static readonly string[] Facilities = { "금속 가공 시설", "목재 가공 시설", "가죽 가공 시설", "옷감 가공 시설", "약품 가공 시설", "식재료 가공 시설" };
    internal string ScreenTitle => FacilityName.Replace(" 시설", "");
    internal string OutputName => Regex.Replace(DisplayName, @"\([^()]*\)$", "").Trim();
}

internal static class AlteringText
{
    // Windows Korean OCR consistently reads the final 괴 glyph as 과 in the supplied
    // screenshots. Allow this specific alias only if it cannot name another CLI recipe.
    internal static string? UniqueOcrAlias(string wanted, IEnumerable<string> catalog)
    {
        if (!wanted.EndsWith("괴", StringComparison.Ordinal)) return null;
        string alias = wanted[..^1] + "과";
        return catalog.Any(x => Normalize(x) == Normalize(alias)) ? null : alias;
    }
    // Do not use FuzzyText.Normalize: it removes '+' and merges different recipes.
    internal static string Normalize(string text) => new string(Regex.Replace(text,
        @"</?color(?:=[^<>]*)?>", "", RegexOptions.CultureInvariant)
        .Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)).ToArray());

    // A faint list card may be a candidate, never authority to spend currency.
    // The larger detail title is subsequently checked with exact matching twice.
    internal static bool IsCardCandidate(string candidate, string wanted)
    {
        string a = Normalize(candidate), b = Normalize(wanted);
        if (a == b) return true;
        if (a.Length != b.Length || b.Length < 3 ||
            new string(a.Where(c => !char.IsLetterOrDigit(c)).ToArray()) !=
            new string(b.Where(c => !char.IsLetterOrDigit(c)).ToArray())) return false;
        return a.Zip(b).Count(x => x.First != x.Second) == 1;
    }
}

internal interface IAlteringScreen : IDisposable
{
    Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct);
    Task CollectAsync(AlteringPlan plan, CancellationToken ct);
}

internal interface IAlteringData
{
    Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct);
    Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct);
    Task<long> ItemCountAsync(string name, CancellationToken ct);
}

internal sealed class AlteringAutomation
{
    private readonly IAlteringData _data;
    private readonly IAlteringScreen _screen;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _verificationAttempts;
    internal event Action<string>? Log;
    internal int QueuedWorks { get; private set; }
    internal long ReservedWings { get; private set; }

    internal AlteringAutomation(IAlteringData data, IAlteringScreen screen,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int verificationAttempts = 120)
    { _data = data; _screen = screen; _delay = delay ?? Task.Delay; _verificationAttempts = verificationAttempts; }

    internal async Task RunAsync(AlteringPlan plan, CancellationToken ct)
    {
        plan.Validate();
        QueuedWorks = 0; ReservedWings = 0;
        var recipes = await _data.RecipesAsync(ct);
        var selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
        if (selected.Length < plan.RecipeOrdinal || selected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
            throw new InvalidOperationException("선택 품목의 조회 결과가 바뀌었습니다. 목록을 다시 불러오세요.");
        plan = plan with { RecipeCount = selected.Length, VerifiedOcrAlias = AlteringText.UniqueOcrAlias(plan.DisplayName, recipes.Select(x => x.DisplayName)) };
        var works = await _data.WorksAsync(ct);
        // No work IDs or actual critical yields exist in the CLI schema. Drain existing
        // same-output jobs before taking the baseline, then produce the requested extra
        // quantity. This permits continuation without counting old rewards as new ones.
        int existingCount = Matching(works, plan).Count();
        if (existingCount > 0)
        {
            Log?.Invoke($"[자동 가공] 기존 {plan.DisplayName} 작업 {existingCount}건을 먼저 완료·수령합니다. 이후 추가 {plan.TargetQuantity}개를 가공합니다.");
            int idlePolls = 0;
            while (Matching(works, plan).Any())
            {
                ct.ThrowIfCancellationRequested();
                if (await CollectIfReadyAsync(plan, works, ct)) works = await _data.WorksAsync(ct);
                if (!Matching(works, plan).Any()) break;
                bool progressing = works.Any(x => x.FacilityName == plan.FacilityName && x.State == "InProgress");
                idlePolls = progressing ? 0 : idlePolls + 1;
                if (idlePolls >= _verificationAttempts)
                    throw new InvalidOperationException("기존 가공 작업이 진행되지 않아 정지합니다. 게임에서 대기열 상태를 확인하세요.");
                long remaining = works.Where(x => x.FacilityName == plan.FacilityName && x.State == "InProgress")
                    .Select(x => x.RemainingSeconds).DefaultIfEmpty(5).Min();
                Log?.Invoke($"[자동 가공] 기존 작업 완료 대기 · 남은 {Matching(works, plan).Count()}건 · 진행 작업 {remaining}초 · 새 작업 등록 0회");
                await _delay(TimeSpan.FromSeconds(Math.Clamp(remaining, 2, 30)), ct);
                works = await _data.WorksAsync(ct);
            }
            Log?.Invoke("[자동 가공] 기존 동일 품목 작업 수령 완료 · 새 목표 수량의 기준을 설정합니다.");
        }
        await CollectIfReadyAsync(plan, works, ct);
        long baseline = await _data.ItemCountAsync(plan.OutputName, ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 목표 {plan.TargetQuantity}개 · {plan.RequiredWorks}회 / 최소 {plan.ExpectedQuantity}개 · 버튼 비용 상한 {plan.MaximumWings}개");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            works = await _data.WorksAsync(ct);
            if (await CollectIfReadyAsync(plan, works, ct)) works = await _data.WorksAsync(ct);
            var outstanding = Matching(works, plan).ToArray();
            if (QueuedWorks == plan.RequiredWorks && outstanding.Length == 0)
            {
                long gained = await _data.ItemCountAsync(plan.OutputName, ct) - baseline;
                if (gained < plan.ExpectedQuantity)
                    throw new InvalidOperationException($"작업은 종료됐지만 수령 수량 확인이 부족합니다: +{gained} / 최소 {plan.ExpectedQuantity}. 중복 시작 없이 정지합니다.");
                Log?.Invoke($"[자동 가공] 완료 · {plan.DisplayName} +{gained}개 · 등록 {QueuedWorks}회 · 버튼 비용 예약 {ReservedWings}개");
                return;
            }
            // The provided facility UI has seven slots. Do not spend a paid click when full.
            bool full = works.Count(x => x.FacilityName == plan.FacilityName) >= 7;
            if (QueuedWorks < plan.RequiredWorks && !full)
            {
                recipes = await _data.RecipesAsync(ct);
                selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                if (selected.Length != plan.RecipeCount || selected.ElementAtOrDefault(plan.RecipeOrdinal - 1)?.ProducedPerWork != plan.ProducedPerWork)
                    throw new InvalidOperationException("가공 제법 또는 생산 수량이 바뀌어 정지합니다.");
                plan = plan with { VerifiedOcrAlias = AlteringText.UniqueOcrAlias(plan.DisplayName, recipes.Select(x => x.DisplayName)) };
                if (selected.Length < plan.RecipeOrdinal || !selected[plan.RecipeOrdinal - 1].Alterable)
                {
                    var recipe = selected.ElementAtOrDefault(plan.RecipeOrdinal - 1);
                    string reason = recipe?.Reason ?? "not_found";
                    string missing = string.Join(", ", recipe?.MissingIngredients.Select(x => $"{x.DisplayName} {x.Owned}/{x.Required}") ?? Array.Empty<string>());
                    throw new InvalidOperationException($"가공 불가: {reason} {missing}. 등록된 작업은 게임에 남습니다.");
                }
                int previous = outstanding.Length;
                bool reserved = false;
                await _screen.QueueAsync(plan, () =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (reserved || !plan.AllowPaidButton || ReservedWings + 5 > plan.MaximumWings)
                        throw new InvalidOperationException("가공 버튼 비용 사용 한도를 초과합니다.");
                    reserved = true; ReservedWings += 5;
                }, ct);
                // A paid click is never retried if registration is uncertain.
                await VerifyAsync(async token =>
                {
                    int count = Matching(await _data.WorksAsync(token), plan).Count();
                    if (count > previous + 1) throw new InvalidOperationException("동시에 다른 가공 작업이 등록되어 수량을 확정할 수 없습니다.");
                    return count == previous + 1;
                }, "작업 등록을 확인하지 못했습니다. 재화 중복 사용을 막기 위해 재클릭하지 않고 정지합니다.", ct);
                QueuedWorks++;
                Log?.Invoke($"[자동 가공] 작업 등록 확인 {QueuedWorks}/{plan.RequiredWorks} · {plan.DisplayName}");
                continue;
            }
            if (full && !works.Any(x => x.FacilityName == plan.FacilityName && x.State == "InProgress"))
                throw new InvalidOperationException("가공 대기열이 가득 찼지만 진행 중인 작업이 없습니다. 게임에서 작업 상태를 확인하세요.");
            var remaining = works.Where(x => x.FacilityName == plan.FacilityName && x.State == "InProgress")
                .Select(x => x.RemainingSeconds).DefaultIfEmpty(10).Min();
            Log?.Invoke($"[자동 가공] 완료 대기 · 등록 {QueuedWorks}/{plan.RequiredWorks} · 남은 시간 {remaining}초");
            await _delay(TimeSpan.FromSeconds(Math.Clamp(remaining, 2, 30)), ct);
        }
    }

    private static IEnumerable<AlteringWork> Matching(IEnumerable<AlteringWork> works, AlteringPlan plan)
        => works.Where(x => (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName) && x.FacilityName == plan.FacilityName);

    private async Task<bool> CollectIfReadyAsync(AlteringPlan plan, IReadOnlyList<AlteringWork> works, CancellationToken ct)
    {
        int count = works.Count(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        if (count == 0) return false;
        int totalBefore = works.Count(x => x.FacilityName == plan.FacilityName);
        await _screen.CollectAsync(plan, ct);
        // Another queued work may finish during collection. Total queue shrinkage is
        // the receipt signal; completedCount alone can stay unchanged or increase.
        await VerifyAsync(async token => (await _data.WorksAsync(token)).Count(x => x.FacilityName == plan.FacilityName) < totalBefore,
            "완료 작업 수령을 확인하지 못했습니다. 반복 클릭 없이 정지합니다.", ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 완료 작업 수령 확인");
        return true;
    }

    private async Task VerifyAsync(Func<CancellationToken, Task<bool>> condition, string failure, CancellationToken ct)
    {
        for (int i = 0; i < _verificationAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await condition(ct)) return;
            await _delay(TimeSpan.FromSeconds(5), ct);
        }
        throw new InvalidOperationException(failure);
    }
}
