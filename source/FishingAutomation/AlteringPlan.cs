using System.Text;
using System.Text.RegularExpressions;

namespace FishingAutomation;

internal static class AlteringEtaEstimator
{
    internal static long? Estimate(
        int requiredWorks,
        int queuedWorks,
        int activeItemSlots,
        long? batchRemainingSeconds,
        long? estimatedWorkSeconds)
    {
        if (requiredWorks <= 0 || queuedWorks < 0 || queuedWorks > requiredWorks)
            return null;

        int futureWorks = Math.Max(0, requiredWorks - queuedWorks);
        long? cycle = estimatedWorkSeconds ?? batchRemainingSeconds;

        if (futureWorks == 0)
            return batchRemainingSeconds ?? 0;

        if (!cycle.HasValue || cycle.Value <= 0)
            return null;

        int slots = Math.Max(1, activeItemSlots);
        long futureCycles = (futureWorks + (long)slots - 1) / slots;
        long currentBatch = Math.Max(0, batchRemainingSeconds ?? 0);

        try
        {
            return checked(currentBatch + futureCycles * cycle.Value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}

internal sealed record AlteringPlan(string FacilityName, string DisplayName, int TargetQuantity,
    int ProducedPerWork, bool AllowPaidButton, int RecipeOrdinal = 1)
{
    internal string? VerifiedOcrAlias { get; init; }
    internal int RecipeCount { get; init; } = 1;
    internal int RequiredWorks => checked((int)(((long)TargetQuantity + ProducedPerWork - 1) / ProducedPerWork));
    internal long ExpectedQuantity => (long)RequiredWorks * ProducedPerWork;
    internal long MaximumWings => 0;
    internal void Validate()
    {
        if (AllowPaidButton)
            throw new InvalidDataException("정령의 날개 사용은 허용되지 않습니다. 자동 가공은 항상 0개 사용 모드입니다.");
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
    Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct);
    Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct);
}

internal interface IDirectCliAlteringScreen
{
    Task CompleteAsync(string displayName, CancellationToken ct);
}

internal interface IAlteringRecoveryScreen
{
    Task RecoverStallAsync(AlteringPlan plan, int attempt, string reason, CancellationToken ct);
}

internal interface IAlteringFieldExitScreen
{
    Task ExitToFieldAsync(CancellationToken ct);
}

internal interface IAlteringData
{
    Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct);
    Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct);
    Task<long> ItemCountAsync(string name, CancellationToken ct);
}

internal interface IAlteringSupplyResolver
{
    Task ResolveAsync(AlteringPlan parentPlan, AlteringRecipe blockedRecipe, int remainingWorks, CancellationToken ct);
}

internal enum AlteringRunResult
{
    Completed,
    BatchQueued
}

internal sealed class AlteringAutomation
{
    private readonly IAlteringData _data;
    private readonly IAlteringScreen _screen;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _verificationAttempts;
    private readonly IAlteringSupplyResolver? _supplyResolver;
    private readonly IAlteringInternalConsumptionObserver? _internalConsumptionObserver;
    private readonly Action<AlteringPlan, IReadOnlyList<AlteringWork>>? _onConfirmedReceipt;
    private readonly AlteringSessionStore? _sessionStore;
    private AlteringSessionState? _session;
    private long? _estimatedWorkSeconds;
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(60);
    private const int MaxStallRecoveries = 3;

    internal event Action<string>? Log;
    internal event Action<AlteringProgress>? Progress;
    internal int QueuedWorks { get; private set; }
    internal int ConfirmedRegistrationsThisRun { get; private set; }
    internal long ReservedWings { get; private set; }

    internal AlteringAutomation(IAlteringData data, IAlteringScreen screen,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int verificationAttempts = 120,
        IAlteringSupplyResolver? supplyResolver = null,
        AlteringSessionStore? sessionStore = null,
        AlteringSessionState? session = null,
        IAlteringInternalConsumptionObserver? internalConsumptionObserver = null,
        Action<AlteringPlan, IReadOnlyList<AlteringWork>>? onConfirmedReceipt = null)
    {
        if ((sessionStore is null) != (session is null))
            throw new ArgumentException("이어하기 저장소와 세션 상태는 함께 제공해야 합니다.");

        _data = data;
        _screen = screen;
        _delay = delay ?? Task.Delay;
        _verificationAttempts = verificationAttempts;
        _supplyResolver = supplyResolver;
        _internalConsumptionObserver = internalConsumptionObserver;
        _onConfirmedReceipt = onConfirmedReceipt;
        _sessionStore = sessionStore;
        _session = session;
    }

    internal async Task RunAsync(AlteringPlan plan, CancellationToken ct)
        => _ = await RunCoreAsync(plan, yieldAtBatchBoundary: false, batchRegistrationLimit: null, ct);

    internal Task<AlteringRunResult> RunBatchAsync(AlteringPlan plan, CancellationToken ct)
        => RunCoreAsync(plan, yieldAtBatchBoundary: true, batchRegistrationLimit: null, ct);

    internal Task<AlteringRunResult> RunBatchAsync(
        AlteringPlan plan,
        int maxRegistrations,
        CancellationToken ct)
    {
        if (maxRegistrations is < 1 or > 7)
            throw new ArgumentOutOfRangeException(
                nameof(maxRegistrations),
                "다중가공 한 번의 배치 등록 제한은 1~7칸이어야 합니다.");
        return RunCoreAsync(
            plan,
            yieldAtBatchBoundary: true,
            batchRegistrationLimit: maxRegistrations,
            ct);
    }

    private async Task<AlteringRunResult> RunCoreAsync(
        AlteringPlan plan,
        bool yieldAtBatchBoundary,
        int? batchRegistrationLimit,
        CancellationToken ct)
    {
        plan.Validate();
        QueuedWorks = 0;
        ReservedWings = 0;

        var recipes = await _data.RecipesAsync(ct);
        var selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
        if (selected.Length < plan.RecipeOrdinal ||
            selected[plan.RecipeOrdinal - 1].ProducedPerWork != plan.ProducedPerWork)
            throw new InvalidOperationException("선택 품목의 조회 결과가 바뀌었습니다. 목록을 다시 불러오세요.");

        plan = plan with
        {
            RecipeCount = selected.Length,
            VerifiedOcrAlias = AlteringText.UniqueOcrAlias(plan.DisplayName, recipes.Select(x => x.DisplayName))
        };

        var works = await _data.WorksAsync(ct);
        long baseline;
        int initialExistingCount;

        if (_session is not null)
        {
            if (!_session.MatchesPlan(plan))
                throw new InvalidOperationException("저장된 자동 가공 작업과 현재 선택한 작업이 달라 이어서 실행하지 않습니다.");

            QueuedWorks = _session.QueuedWorks;
            baseline = _session.BaselineQuantity;
            initialExistingCount = _session.InitialExistingWorks;

            int currentKnownWorks = Matching(works, plan).Count();
            int maximumKnownWorks = checked(
                _session.InitialExistingWorks + _session.QueuedWorks + (_session.PendingRegistration ? 1 : 0));
            if (currentKnownWorks > maximumKnownWorks)
                throw new InvalidOperationException(
                    $"이어하기 대기열에 저장 기록보다 많은 동일 품목 작업이 있습니다: 저장 기준 최대 {maximumKnownWorks}건 / 현재 {currentKnownWorks}건. " +
                    "외부에서 추가 등록된 작업과 목표 작업을 구분할 수 없어 정지합니다.");

            long currentOutput = EffectiveOutputQuantity(
                await _data.ItemCountAsync(plan.OutputName, ct));
            if (currentOutput < _session.LastObservedOutputQuantity)
                throw new InvalidOperationException(
                    $"이어하기 확인 중 {plan.OutputName} 보유량이 감소했습니다: 마지막 확인 {_session.LastObservedOutputQuantity:N0} / 현재 {currentOutput:N0}. " +
                    "완성품 소비로 목표 추적이 모호해져 중복 등록 방지를 위해 정지합니다.");

            // When this session started with no pre-existing same-item jobs, every
            // current matching job belongs to this session. Rebuild progress from
            // authoritative facts instead of trusting a stale queued-work counter:
            //   1) output gained since the session baseline, expressed as minimum
            //      work-equivalents, plus
            //   2) same-item jobs that are still present in the live queue.
            // This safely handles the user receiving completed jobs and cancelling
            // the remaining queued jobs before pressing F9 again.
            if (initialExistingCount == 0)
            {
                long gainedSinceStart = Math.Max(0, currentOutput - baseline);
                int completedEquivalent = checked((int)Math.Min(
                    plan.RequiredWorks,
                    gainedSinceStart / plan.ProducedPerWork));
                int reconciledQueued = Math.Min(
                    plan.RequiredWorks,
                    checked(completedEquivalent + currentKnownWorks));

                int savedQueued = QueuedWorks;
                bool hadPending = _session.PendingRegistration;
                QueuedWorks = reconciledQueued;

                if (savedQueued != QueuedWorks || hadPending)
                {
                    int cancelledEquivalent = Math.Max(0, savedQueued - QueuedWorks);
                    Log?.Invoke(
                        $"[자동 가공] 이어하기 실상태 재산정 · 저장 등록 {savedQueued}/{plan.RequiredWorks} → " +
                        $"실제 진행 {QueuedWorks}/{plan.RequiredWorks} · 세션 완성품 증가 +{gainedSinceStart:N0}개 " +
                        $"({completedEquivalent}작업 상당) · 현재 대기 {currentKnownWorks}건" +
                        (cancelledEquivalent > 0
                            ? $" · 취소/소멸 {cancelledEquivalent}작업 상당 제외"
                            : ""));
                }

                SaveSession(_session with
                {
                    QueuedWorks = QueuedWorks,
                    PendingRegistration = false,
                    PendingBeforeMatchingCount = 0,
                    LastObservedOutputQuantity = currentOutput,
                    Stage = "가공 이어하기 · 실상태 재산정"
                });
            }
            else if (_session.PendingRegistration)
            {
                int current = currentKnownWorks;
                int before = _session.PendingBeforeMatchingCount;
                long outputGain = currentOutput - _session.LastObservedOutputQuantity;
                if (current == before + 1)
                {
                    QueuedWorks++;
                    Log?.Invoke($"[자동 가공] 이어하기 등록 복구 · 중단 직전 1작업이 실제 등록된 것으로 확인 · 전체 {QueuedWorks}/{plan.RequiredWorks}");
                }
                else if (current == before && outputGain == 0)
                {
                    Log?.Invoke("[자동 가공] 이어하기 등록 복구 · 중단 직전 작업은 등록되지 않은 것으로 확인 · 재클릭 없이 정상 진행");
                }
                else if (current < before)
                {
                    int disappearedWithoutPending = before - current;
                    long minimumWithoutPending = checked((long)disappearedWithoutPending * plan.ProducedPerWork);
                    long minimumWithPending = checked((long)(disappearedWithoutPending + 1) * plan.ProducedPerWork);

                    if (outputGain >= minimumWithoutPending && outputGain < minimumWithPending)
                    {
                        Log?.Invoke(
                            $"[자동 가공] 이어하기 등록 복구 · 저장 후 기존 작업 {disappearedWithoutPending}건이 수령되어 대기열이 감소했지만 " +
                            $"완성품 증가 +{outputGain:N0}개로 중단 직전 새 작업은 등록되지 않은 것으로 확정 · 재클릭 없이 정상 진행");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"이어하기 등록 상태를 안전하게 확정할 수 없습니다. 저장 전 {before}건 / 현재 {current}건 / " +
                            $"완성품 증가 +{outputGain:N0}개. 기존 작업이 섞인 세션이라 자동 재산정하지 않고 정지합니다.");
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"이어하기 등록 상태를 안전하게 확정할 수 없습니다. 저장 전 {before}건 / 현재 {current}건 / " +
                        $"완성품 증가 +{outputGain:N0}개. 기존 작업이 섞인 세션이라 자동 재산정하지 않고 정지합니다.");
                }

                SaveSession(_session with
                {
                    QueuedWorks = QueuedWorks,
                    PendingRegistration = false,
                    PendingBeforeMatchingCount = 0,
                    LastObservedOutputQuantity = currentOutput,
                    Stage = "가공 이어하기"
                });
            }

            if (QueuedWorks > plan.RequiredWorks)
                throw new InvalidOperationException("저장된 등록 횟수가 현재 목표 작업 수를 초과해 이어서 실행하지 않습니다.");

            Log?.Invoke(
                $"[자동 가공] 이어하기 · 저장 등록 {QueuedWorks}/{plan.RequiredWorks} · 기준 보유 {baseline:N0}개 · 기존 작업 {initialExistingCount}건 · 저장 단계={_session.Stage}");
        }
        else
        {
            initialExistingCount = Matching(works, plan).Count();
            baseline = await _data.ItemCountAsync(plan.OutputName, ct);
        }

        if (initialExistingCount > 0)
            Log?.Invoke($"[자동 가공] 기존 {plan.DisplayName} 작업 {initialExistingCount}건 유지 · 완료되는 즉시 수령하고 빈 슬롯에 새 목표 작업을 채웁니다.");

        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 목표 {plan.TargetQuantity}개 · {plan.RequiredWorks}회 / 최소 {plan.ExpectedQuantity}개 · 정령의 날개 0개 고정");
        SaveStage("가공 진행");

        string? lastProgressSignature = null;
        DateTime lastProgressAt = DateTime.UtcNow;
        int stallRecoveries = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            works = await _data.WorksAsync(ct);

            if (works.Any(x => x.FacilityName == plan.FacilityName && x.IsCompleted))
                SaveStage("완료품 수령");
            if (await CollectIfReadyAsync(plan, works, ct))
            {
                works = await _data.WorksAsync(ct);
                // A nested dependency can start before RunBatchAsync returns.
                // Reconcile the proven receipt immediately, rather than leaving
                // the parent lane's shrinking queue for a child to misclassify
                // as an external/manual cancellation.
                _onConfirmedReceipt?.Invoke(plan, works);
                SaveStage("가공 진행");
            }

            string progressSignature = ProgressSignature(works, plan, QueuedWorks);
            if (!string.Equals(progressSignature, lastProgressSignature, StringComparison.Ordinal))
            {
                lastProgressSignature = progressSignature;
                lastProgressAt = DateTime.UtcNow;
                stallRecoveries = 0;
            }
            else if (DateTime.UtcNow - lastProgressAt >= StallThreshold)
            {
                if (stallRecoveries >= MaxStallRecoveries)
                    throw new InvalidOperationException(
                        $"가공 진행 정체가 {MaxStallRecoveries}회 복구 후에도 계속되었습니다. 마지막 진단 화면을 저장한 상태로 정지합니다.");

                stallRecoveries++;
                string reason = $"대기열/남은시간 변화 없음 {StallThreshold.TotalSeconds:0}초 · 등록 {QueuedWorks}/{plan.RequiredWorks}";
                Log?.Invoke($"[자동 가공] 정체 감지 {stallRecoveries}/{MaxStallRecoveries} · {reason}");
                SaveStage($"정체 복구 {stallRecoveries}/{MaxStallRecoveries}");

                if (_screen is IAlteringRecoveryScreen recovery)
                {
                    await recovery.RecoverStallAsync(plan, stallRecoveries, reason, ct);
                    lastProgressAt = DateTime.UtcNow;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"가공 진행이 {StallThreshold.TotalSeconds:0}초 동안 변하지 않았고 화면 복구 기능을 사용할 수 없어 정지합니다.");
                }
            }

            await ReportProgressAsync(works, plan, baseline, initialExistingCount, "재료 정상", ct);

            var outstanding = Matching(works, plan).ToArray();
            if (QueuedWorks == plan.RequiredWorks && outstanding.Length == 0)
            {
                long grossGained = EffectiveOutputQuantity(
                    await _data.ItemCountAsync(plan.OutputName, ct)) - baseline;
                long oldMinimum = checked((long)initialExistingCount * plan.ProducedPerWork);
                long targetGained = Math.Max(0, grossGained - oldMinimum);
                if (targetGained < plan.ExpectedQuantity)
                    throw new InvalidOperationException(
                        $"작업은 종료됐지만 새 목표 수령 수량 확인이 부족합니다: +{targetGained} / 최소 {plan.ExpectedQuantity}. " +
                        $"총 인벤토리 변화 +{grossGained}, 기존 작업 최소 생산량 {oldMinimum} 제외. 중복 시작 없이 정지합니다.");

                Log?.Invoke(
                    $"[자동 가공] 완료 · {plan.DisplayName} 새 목표 +{targetGained}개 · 등록 {QueuedWorks}회 · 기존 작업 최소 {oldMinimum}개 제외 · 버튼 비용 예약 {ReservedWings}개");
                _sessionStore?.Delete();
                Progress?.Invoke(new(
                    plan.TargetQuantity, plan.TargetQuantity, QueuedWorks, plan.RequiredWorks,
                    0, null, "완료"));
                return AlteringRunResult.Completed;
            }

            int facilityCount = works.Count(x => x.FacilityName == plan.FacilityName);
            int freeSlots = Math.Max(0, 7 - facilityCount);
            if (QueuedWorks < plan.RequiredWorks && freeSlots > 0)
            {
                int batchGoal = Math.Min(freeSlots, plan.RequiredWorks - QueuedWorks);
                if (yieldAtBatchBoundary && batchRegistrationLimit.HasValue)
                    batchGoal = Math.Min(batchGoal, batchRegistrationLimit.Value);
                int queuedThisBatch = 0;
                Log?.Invoke($"[자동 가공] 일괄 등록 시작 · 빈 슬롯 {freeSlots}칸 · 이번 묶음 {batchGoal}작업 / 최대 {batchGoal * plan.ProducedPerWork}개");

                while (queuedThisBatch < batchGoal && QueuedWorks < plan.RequiredWorks)
                {
                    ct.ThrowIfCancellationRequested();

                    recipes = await _data.RecipesAsync(ct);
                    selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                    if (selected.Length != plan.RecipeCount ||
                        selected.ElementAtOrDefault(plan.RecipeOrdinal - 1)?.ProducedPerWork != plan.ProducedPerWork)
                        throw new InvalidOperationException("가공 제법 또는 생산 수량이 바뀌어 정지합니다.");

                    plan = plan with
                    {
                        VerifiedOcrAlias = AlteringText.UniqueOcrAlias(
                            plan.DisplayName, recipes.Select(x => x.DisplayName))
                    };

                    if (selected.Length < plan.RecipeOrdinal ||
                        !selected[plan.RecipeOrdinal - 1].Alterable)
                    {
                        var recipe = selected.ElementAtOrDefault(plan.RecipeOrdinal - 1);
                        if (recipe is not null &&
                            recipe.MissingIngredients.Count > 0 &&
                            _supplyResolver is not null)
                        {
                            int remainingWorks = plan.RequiredWorks - QueuedWorks;
                            string missingText = string.Join(", ",
                                recipe.MissingIngredients.Select(x =>
                                    $"{x.DisplayName} {x.Owned}/{x.Required}"));

                            SaveStage("재료 해결 · " + string.Join(", ", recipe.MissingIngredients.Select(x => x.DisplayName)));
                            await ReportProgressAsync(works, plan, baseline, initialExistingCount, "재료 확보 중", ct);
                            Log?.Invoke(
                                $"[자동 가공] 재료 부족 감지 · Reason={recipe.Reason ?? "unknown"} · {missingText} · 남은 등록 {remainingWorks}회 · 하위 재료 해결 시작");

                            await _supplyResolver.ResolveAsync(plan, recipe, remainingWorks, ct);

                            recipes = await _data.RecipesAsync(ct);
                            selected = recipes.Where(x => x.DisplayName == plan.DisplayName).ToArray();
                            var refreshed = selected.ElementAtOrDefault(plan.RecipeOrdinal - 1);
                            if (refreshed is not null && refreshed.Alterable)
                            {
                                SaveStage("가공 재개");
                                Log?.Invoke($"[자동 가공] 재료 재확인 완료 · {plan.DisplayName} 가공을 이어갑니다.");
                                continue;
                            }
                        }

                        string reason = recipe?.Reason ?? "not_found";
                        string missing = string.Join(", ",
                            recipe?.MissingIngredients.Select(x =>
                                $"{x.DisplayName} {x.Owned}/{x.Required}") ??
                            Array.Empty<string>());
                        throw new InvalidOperationException(
                            $"가공 불가: {reason} {missing}. 등록된 작업은 게임에 남습니다.");
                    }

                    works = await _data.WorksAsync(ct);
                    int currentFacilityCount = works.Count(x => x.FacilityName == plan.FacilityName);
                    if (currentFacilityCount >= 7)
                        break;

                    int previous = Matching(works, plan).Count();
                    SavePendingRegistration(previous);

                    AlteringInternalConsumptionSnapshot? consumptionBefore = null;
                    if (_internalConsumptionObserver is not null)
                        consumptionBefore = await _internalConsumptionObserver
                            .CaptureBeforeRegistrationAsync(plan, ct);

                    bool reserved = false;
                    await _screen.QueueAsync(plan, () =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (reserved || !plan.AllowPaidButton ||
                            ReservedWings + 5 > plan.MaximumWings)
                            throw new InvalidOperationException(
                                "정령의 날개 사용 시도가 0개 사용 원칙에 의해 차단되었습니다.");
                        reserved = true;
                        ReservedWings += 5;
                    }, ct);

                    await VerifyAsync(async token =>
                    {
                        int count = Matching(await _data.WorksAsync(token), plan).Count();
                        if (count > previous + 1)
                            throw new InvalidOperationException(
                                "동시에 다른 가공 작업이 등록되어 수량을 확정할 수 없습니다.");
                        return count == previous + 1;
                    }, "작업 등록을 확인하지 못했습니다. 재화 중복 사용을 막기 위해 재클릭하지 않고 정지합니다.",
                    ct, TimeSpan.FromSeconds(1));

                    if (_internalConsumptionObserver is not null && consumptionBefore is not null)
                        await _internalConsumptionObserver.CommitAfterRegistrationAsync(
                            plan, consumptionBefore, ct);

                    QueuedWorks++;
                    ConfirmedRegistrationsThisRun++;
                    queuedThisBatch++;
                    SaveConfirmedRegistration();
                    Log?.Invoke(
                        $"[자동 가공] 일괄 등록 진행 {queuedThisBatch}/{batchGoal} · 전체 {QueuedWorks}/{plan.RequiredWorks} · {plan.DisplayName}");

                    works = await _data.WorksAsync(ct);
                    await ReportProgressAsync(works, plan, baseline, initialExistingCount, "재료 정상", ct);
                }

                if (queuedThisBatch > 0)
                {
                    Log?.Invoke(
                        $"[자동 가공] 일괄 등록 완료 · {queuedThisBatch}작업 / {queuedThisBatch * plan.ProducedPerWork}개 생산 예약");

                    if (yieldAtBatchBoundary)
                    {
                        SaveStage("다중가공 배치 대기");
                        Log?.Invoke(
                            $"[자동 가공] 다중가공 배치 양보 · {plan.DisplayName} · 시설 대기열을 채운 뒤 다른 시설로 전환");
                        return AlteringRunResult.BatchQueued;
                    }
                }

                continue;
            }

            bool full = facilityCount >= 7;
            if (full && !works.Any(x => x.FacilityName == plan.FacilityName && x.State == "InProgress"))
                throw new InvalidOperationException("가공 대기열이 가득 찼지만 진행 중인 작업이 없습니다. 게임에서 작업 상태를 확인하세요.");

            var remaining = works.Where(x => x.FacilityName == plan.FacilityName && x.State == "InProgress")
                .Select(x => x.RemainingSeconds).DefaultIfEmpty(10).Min();
            SaveStage("완료 대기");
            Log?.Invoke($"[자동 가공] 완료 대기 · 등록 {QueuedWorks}/{plan.RequiredWorks} · 남은 시간 {remaining}초");

            if (yieldAtBatchBoundary)
            {
                SaveStage("다중가공 배치 대기");
                Log?.Invoke(
                    $"[자동 가공] 다중가공 배치 양보 · {plan.DisplayName} · 현재 시설 배치 전체 완료 전 재방문 없음");
                return AlteringRunResult.BatchQueued;
            }

            await _delay(TimeSpan.FromSeconds(Math.Clamp(remaining, 2, 30)), ct);
        }
    }

    internal void NoteStage(string stage) => SaveStage(stage);

    internal void CreditInternalConsumption(long quantity, string consumerDisplayName)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (_session is null || _sessionStore is null)
            throw new InvalidOperationException(
                "다중가공 내부 재료 소비를 기록할 이어하기 세션이 없습니다.");

        long credited = checked(
            _session.CreditedInternalConsumptionQuantity + quantity);
        SaveSession(_session with
        {
            CreditedInternalConsumptionQuantity = credited
        });

        Log?.Invoke(
            $"[자동 가공] 내부 재료 소비 보정 · {consumerDisplayName} 등록으로 " +
            $"{_session.DisplayName} 결과물 {quantity:N0}개 소비 인정 · 누적 {credited:N0}개");
    }

    private long EffectiveOutputQuantity(long actualQuantity)
    {
        if (_session is null)
            return actualQuantity;
        return checked(
            actualQuantity + _session.CreditedInternalConsumptionQuantity);
    }

    private void SaveStage(string stage)
    {
        if (_session is null || _sessionStore is null) return;
        SaveSession(_session with { Stage = stage });
    }

    private void SavePendingRegistration(int beforeMatchingCount)
    {
        if (_session is null || _sessionStore is null) return;
        SaveSession(_session with
        {
            QueuedWorks = QueuedWorks,
            PendingRegistration = true,
            PendingBeforeMatchingCount = beforeMatchingCount,
            Stage = "작업 등록 확인"
        });
    }

    private void SaveConfirmedRegistration()
    {
        if (_session is null || _sessionStore is null) return;
        SaveSession(_session with
        {
            QueuedWorks = QueuedWorks,
            PendingRegistration = false,
            PendingBeforeMatchingCount = 0,
            Stage = "가공 진행"
        });
    }

    private void SaveSession(AlteringSessionState state)
    {
        if (_sessionStore is null) return;
        _session = state with { UpdatedAt = DateTimeOffset.Now };
        _sessionStore.Save(_session);
    }

    private async Task ReportProgressAsync(
        IReadOnlyList<AlteringWork> works,
        AlteringPlan plan,
        long baseline,
        int initialExistingCount,
        string materialState,
        CancellationToken ct)
    {
        long current = EffectiveOutputQuantity(
            await _data.ItemCountAsync(plan.OutputName, ct));
        if (_session is not null)
        {
            if (current < _session.LastObservedOutputQuantity)
                throw new InvalidOperationException(
                    $"{plan.OutputName} 보유량이 자동 가공 중 감소했습니다: 이전 확인 {_session.LastObservedOutputQuantity:N0} / 현재 {current:N0}. " +
                    "완성품 소비로 목표 추적이 모호해져 중복 생산 없이 정지합니다.");
            if (current != _session.LastObservedOutputQuantity)
                SaveSession(_session with { LastObservedOutputQuantity = current });
        }

        if (Progress is null) return;

        long oldMinimum = checked((long)initialExistingCount * plan.ProducedPerWork);
        long confirmed = Math.Max(0, current - baseline - oldMinimum);
        confirmed = Math.Min(plan.TargetQuantity, confirmed);

        var matching = Matching(works, plan).ToArray();
        var matchingInProgress = matching
            .Where(x => x.State == "InProgress")
            .ToArray();
        long? next = matchingInProgress
            .Select(x => (long?)x.RemainingSeconds)
            .DefaultIfEmpty(null)
            .Min();
        long? batchRemaining = matchingInProgress
            .Select(x => (long?)x.RemainingSeconds)
            .DefaultIfEmpty(null)
            .Max();

        if (batchRemaining is > 0 &&
            (!_estimatedWorkSeconds.HasValue || batchRemaining > _estimatedWorkSeconds.Value))
            _estimatedWorkSeconds = batchRemaining;

        long? totalRemaining = AlteringEtaEstimator.Estimate(
            plan.RequiredWorks,
            QueuedWorks,
            matching.Length,
            batchRemaining,
            _estimatedWorkSeconds);

        Progress.Invoke(new(
            confirmed,
            plan.TargetQuantity,
            QueuedWorks,
            plan.RequiredWorks,
            works.Count(x => x.FacilityName == plan.FacilityName),
            next,
            materialState,
            batchRemaining,
            totalRemaining));
    }

    private static string ProgressSignature(
        IReadOnlyList<AlteringWork> works, AlteringPlan plan, int queuedWorks)
    {
        string queue = string.Join("|", works
            .Where(x => x.FacilityName == plan.FacilityName)
            .OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.State, StringComparer.Ordinal)
            .ThenBy(x => x.IsCompleted)
            .ThenBy(x => x.RemainingSeconds)
            .Select(x => $"{x.DisplayName}:{x.State}:{x.IsCompleted}:{x.RemainingSeconds}"));
        return $"{queuedWorks}#{queue}";
    }

    internal async Task<bool> CollectReadyBatchAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
        plan.Validate();
        var works = await _data.WorksAsync(ct);
        return await CollectIfReadyAsync(plan, works, ct);
    }

    private static IEnumerable<AlteringWork> Matching(IEnumerable<AlteringWork> works, AlteringPlan plan)
        => works.Where(x => (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName) &&
            x.FacilityName == plan.FacilityName);

    private async Task<bool> CollectIfReadyAsync(
        AlteringPlan plan, IReadOnlyList<AlteringWork> works, CancellationToken ct)
    {
        int count = works.Count(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        if (count == 0) return false;
        int totalBefore = works.Count(x => x.FacilityName == plan.FacilityName);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 완료 작업 {count}건 수령 시작");

        if (_screen is IDirectCliAlteringScreen direct)
        {
            var completed = works.First(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
            await direct.CompleteAsync(completed.DisplayName, ct);
            await VerifyAsync(async token =>
                (await _data.WorksAsync(token)).Count(x => x.FacilityName == plan.FacilityName) < totalBefore,
                "CLI 완료 작업 수령 후 대기열 감소를 확인하지 못했습니다. 중복 실행 없이 정지합니다.", ct);
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 완료 작업 수령 확인 · CLI · {completed.DisplayName}");
            return true;
        }

        bool firstCollected = await _screen.CollectAsync(plan, ct);
        if (firstCollected)
        {
            await VerifyAsync(async token =>
                (await _data.WorksAsync(token)).Count(x => x.FacilityName == plan.FacilityName) < totalBefore,
                "1차 모두 받기 후 완료 작업 수령을 확인하지 못했습니다. 반복 입력 없이 정지합니다.",
                ct, TimeSpan.FromSeconds(1));
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 완료 작업 수령 확인 · 1차 모두 받기");
            return true;
        }

        Log?.Invoke($"[자동 가공] 1차 모두 받기 = 설비 이동 · 즉시 가공대 수령 화면 대기");
        if (!await _screen.CollectAfterTravelAsync(plan, ct))
            throw new InvalidOperationException("가공대 도착 후 2차 모두 받기 화면을 확인하지 못했습니다. 추가 입력 없이 정지합니다.");

        await VerifyAsync(async token =>
            (await _data.WorksAsync(token)).Count(x => x.FacilityName == plan.FacilityName) < totalBefore,
            "2차 모두 받기 후 완료 작업 수령을 확인하지 못했습니다. 반복 클릭 없이 정지합니다.", ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 완료 작업 수령 확인 · 2차 모두 받기");
        return true;
    }

    private async Task VerifyAsync(
        Func<CancellationToken, Task<bool>> condition,
        string failure,
        CancellationToken ct,
        TimeSpan? pollDelay = null)
    {
        TimeSpan delay = pollDelay ?? TimeSpan.FromSeconds(5);
        for (int i = 0; i < _verificationAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await condition(ct)) return;
            await _delay(delay, ct);
        }
        throw new InvalidOperationException(failure);
    }
}
