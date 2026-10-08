using System.Text;
using System.Text.Json;

namespace FishingAutomation;

internal enum MultiAlteringBatchState { Active, Completed, Closed }

internal sealed record MultiAlteringBatch
{
    public int Version { get; init; } = 1;
    public string BatchId { get; init; } = "";
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

    internal static IReadOnlyList<AlteringPlan> ReadPendingPlans(string directory)
    {
        MultiAlteringBatch? saved;
        try
        {
            saved = JsonSerializer.Deserialize<MultiAlteringBatch>(
                File.ReadAllText(System.IO.Path.Combine(directory, "batch.json")), JsonOptions)
                ?? throw new InvalidDataException("다중가공 배치 기록이 비어 있습니다.");
        }
        catch (FileNotFoundException) { return Array.Empty<AlteringPlan>(); }
        catch (DirectoryNotFoundException) { return Array.Empty<AlteringPlan>(); }
        catch (JsonException ex) { throw new InvalidDataException("다중가공 배치 기록 손상 · 원본 보존", ex); }
        Validate(saved);
        if (saved.State == MultiAlteringBatchState.Closed) return Array.Empty<AlteringPlan>();
        return saved.Items.Select(s => new AlteringPlan(s.FacilityName, s.DisplayName,
            s.TargetQuantity, s.ProducedPerWork, false, s.RecipeOrdinal)).ToArray();
    }

    internal async Task OpenAsync(IReadOnlyList<AlteringPlan> plans, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var plan in plans) plan.Validate();
        if (plans.Count == 0 || plans.Select(Key).Distinct().Count() != plans.Count)
            throw new InvalidDataException("다중가공 계획이 비었거나 품목 고유키가 중복됩니다.");
        if (string.IsNullOrWhiteSpace(identity.CharacterId) &&
            (string.IsNullOrWhiteSpace(identity.CharacterName) || string.IsNullOrWhiteSpace(identity.RealmName)))
            throw new InvalidOperationException("다중가공 캐릭터 식별 정보가 부족해 새 작업/이어하기를 구분할 수 없습니다.");

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
                if (saved.Items.Any(x => x.MultiState == MultiAlteringItemState.RecoveryRequired))
                    throw new InvalidOperationException(
                        $"다중가공 배치 {saved.BatchId} 수령 확정 중 중단 · RecoveryRequired · 기록 보존 · 신규 등록 없이 안전 정지");
                IsResuming = true;
                _batch = saved;
                return;
            }
        }

        var legacyPaths = Directory.EnumerateFiles(_directory, "*.json")
            .Where(p => p != _path).ToArray();
        foreach (string path in legacyPaths)
        {
            if (System.IO.Path.GetFileName(path).StartsWith("plan-", StringComparison.Ordinal) ||
                AlteringSessionStore.IsLegacyMultiPath(path))
                _ = new AlteringSessionStore(path).Load(); // Validate v1; never delete it.
        }
        if (legacyPaths.Length > 0 || Directory.EnumerateFiles(_directory, "*.tmp").Any() ||
            Directory.Exists(System.IO.Path.Combine(_directory, "dependencies")) &&
            Directory.EnumerateFiles(System.IO.Path.Combine(_directory, "dependencies"), "*.json").Any())
            throw new InvalidOperationException(
                "기존 다중가공 이어하기 기록은 읽었지만 배치 전체 완료 이력을 확정할 수 없습니다. " +
                "구형 기록/미확정 임시 파일 보존 · RecoveryRequired · 신규 등록 없이 안전 정지");

        string batchId = Guid.NewGuid().ToString("N");
        var works = await data.WorksAsync(ct);
        var items = new List<AlteringSessionState>();
        foreach (var plan in plans)
        {
            long baseline = await data.ItemCountAsync(plan.OutputName, ct);
            int existing = works.Count(x => x.FacilityName == plan.FacilityName &&
                (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
            items.Add(AlteringSessionState.Create(plan, identity, baseline, existing) with { BatchId = batchId });
        }
        ct.ThrowIfCancellationRequested();
        Commit(new MultiAlteringBatch { BatchId = batchId, Identity = identity, Items = items.ToArray() });
    }

    internal static string Key(AlteringPlan p) => $"{p.FacilityName}\u001f{p.DisplayName}\u001f{p.RecipeOrdinal}";
    private static string Key(AlteringSessionState s) => $"{s.FacilityName}\u001f{s.DisplayName}\u001f{s.RecipeOrdinal}";
    private static string Describe(IEnumerable<AlteringSessionState> items)
        => string.Join("; ", items.Select(s => $"{s.FacilityName}/{s.DisplayName}#{s.RecipeOrdinal} 목표={s.TargetQuantity} 생산={s.ProducedPerWork}"));
    private static bool SamePlans(MultiAlteringBatch b, IReadOnlyList<AlteringPlan> plans)
        => b.Items.Length == plans.Count && plans.All(p => b.Items.Any(s => s.MatchesPlan(p)));

    private static void Validate(MultiAlteringBatch b)
    {
        if (b.Version != 1 || !Guid.TryParseExact(b.BatchId, "N", out _) ||
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
                s.MultiState == MultiAlteringItemState.Completed &&
                (s.CompletedAt is null || s.QueuedWorks != s.RequiredWorks || s.PendingRegistration ||
                 s.LastObservedOutputQuantity - s.BaselineQuantity - s.InitialExistingMinimum < p.ExpectedQuantity))
                throw new InvalidDataException("다중가공 품목 기록 불일치/손상 · 기록 보존");
        }
        if (b.State != MultiAlteringBatchState.Active &&
            (b.CompletedAt is null || b.Items.Any(s => s.MultiState != MultiAlteringItemState.Completed)))
            throw new InvalidDataException("다중가공 전체 완료 증거 부족 · 기록 보존");
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
        Commit(Batch with { Items = Batch.Items.Select(s => Key(s) == Key(plan) ? state : s).ToArray() });
    }

    internal Task BeginReceiptAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
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
