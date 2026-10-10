using System.Text;
using System.Text.Json;

namespace FishingAutomation;

internal enum MultiAlteringBatchState { Active, Completed, Closed }

// The before-image and successful credits share the same authoritative batch
// manifest as every producer checkpoint. A prepared but unresolved transaction
// cannot safely be replayed after F10/crash: no automatic registration is allowed.
internal sealed record MultiAlteringConsumptionTransaction
{
    public string TransactionId { get; init; } = "";
    public string ConsumerKey { get; init; } = "";
    public Dictionary<string, long> BeforeCounts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> AppliedCredits { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record MultiAlteringBatch
{
    public int Version { get; init; } = 2;
    public MultiAlteringConsumptionTransaction? PreparedConsumption { get; init; }
    public MultiAlteringConsumptionTransaction[] AppliedConsumption { get; init; } = [];
    public string BatchId { get; init; } = "";
    public bool MigratedFromLegacy { get; init; }
    public CliIdentityContext Identity { get; init; } = new(null, null, null, null);
    public MultiAlteringBatchState State { get; init; }
    public AlteringSessionState[] Items { get; init; } = Array.Empty<AlteringSessionState>();
    public DateTimeOffset? CompletedAt { get; init; }
}

// One authoritative manifest owns the roster, registration checkpoints and final
// receipts. Item stores are views into it; there is no two-file completion commit.
// A process-wide file lease excludes concurrent app instances. No cancellation
// callback performs IO, takes this lease or waits for a game input.
internal sealed class MultiAlteringBatchStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly string _path;
    private readonly FileStream _lease;
    private readonly Action<string>? _fault;
    private MultiAlteringBatch? _batch;
    private bool _failed;
    internal bool IsResuming { get; private set; }
    internal string BatchId => Batch.BatchId;
    internal bool IsTerminal => Batch.State != MultiAlteringBatchState.Active;
    private MultiAlteringBatch Batch => _batch ?? throw new InvalidOperationException("다중가공 배치 초기화 전입니다.");

    internal MultiAlteringBatchStore(string directory, Action<string>? fault = null)
    {
        _directory = directory;
        _path = System.IO.Path.Combine(directory, "batch.json");
        _fault = fault;
        Directory.CreateDirectory(directory);
        _lease = new FileStream(System.IO.Path.Combine(directory, "batch.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    // Read-only snapshot for the explicit resume chooser. Never replaces a
    // missing or damaged manifest with a new batch.
    internal static MultiAlteringBatch ReadManifestSnapshot(string directory)
    {
        MultiAlteringBatch batch;
        try
        {
            batch = JsonSerializer.Deserialize<MultiAlteringBatch>(
                File.ReadAllText(System.IO.Path.Combine(directory, "batch.json")), JsonOptions)
                ?? throw new InvalidDataException("다중가공 배치 기록이 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("다중가공 배치 기록 손상 · 원본 보존", ex);
        }
        Validate(batch);
        return batch;
    }

    internal static IReadOnlyList<AlteringPlan> ReadPendingPlans(string directory)
    {
        MultiAlteringBatch? saved;
        try
        {
            saved = JsonSerializer.Deserialize<MultiAlteringBatch>(
                File.ReadAllText(System.IO.Path.Combine(directory, "batch.json")), JsonOptions)
                ?? throw new InvalidDataException("다중가공 배치 기록이 비어 있습니다.");
        }
        catch (FileNotFoundException)
        {
            // The pre-N02 format never persisted the full batch roster.
            // A completed peer's plan file may have been deleted, so even
            // valid remaining zero-work files cannot authorize AUTO-selection.
            // The user must explicitly select the old, still-pending plans;
            // OpenAsync will then strictly verify zero registrations, identity,
            // baseline quantities and no live facility works before migration.
            if (Directory.Exists(directory) &&
                (Directory.EnumerateFiles(directory, "*.json").Any() ||
                 Directory.EnumerateFiles(directory, "*.tmp").Any() ||
                 Directory.Exists(System.IO.Path.Combine(directory, "dependencies")) &&
                 Directory.EnumerateFiles(System.IO.Path.Combine(directory, "dependencies"), "*.json").Any()))
                throw new InvalidOperationException(
                    "구형 다중가공 기록 발견 · 과거 전체 품목 목록을 증명할 수 없어 자동 목록 복원/단일가공 전환 차단. " +
                    "기존 기록은 보존됩니다. 사용자가 미완료 품목을 다중가공 목록에 직접 선택한 뒤 " +
                    "등록 0회·대기열 0건·기준 수량 불변을 검증하는 안전 이관만 허용합니다. " +
                    "진행 이력이 있는 구형 기록은 RecoveryRequired · 자동 재등록 금지");
            return Array.Empty<AlteringPlan>();
        }
        catch (DirectoryNotFoundException) { return Array.Empty<AlteringPlan>(); }
        catch (JsonException ex) { throw new InvalidDataException("다중가공 배치 기록 손상 · 원본 보존", ex); }
        Validate(saved);
        if (saved.State == MultiAlteringBatchState.Closed) return Array.Empty<AlteringPlan>();
        return saved.Items.Select(s => new AlteringPlan(s.FacilityName, s.DisplayName,
            s.TargetQuantity, s.ProducedPerWork, false, s.RecipeOrdinal)).ToArray();
    }

    private static AlteringPlan PlanFrom(AlteringSessionState s)
        => new(s.FacilityName, s.DisplayName, s.TargetQuantity, s.ProducedPerWork,
            false, s.RecipeOrdinal);

    // Conservative v1 upgrade: every selected item must have a stable-key
    // checkpoint proving ZERO registrations. Old nonzero/incomplete/deleted
    // item histories cannot prove an absent peer was not already finished.
    // Never delete or replace an unsupported legacy checkpoint.
    private static AlteringSessionState[] ReadLegacyZeroWorkSessions(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        if (Directory.EnumerateFiles(directory, "*.tmp").Any())
            throw new InvalidOperationException(
                "미확정 임시 세션 파일이 있어 구형 배치 이관 불가 · 기록 보존 · RecoveryRequired");
        string dependencies = System.IO.Path.Combine(directory, "dependencies");
        if (Directory.Exists(dependencies) &&
            Directory.EnumerateFiles(dependencies, "*.json").Any())
            throw new InvalidOperationException(
                "진행 중인 구형 중간재료 기록이 있어 이관 불가 · 기록 보존 · RecoveryRequired");

        var paths = Directory.EnumerateFiles(directory, "*.json")
            .Where(p => !string.Equals(System.IO.Path.GetFileName(p),
                "batch.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var saved = new List<AlteringSessionState>();
        foreach (var path in paths)
        {
            if (!System.IO.Path.GetFileName(path).StartsWith("plan-", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "구형 순번/알 수 없는 세션은 전체 계획을 증명할 수 없어 이관 불가 · 기록 보존 · RecoveryRequired");
            var s = new AlteringSessionStore(path).Load()
                ?? throw new InvalidDataException("구형 가공 세션 기록을 읽지 못했습니다.");
            var p = PlanFrom(s); p.Validate();
            if (!string.Equals(path, AlteringSessionStore.MultiPlanPath(directory, p),
                    StringComparison.OrdinalIgnoreCase) ||
                s.BatchId is not null || s.MultiState != MultiAlteringItemState.NotStarted ||
                s.CompletedAt is not null || s.QueuedWorks != 0 || s.PendingRegistration ||
                s.PendingBeforeMatchingCount != 0 || s.InitialExistingWorks != 0 ||
                s.CreditedInternalConsumptionQuantity != 0 ||
                s.LastObservedOutputQuantity != s.BaselineQuantity ||
                s.Stage.Contains("완료", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "구형 가공 세션에 등록/수령/소비 또는 상태 불확실성이 있어 이관 불가 · 기록 보존 · RecoveryRequired");
            saved.Add(s);
        }
        if (saved.Select(Key).Distinct(StringComparer.Ordinal).Count() != saved.Count)
            throw new InvalidDataException("구형 가공 세션 품목 고유키 중복 · 기록 보존");
        return saved.ToArray();
    }

    // Backward-compatible entry point for the isolated V3.1.55 ledger.
    // In single-character mode the SAME durable manifest may now resume,
    // but a new/changed plan must never overwrite a pending batch.
    internal Task OpenNewLimitedTestAsync(
        IReadOnlyList<AlteringPlan> plans, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct)
        => OpenAsync(plans, identity, data, ct, allowSingleCharacter: true);

    // Explicit fresh F9 mode. Historical batches (including unresolved F05
    // transactions) live in OTHER run folders and are never deserialized,
    // replayed, reset or deleted by this entry point. The manager owns the
    // whole new job roster; do not resume anything from a past F10 run.
    internal async Task OpenFreshAsync(
        IReadOnlyList<AlteringPlan> plans, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var plan in plans) plan.Validate();
        if (plans.Count == 0 || plans.Select(Key).Distinct().Count() != plans.Count)
            throw new InvalidDataException("신규 다중가공 계획이 비었거나 중복되었습니다.");

        // An explicit, newly allocated directory may contain only the lease.
        // Existing/corrupt manifests must never turn into a fresh operation.
        if (File.Exists(_path) ||
            Directory.EnumerateFileSystemEntries(_directory)
                .Any(p => !string.Equals(System.IO.Path.GetFileName(p),
                    "batch.lock", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "새 F9 작업 폴더에 이전 기록이 존재합니다 · 신규 등록 차단 · 기록 보존");

        // F9 starts a new order at zero without changing the live queue.
        // The initial queue quantity only prevents unrelated game output
        // from satisfying this order. Old batches are never reopened.
        await OpenAsync(plans, identity, data, ct,
            allowSingleCharacter: true, allowPreexistingSelectedFacilityWorks: true);
        if (IsResuming)
            throw new InvalidOperationException(
                "F9 새 작업이 기존 배치를 복원하려 했습니다 · 안전 정지");
    }

    internal static void EnsureNoActiveVerifiedBatch(string directory)
    {
        MultiAlteringBatch? saved;
        try
        {
            saved = JsonSerializer.Deserialize<MultiAlteringBatch>(
                File.ReadAllText(System.IO.Path.Combine(directory, "batch.json")), JsonOptions)
                ?? throw new InvalidDataException("기존 다중가공 기록이 비어 있습니다.");
        }
        catch (FileNotFoundException) { return; }
        catch (JsonException ex) { throw new InvalidDataException(
            "기존 다중가공 기록 손상 · 새 테스트 차단 · 기록 보존", ex); }
        Validate(saved);
        if (saved.State != MultiAlteringBatchState.Closed)
            throw new InvalidOperationException(
                "미완료/미확정 기존 다중가공 배치가 있습니다 · 제한적 새 테스트 중복 가공 위험 · 기록 보존 · 신규 등록 차단");
    }

    internal async Task OpenAsync(IReadOnlyList<AlteringPlan> plans, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct, bool allowSingleCharacter = false,
        bool allowPreexistingSelectedFacilityWorks = false)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var plan in plans) plan.Validate();
        if (plans.Count == 0 || plans.Select(Key).Distinct().Count() != plans.Count)
            throw new InvalidDataException("다중가공 계획이 비었거나 품목 고유키가 중복됩니다.");
        // In the explicitly configured ONE-character environment a stable
        // realm-only CLI identity is sufficient to re-open the isolated
        // local N02 manifest. This is not cross-character authentication:
        // a user changing characters must NOT reuse this ledger.
        if (!identity.HasDurableMultiIdentity &&
            (!allowSingleCharacter || string.IsNullOrWhiteSpace(identity.RealmName)))
            throw new InvalidOperationException(
                "다중가공 캐릭터 식별 정보가 부족해 새 작업/이어하기를 구분할 수 없습니다. " +
                "고유 ID 또는 1캐릭터 모드의 안정된 서버 정보 필요 · 기록 보존");

        _fault?.Invoke("read");
        MultiAlteringBatch? saved = null;
        // File.Exists swallows access errors. Read directly and distinguish only
        // genuine absence; damaged/inaccessible records never mean a fresh batch.
        try
        {
            saved = JsonSerializer.Deserialize<MultiAlteringBatch>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException("다중가공 배치 기록이 비어 있습니다.");
        }
        catch (FileNotFoundException) { }
        catch (JsonException ex) { throw new InvalidDataException("다중가공 배치 기록 손상 · 원본 보존 · 안전 정지", ex); }

        if (saved is not null)
        {
            Validate(saved);
            if (saved.State != MultiAlteringBatchState.Closed)
            {
                if (saved.Identity != identity || !SamePlans(saved, plans))
                    throw new InvalidOperationException(
                        $"다중가공 저장 충돌 · 배치 {saved.BatchId} · 캐릭터/계정/서버 또는 시설/품목/제법/생산량/목표 불일치. " +
                        $"저장: {Describe(saved.Items)} / 선택: {string.Join("; ", plans.Select(p => $"{p.FacilityName}/{p.DisplayName}#{p.RecipeOrdinal} 목표={p.TargetQuantity} 생산={p.ProducedPerWork}"))}. " +
                        "기존 기록을 보존하고 신규 등록 없이 정지합니다.");
                // A v1 manifest did not journal the registration/consumption
                // boundary. Never silently upgrade a partially registered batch.
                if (saved.Version == 1 && saved.State == MultiAlteringBatchState.Active)
                {
                    if (saved.Items.Any(x => x.QueuedWorks != 0 ||
                        x.PendingRegistration || x.InitialExistingWorks != 0 ||
                        x.CreditedInternalConsumptionQuantity != 0 ||
                        x.LastObservedOutputQuantity != x.BaselineQuantity) ||
                        (await data.WorksAsync(ct)).Any(w =>
                            plans.Any(p => p.FacilityName == w.FacilityName)))
                        throw new InvalidOperationException(
                            "F05 이전 배치에 등록/소비 이력이 있어 거래형 소비 장부로 안전 이관 불가 · RecoveryRequired · 원본 보존");
                    foreach (var plan in plans)
                        if (await data.ItemCountAsync(plan.OutputName, ct) !=
                            saved.Items.Single(x => x.MatchesPlan(plan)).BaselineQuantity)
                            throw new InvalidOperationException(
                                "F05 이전 배치 기준 수량 변동 · 거래 이력 불확실 · RecoveryRequired · 신규 등록 차단");
                    ct.ThrowIfCancellationRequested();
                    _batch = saved;
                    Commit(saved with { Version = 2 });
                    saved = Batch;
                }
                if (saved.PreparedConsumption is not null)
                    throw new InvalidOperationException(
                        "F05 미확정 소비 거래가 있습니다 · RecoveryRequired · 중복 등록/보정 없이 안전 정지");
                if (saved.Items.Any(x => x.PendingRegistration &&
                    x.PendingConsumptionTransactionId is not null &&
                    saved.AppliedConsumption.Any(t =>
                        t.ConsumerKey == Key(x) && t.TransactionId == x.PendingConsumptionTransactionId)))
                    throw new InvalidOperationException(
                        "F05 소비 보정 저장 후 등록 확정 전 중단 · RecoveryRequired · 재등록/이중 보정 차단");
                // Recursive child session is a separate legacy checkpoint; if a
                // durable parent credit was applied but child confirmation was
                // interrupted, it must not be automatically retried.
                string childDir = System.IO.Path.Combine(_directory, "dependencies");
                if (Directory.Exists(childDir))
                    foreach (var file in Directory.EnumerateFiles(childDir, "plan-*.json"))
                    {
                        var child = new AlteringSessionStore(file).Load();
                        if (child is not null && child.PendingRegistration &&
                            child.PendingConsumptionTransactionId is not null &&
                            saved.AppliedConsumption.Any(t =>
                                t.ConsumerKey == Key(child) &&
                                t.TransactionId == child.PendingConsumptionTransactionId))
                            throw new InvalidOperationException(
                                "F05 중간재료 등록 확정 전 종료 · RecoveryRequired · 자식 작업 중복 등록 차단");
                    }
                if (saved.Items.Any(x => x.MultiState == MultiAlteringItemState.RecoveryRequired))
                    throw new InvalidOperationException(
                        $"다중가공 배치 {saved.BatchId} 수령 확정 중 중단 · RecoveryRequired · 기록 보존 · 신규 등록 없이 안전 정지");
                if (allowSingleCharacter)
                {
                    // An unjournaled pre-existing job at the selected facility
                    // cannot be attributed to this one-character checkpoint.
                    var live = await data.WorksAsync(ct);
                    foreach (string facility in plans.Select(x => x.FacilityName).Distinct())
                    {
                        var recorded = saved.Items.Where(x => x.FacilityName == facility).ToArray();
                        var visible = live.Where(x => x.FacilityName == facility).ToArray();
                        if (visible.Length > recorded.Sum(x => x.QueuedWorks) ||
                            visible.Any(w => !recorded.Any(x =>
                                x.DisplayName == w.DisplayName ||
                                PlanFrom(x).OutputName == w.DisplayName)))
                            throw new InvalidOperationException(
                                "1캐릭터 이어하기: 선택 시설 대기열이 저장된 등록 기록과 다릅니다 · " +
                                "타 작업 수령/재등록 차단 · 배치 기록 보존");
                    }
                }
                IsResuming = true;
                _batch = saved;
                return;
            }
        }

        var legacy = ReadLegacyZeroWorkSessions(_directory);
        var works = await data.WorksAsync(ct);
        // The 1-character unbound-identity mode owns only the SELECTED
        // facilities. Unrelated facilities are not touched or collected.
        if (allowSingleCharacter && !allowPreexistingSelectedFacilityWorks && works.Any(w =>
                plans.Any(p => p.FacilityName == w.FacilityName)))
            throw new InvalidOperationException(
                "1캐릭터 신규 가공: 선택한 시설에 기존 대기 작업이 있습니다 · " +
                "관계없는 시설 대기열은 허용 · 선택 시설 등록/수령 차단 · 기록 보존");
        string batchId = Guid.NewGuid().ToString("N");
        var items = new List<AlteringSessionState>();
        if (legacy.Length > 0)
        {
            if (legacy.Length != plans.Count || plans.Any(p => !legacy.Any(s => s.MatchesPlan(p))) ||
                legacy.Any(s => new CliIdentityContext(
                    s.CharacterId, s.CharacterName, s.AccountCode, s.RealmName) != identity) ||
                works.Any(w => plans.Any(p => p.FacilityName == w.FacilityName)))
                throw new InvalidOperationException(
                    "구형 배치 계획/캐릭터 또는 실제 시설 대기열 불일치 · 기록 보존 · RecoveryRequired");

            foreach (var plan in plans)
            {
                var old = legacy.Single(s => s.MatchesPlan(plan));
                long current = await data.ItemCountAsync(plan.OutputName, ct);
                if (current != old.BaselineQuantity)
                    throw new InvalidOperationException(
                        "구형 배치 보유 수량 변화 감지 · 완료/소비 여부 불확실 · 기록 보존 · RecoveryRequired");
                items.Add(old with { BatchId = batchId,
                    Stage = "구형 무등록 기록 검증 후 이관" });
            }
        }
        else
        {
            foreach (var plan in plans)
            {
                long baseline = await data.ItemCountAsync(plan.OutputName, ct);
                int existing = works.Count(x => x.FacilityName == plan.FacilityName &&
                    (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
                items.Add(AlteringSessionState.Create(plan, identity, baseline, existing) with { BatchId = batchId });
            }
        }
        // Never credit old jobs towards the new target. Also ensure that
        // another actor did not add/remove jobs while we read inventory
        // baselines: a changed queue cannot be safely attributed to this F9.
        if (allowPreexistingSelectedFacilityWorks)
        {
            var latest = await data.WorksAsync(ct);
            foreach (string facility in plans.Select(p => p.FacilityName).Distinct())
            {
                static string QueueShape(IEnumerable<AlteringWork> list) =>
                    string.Join("|", list.GroupBy(w => w.DisplayName, StringComparer.Ordinal)
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => g.Key + ":" + g.Count()));
                if (QueueShape(works.Where(w => w.FacilityName == facility)) !=
                    QueueShape(latest.Where(w => w.FacilityName == facility)))
                    throw new InvalidOperationException(
                        "F9 신규 작업 기준 수량 확인 중 기존 대기열이 변했습니다 · " +
                        "과거 작업을 새 수량으로 오인하지 않도록 등록 없이 정지");
            }
        }
        else if (allowSingleCharacter && (await data.WorksAsync(ct)).Any(w =>
                plans.Any(p => p.FacilityName == w.FacilityName)))
            throw new InvalidOperationException(
                "1캐릭터 신규 가공 준비 도중 선택 시설 대기열이 변했습니다 · " +
                "기록을 새로 만들지 않고 정지합니다");
        ct.ThrowIfCancellationRequested();
        Commit(new MultiAlteringBatch { BatchId = batchId, Identity = identity,
            MigratedFromLegacy = legacy.Length > 0, Items = items.ToArray() });
    }

    internal static string Key(AlteringPlan p) => $"{p.FacilityName}\u001f{p.DisplayName}\u001f{p.RecipeOrdinal}";
    private static string Key(AlteringSessionState s) => $"{s.FacilityName}\u001f{s.DisplayName}\u001f{s.RecipeOrdinal}";
    private static string Describe(IEnumerable<AlteringSessionState> items)
        => string.Join("; ", items.Select(s => $"{s.FacilityName}/{s.DisplayName}#{s.RecipeOrdinal} 목표={s.TargetQuantity} 생산={s.ProducedPerWork}"));
    private static bool SamePlans(MultiAlteringBatch b, IReadOnlyList<AlteringPlan> plans)
        => b.Items.Length == plans.Count && plans.All(p => b.Items.Any(s => s.MatchesPlan(p)));

    private static void Validate(MultiAlteringBatch b)
    {
        if (b.Version is not (1 or 2) || !Guid.TryParseExact(b.BatchId, "N", out _) ||
            !Enum.IsDefined(b.State) || b.Items.Length == 0 ||
            b.Items.Select(Key).Distinct().Count() != b.Items.Length)
            throw new InvalidDataException("다중가공 배치 식별/상태 손상 · 기록 보존");
        foreach (var s in b.Items)
        {
            var p = new AlteringPlan(s.FacilityName, s.DisplayName, s.TargetQuantity, s.ProducedPerWork, false, s.RecipeOrdinal);
            p.Validate();
            if (s.BatchId != b.BatchId || s.Version != 1 || !s.MatchesPlan(p) ||
                new CliIdentityContext(s.CharacterId, s.CharacterName, s.AccountCode, s.RealmName) != b.Identity ||
                !Enum.IsDefined(s.MultiState) || s.QueuedWorks < 0 || s.QueuedWorks > s.RequiredWorks ||
                s.BaselineQuantity < 0 || s.LastObservedOutputQuantity < s.BaselineQuantity ||
                s.CreditedInternalConsumptionQuantity < 0 || s.InitialExistingWorks < 0 || s.PendingBeforeMatchingCount < 0 ||
                (s.PendingConsumptionTransactionId is not null &&
                    (!s.PendingRegistration ||
                     !Guid.TryParseExact(s.PendingConsumptionTransactionId, "N", out _))) ||
                s.MultiState == MultiAlteringItemState.Completed &&
                (s.CompletedAt is null || s.QueuedWorks != s.RequiredWorks || s.PendingRegistration ||
                 s.LastObservedOutputQuantity - s.BaselineQuantity - s.InitialExistingMinimum < p.ExpectedQuantity))
                throw new InvalidDataException("다중가공 품목 기록 불일치/손상 · 기록 보존");
        }
        if (b.State != MultiAlteringBatchState.Active &&
            (b.CompletedAt is null || b.Items.Any(s => s.MultiState != MultiAlteringItemState.Completed)))
            throw new InvalidDataException("다중가공 전체 완료 증거 부족 · 기록 보존");
        if (b.Version == 1 &&
            (b.PreparedConsumption is not null || b.AppliedConsumption.Length != 0))
            throw new InvalidDataException("F05 구형 소비 기록 구조 손상 · 안전 정지");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tx in b.AppliedConsumption.Concat(
            b.PreparedConsumption is null ? Array.Empty<MultiAlteringConsumptionTransaction>() :
            new[] { b.PreparedConsumption }))
        {
            if (!Guid.TryParseExact(tx.TransactionId, "N", out _) ||
                string.IsNullOrWhiteSpace(tx.ConsumerKey) || !ids.Add(tx.TransactionId) ||
                tx.BeforeCounts is null || tx.AppliedCredits is null ||
                tx.BeforeCounts.Any(kv => kv.Key.Length == 0 || kv.Value < 0) ||
                tx.AppliedCredits.Any(kv => kv.Value <= 0 ||
                    !tx.BeforeCounts.ContainsKey(kv.Key) ||
                    tx.BeforeCounts[kv.Key] < kv.Value) ||
                b.PreparedConsumption == tx && tx.AppliedCredits.Count > 0)
                throw new InvalidDataException("F05 소비 거래 기록 손상 · 안전 정지");
        }
        if (b.State != MultiAlteringBatchState.Active && b.PreparedConsumption is not null)
            throw new InvalidDataException("F05 미확정 소비 거래가 남은 배치 종료 차단");
    }

    internal AlteringSessionState Session(AlteringPlan plan)
        => Batch.Items.Single(s => s.MatchesPlan(plan));
    internal AlteringSessionStore PlanStore(AlteringPlan plan) => new ItemStore(this, plan);
    internal IReadOnlyList<AlteringPlan> CompletedPlans(IReadOnlyList<AlteringPlan> plans)
    {
        if (!SamePlans(Batch, plans)) throw new InvalidOperationException("배치 계획 불일치 · 안전 정지");
        return plans.Where(p => Session(p).MultiState == MultiAlteringItemState.Completed).ToArray();
    }

    private void SaveItem(AlteringPlan plan, AlteringSessionState state)
    {
        var previous = Session(plan);
        if (Batch.State != MultiAlteringBatchState.Active || state.BatchId != Batch.BatchId ||
            !state.MatchesPlan(plan) || previous.MultiState == MultiAlteringItemState.Completed && state != previous)
            throw new InvalidOperationException("다중가공 완료/배치 기록 변경 차단");
        // A producer credit may have been atomically applied by a recursive
        // consumer while this item's automation still holds an older copy.
        // Saving its stage must never roll back those durable credits.
        if (state.CreditedInternalConsumptionQuantity <
            previous.CreditedInternalConsumptionQuantity)
            state = state with {
                CreditedInternalConsumptionQuantity = previous.CreditedInternalConsumptionQuantity
            };
        Commit(Batch with { Items = Batch.Items.Select(s => Key(s) == Key(plan) ? state : s).ToArray() });
    }

    internal void PrepareConsumption(
        AlteringPlan consumer, string transactionId,
        IReadOnlyDictionary<string, long> before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Batch.Version != 2 || Batch.State != MultiAlteringBatchState.Active ||
            Batch.PreparedConsumption is not null ||
            !Guid.TryParseExact(transactionId, "N", out _))
            throw new InvalidOperationException(
                "F05 미확정 거래/구버전 배치 · 신규 입력 없이 안전 정지");
        if (before.Count == 0) return;
        foreach (var (output, count) in before)
        {
            if (count < 0 || !Batch.Items.Any(s =>
                s.MultiState != MultiAlteringItemState.Completed &&
                PlanFrom(s).OutputName == output))
                throw new InvalidOperationException(
                    "F05 소비 대상 생산자 불일치 · 기록 보존 · 입력 차단");
        }
        Commit(Batch with { PreparedConsumption = new()
        {
            TransactionId = transactionId,
            ConsumerKey = Key(consumer),
            BeforeCounts = before.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        } });
        ct.ThrowIfCancellationRequested();
    }

    internal bool ApplyConsumption(
        AlteringPlan consumer, string transactionId,
        IReadOnlyDictionary<string, long> after, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Batch.AppliedConsumption.Any(t => t.TransactionId == transactionId))
            return false; // Retried commit is idempotent, not additive.
        var prepared = Batch.PreparedConsumption;
        if (Batch.Version != 2 || prepared is null ||
            prepared.TransactionId != transactionId ||
            prepared.ConsumerKey != Key(consumer) ||
            after.Count != prepared.BeforeCounts.Count ||
            after.Keys.Any(k => !prepared.BeforeCounts.ContainsKey(k)))
            throw new InvalidOperationException(
                "F05 소비 거래 ID/스냅샷 불일치 · 이중 보정 차단");
        var credits = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (name, initial) in prepared.BeforeCounts)
        {
            if (!after.TryGetValue(name, out long current) || current < 0)
                throw new InvalidDataException("F05 소비 후 보유량 불확실 · 거래 유지");
            if (current < initial) credits[name] = checked(initial - current);
        }
        var items = Batch.Items.ToArray();
        foreach (var (output, amount) in credits)
        {
            int index = Array.FindIndex(items, x =>
                x.MultiState != MultiAlteringItemState.Completed &&
                PlanFrom(x).OutputName == output);
            if (index < 0)
                throw new InvalidOperationException("F05 소비 생산자 사라짐 · 거래 유지");
            items[index] = items[index] with
            {
                CreditedInternalConsumptionQuantity =
                    checked(items[index].CreditedInternalConsumptionQuantity + amount)
            };
        }
        ct.ThrowIfCancellationRequested();
        // Producer credits + transaction identity + final after-image are one
        // atomic manifest replacement. No callback can double-credit on replay.
        Commit(Batch with { Items = items, PreparedConsumption = null,
            AppliedConsumption = [.. Batch.AppliedConsumption,
                prepared with { AppliedCredits = credits }] });
        return true;
    }

    internal Task BeginReceiptAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Batch.PreparedConsumption is not null)
            throw new InvalidOperationException("F05 미확정 등록 소비 거래 · 수령 차단");
        Commit(Batch with { Items = Batch.Items.Select(s =>
            s.FacilityName == plan.FacilityName && s.MultiState != MultiAlteringItemState.Completed
                ? s with { MultiState = MultiAlteringItemState.RecoveryRequired, Stage = "수령 확정 대기" } : s).ToArray() });
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal async Task ConfirmReceiptAsync(AlteringPlan plan, IAlteringData data, CancellationToken ct)
    {
        // A facility receipt may finish a peer plan, even inside a dependency
        // collector. Persist all main producers BEFORE any material can consume it.
        var works = await data.WorksAsync(ct);
        if (works.Any(w => w.FacilityName == plan.FacilityName))
            throw new InvalidOperationException("수령 후 시설 작업이 남아 완료 기록 확정 차단");
        var items = Batch.Items.ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            var s = items[i];
            if (s.FacilityName != plan.FacilityName || s.MultiState == MultiAlteringItemState.Completed) continue;
            var p = new AlteringPlan(s.FacilityName, s.DisplayName, s.TargetQuantity, s.ProducedPerWork, false, s.RecipeOrdinal);
            long current = checked(await data.ItemCountAsync(p.OutputName, ct) + s.CreditedInternalConsumptionQuantity);
            if (current < s.LastObservedOutputQuantity)
                throw new InvalidOperationException("수령 수량 감소 · 완료 증거 불확실 · RecoveryRequired 유지");
            long gain = current - s.BaselineQuantity - s.InitialExistingMinimum;
            bool complete = s.QueuedWorks == s.RequiredWorks && !s.PendingRegistration && gain >= p.ExpectedQuantity;
            if (s.QueuedWorks == s.RequiredWorks && !complete)
                throw new InvalidOperationException("전량 등록 품목의 수령 증거 부족 · RecoveryRequired 유지");
            items[i] = s with { LastObservedOutputQuantity = current,
                MultiState = complete ? MultiAlteringItemState.Completed : MultiAlteringItemState.InProgress,
                CompletedAt = complete ? DateTimeOffset.UtcNow : null, Stage = complete ? "완료" : "가공 진행" };
        }
        ct.ThrowIfCancellationRequested();
        Commit(Batch with { Items = items });
    }

    internal void Complete()
    {
        if (Batch.PreparedConsumption is not null)
            throw new InvalidOperationException("F05 미확정 소비 거래 · 배치 종료 차단");
        if (Batch.Items.Any(s => s.MultiState != MultiAlteringItemState.Completed))
            throw new InvalidOperationException("전체 품목 완료 증거 부족 · 배치 종료 차단");
        if (Batch.State == MultiAlteringBatchState.Active)
            Commit(Batch with { State = MultiAlteringBatchState.Completed, CompletedAt = DateTimeOffset.UtcNow });
    }

    internal void Cleanup()
    {
        if (Batch.State == MultiAlteringBatchState.Active) throw new InvalidOperationException("진행 배치 정리 금지");
        // The terminal manifest remains authoritative through any partial cleanup.
        // Only files owned by this batch's recursive scheduler are eligible here.
        string dependencies = System.IO.Path.Combine(_directory, "dependencies");
        if (Directory.Exists(dependencies))
            foreach (string path in Directory.EnumerateFiles(dependencies))
            { _fault?.Invoke("cleanup"); File.Delete(path); }
        if (Batch.MigratedFromLegacy)
        {
            // Preserve original v1 files as archived evidence, never discard.
            // Retrying this after a crash is safe; already moved files are absent.
            string archive = System.IO.Path.Combine(_directory, "legacy-archive", Batch.BatchId);
            foreach (var p in Directory.EnumerateFiles(_directory, "plan-*.json"))
            {
                var old = new AlteringSessionStore(p).Load()
                    ?? throw new InvalidDataException("구형 세션 보관 실패");
                if (!Batch.Items.Any(s => s.MatchesPlan(PlanFrom(old))))
                    throw new InvalidOperationException("배치 외부 구형 세션 발견 · 보관 중단");
                Directory.CreateDirectory(archive);
                _fault?.Invoke("archive");
                File.Move(p, System.IO.Path.Combine(archive, System.IO.Path.GetFileName(p)));
            }
        }
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    // Called only AFTER UI plan clearing. A Completed manifest after a crash must
    // first be consumed as completion, never interpreted as a new equal plan.
    internal void AcknowledgeClearedPlan()
    {
        if (Batch.State == MultiAlteringBatchState.Active) throw new InvalidOperationException("진행 배치 종료 금지");
        Commit(Batch with { State = MultiAlteringBatchState.Closed });
    }

    private void Commit(MultiAlteringBatch next)
    {
        if (_failed) throw new IOException("이 실행의 다중가공 저장 실패 · 추가 입력/저장 차단");
        Validate(next);
        string temp = _path + ".tmp";
        try
        {
            _fault?.Invoke("write");
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next, JsonOptions));
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            _fault?.Invoke("replace");
            File.Move(temp, _path, overwrite: true);
            _batch = next; // publish only after durable write + successful rename
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _failed = true; throw new IOException("다중가공 원자 저장 실패 · 기존 기록 보존 · 신규 등록 없이 정지: " + ex.Message, ex); }
    }

    public void Dispose() => _lease.Dispose();

    private sealed class ItemStore(MultiAlteringBatchStore owner, AlteringPlan plan)
        : AlteringSessionStore(AlteringSessionStore.MultiPlanPath(owner._directory, plan))
    {
        internal override AlteringSessionState? Load() => owner.Session(plan);
        internal override void Save(AlteringSessionState state) => owner.SaveItem(plan, state);
        internal override void Delete() => throw new InvalidOperationException("다중가공 개별 완료 기록 삭제 금지");
    }
}
