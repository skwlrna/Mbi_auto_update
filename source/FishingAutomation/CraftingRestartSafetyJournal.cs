using System.Text;
using System.Text.Json;

namespace FishingAutomation;

// Read-before-write journal: an interrupted F9 craft run is NOT an authorization
// to repeat the original target after a new process starts. No game input here.
internal sealed record CraftingPendingBatch
{
    public int PlanIndex { get; init; }
    public string DisplayName { get; init; } = "";
    public int Attempts { get; init; }
    public long InventoryBefore { get; init; }
    public long ExpectedIncrease { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

internal sealed record CraftingRestartSnapshot
{
    public int Version { get; init; } = 1;
    public string RunId { get; init; } = "";
    public CliIdentityContext Identity { get; init; } = new(null, null, null, null);
    public CraftingPlan[] Plans { get; init; } = [];
    public int[] CompletedIndices { get; init; } = [];
    public int[] SkippedIndices { get; init; } = [];
    public CraftingPendingBatch? PendingBatch { get; init; }
    public int LastPlanIndex { get; init; } = -1;
    public long LastConfirmedIncrease { get; init; }
    public bool Completed { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// A single lease covers the entire crafting run (including F10). The manifest
// remains if F10, process termination or an uncertain click interrupts it.
// The next F9 run needs an explicit human "NEW target, not resume" decision.
// No old quantities or clicks are ever restored into the new execution engine.
internal sealed class CraftingRestartSafetyJournal : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly string _path;
    private readonly string _tmpPath;
    private readonly FileStream _lease;
    private CraftingRestartSnapshot? _snapshot;
    private bool _disposed;

    internal CraftingRestartSafetyJournal(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "restart-safety.json");
        _tmpPath = _path + ".tmp";
        Directory.CreateDirectory(directory);
        _lease = new FileStream(Path.Combine(directory, "restart-safety.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(_tmpPath))
                throw new InvalidDataException(
                    "제작 임시 기록이 남아 있습니다 · 기록을 덮어쓰지 않고 안전 정지 · 수동 확인 필요");
            if (File.Exists(_path))
                _snapshot = ReadStrict(_path);
        }
        catch
        {
            _lease.Dispose();
            throw;
        }
    }

    internal CraftingRestartSnapshot? ReviewRequired
        => _snapshot is { Completed: false } snapshot ? snapshot : null;

    private static CraftingRestartSnapshot ReadStrict(string path)
    {
        CraftingRestartSnapshot saved;
        try
        {
            saved = JsonSerializer.Deserialize<CraftingRestartSnapshot>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("제작 재시작 기록이 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "제작 재시작 기록이 손상되었습니다 · 자동 신규 실행 금지 · 원본 보존", ex);
        }
        Validate(saved);
        return saved;
    }

    private static void Validate(CraftingRestartSnapshot s)
    {
        if (s.Version != 1 || !Guid.TryParseExact(s.RunId, "N", out _) ||
            s.Identity is null || s.Plans is null || s.Plans.Length == 0 ||
            s.CompletedIndices is null || s.SkippedIndices is null ||
            s.StartedAt == default || s.UpdatedAt == default ||
            s.LastPlanIndex < -1 || s.LastPlanIndex >= s.Plans.Length ||
            s.LastConfirmedIncrease < 0)
            throw new InvalidDataException(
                "제작 재시작 기록 구조가 올바르지 않습니다 · 기존 기록 보존 · 수동 확인 필요");
        foreach (var plan in s.Plans) plan.Validate();
        var all = s.CompletedIndices.Concat(s.SkippedIndices).ToArray();
        if (all.Distinct().Count() != all.Length ||
            all.Any(i => i < 0 || i >= s.Plans.Length))
            throw new InvalidDataException("제작 재시작 기록에 중복 또는 범위 밖 품목이 있습니다.");
        if (s.PendingBatch is { } pending &&
            (s.Completed || pending.PlanIndex < 0 ||
             pending.PlanIndex >= s.Plans.Length ||
             pending.PlanIndex != s.LastPlanIndex ||
             pending.DisplayName != s.Plans[pending.PlanIndex].DisplayName ||
             pending.Attempts is < 1 or > 10 ||
             pending.InventoryBefore < 0 || pending.ExpectedIncrease <= 0 ||
             pending.StartedAt == default || all.Contains(pending.PlanIndex)))
            throw new InvalidDataException("미확정 제작 배치 기록이 서로 일치하지 않습니다.");
        if (s.Completed && (all.Length != s.Plans.Length ||
                            s.SkippedIndices.Length != 0 ||
                            s.PendingBatch is not null))
            throw new InvalidDataException("제작 완료 기록과 미처리 품목이 일치하지 않습니다.");
    }

    internal void StartFresh(
        IReadOnlyList<CraftingPlan> plans,
        CliIdentityContext identity,
        string? acknowledgedOldRunId = null)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(identity);
        if (plans.Count == 0) throw new InvalidDataException("제작 대상이 비어 있습니다.");
        foreach (var plan in plans) plan.Validate();

        if (_snapshot is { Completed: false } old &&
            !string.Equals(acknowledgedOldRunId, old.RunId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "이전 제작의 미완료/미확정 기록이 남아 있습니다 · " +
                "자동 이어하기/동일 목표 재실행 차단 · 수동 재고/퀘스트 확인 후 새 작업만 허용");
        if (_snapshot is { Completed: true } &&
            acknowledgedOldRunId is not null)
            throw new InvalidOperationException("완료된 기록에는 이전 작업 수동 승인 토큰을 사용할 수 없습니다.");
        if (_snapshot is null && acknowledgedOldRunId is not null)
            throw new InvalidOperationException("없는 이전 제작 기록의 수동 승인을 사용할 수 없습니다.");
        if (File.Exists(_tmpPath))
            throw new InvalidDataException("제작 기록 임시 파일 발견 · 자동 진행 금지");

        // Copy the previous manifest BEFORE overwriting it. If archiving fails,
        // no new run may start and the old manifest is left untouched.
        if (_snapshot is { } prior)
        {
            string archive = Path.Combine(_directory, "reviewed");
            Directory.CreateDirectory(archive);
            File.Copy(_path, Path.Combine(archive, prior.RunId + ".json"), overwrite: false);
        }

        var now = DateTimeOffset.UtcNow;
        Save(new CraftingRestartSnapshot
        {
            RunId = Guid.NewGuid().ToString("N"),
            Identity = identity,
            Plans = plans.ToArray(),
            StartedAt = now,
            UpdatedAt = now
        });
    }

    internal void BeginBatch(
        int planIndex, CraftingPlan plan, int craftCount,
        long inventoryBefore, long expectedIncrease)
    {
        var current = RequireActive(planIndex, plan);
        if (current.PendingBatch is not null ||
            current.CompletedIndices.Contains(planIndex) ||
            current.SkippedIndices.Contains(planIndex) ||
            craftCount is < 1 or > 10 || inventoryBefore < 0 || expectedIncrease <= 0)
            throw new InvalidOperationException(
                "제작 배치 이전 기록이 확정되지 않아 추가 클릭을 차단합니다.");
        Save(current with
        {
            LastPlanIndex = planIndex,
            PendingBatch = new CraftingPendingBatch
            {
                PlanIndex = planIndex,
                DisplayName = plan.DisplayName,
                Attempts = craftCount,
                InventoryBefore = inventoryBefore,
                ExpectedIncrease = expectedIncrease,
                StartedAt = DateTimeOffset.UtcNow
            }
        });
    }

    internal void ConfirmBatch(int planIndex, CraftingPlan plan, long inventoryAfter)
    {
        var current = RequireActive(planIndex, plan);
        var pending = current.PendingBatch;
        if (pending is null || pending.PlanIndex != planIndex ||
            inventoryAfter < pending.InventoryBefore ||
            inventoryAfter - pending.InventoryBefore < pending.ExpectedIncrease)
            throw new InvalidOperationException(
                "제작 배치의 재고 증가 증거가 미확정입니다 · 신규 제작 입력 차단");
        Save(current with
        {
            PendingBatch = null,
            LastConfirmedIncrease = checked(current.LastConfirmedIncrease +
                inventoryAfter - pending.InventoryBefore)
        });
    }

    internal void MarkPlanCompleted(int planIndex, CraftingPlan plan)
    {
        var current = RequireActive(planIndex, plan);
        if (current.PendingBatch is not null ||
            current.CompletedIndices.Contains(planIndex) ||
            current.SkippedIndices.Contains(planIndex))
            throw new InvalidOperationException("제작 완료 중복 기록 또는 미확정 배치");
        Save(current with
        {
            LastPlanIndex = planIndex,
            CompletedIndices = current.CompletedIndices.Append(planIndex).ToArray()
        });
    }

    internal void MarkMaterialSkipped(int planIndex, CraftingPlan plan)
    {
        var current = RequireActive(planIndex, plan);
        if (current.PendingBatch is not null ||
            current.CompletedIndices.Contains(planIndex) ||
            current.SkippedIndices.Contains(planIndex))
            throw new InvalidOperationException("제작 품목 건너뛰기 중복 기록 또는 미확정 배치");
        Save(current with
        {
            LastPlanIndex = planIndex,
            SkippedIndices = current.SkippedIndices.Append(planIndex).ToArray()
        });
    }

    internal void FinishQueue(int completed, int skipped)
    {
        EnsureOpen();
        var current = _snapshot ?? throw new InvalidOperationException("제작 기록 초기화 전입니다.");
        if (current.Completed || current.PendingBatch is not null ||
            current.CompletedIndices.Length != completed ||
            current.SkippedIndices.Length != skipped ||
            completed + skipped != current.Plans.Length)
            throw new InvalidOperationException(
                "제작 종료 장부와 확인된 실행 결과가 달라 자동 종료 처리를 차단합니다.");
        // Skipped plans remain unfulfilled. They cannot be silently retried on
        // the next F9 without explicit review of inventory and old objectives.
        Save(current with { Completed = skipped == 0 });
    }

    private CraftingRestartSnapshot RequireActive(int index, CraftingPlan plan)
    {
        EnsureOpen();
        var s = _snapshot ?? throw new InvalidOperationException("제작 기록 초기화 전입니다.");
        if (s.Completed || index < 0 || index >= s.Plans.Length || s.Plans[index] != plan)
            throw new InvalidOperationException("제작 품목/순서가 기록된 작업과 다릅니다 · 자동 실행 차단");
        return s;
    }

    private void Save(CraftingRestartSnapshot snapshot)
    {
        EnsureOpen();
        snapshot = snapshot with { UpdatedAt = DateTimeOffset.UtcNow };
        Validate(snapshot);
        if (File.Exists(_tmpPath))
            throw new InvalidDataException("기존 제작 임시 기록 발견 · 덮어쓰기 금지");
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
        using (var file = new FileStream(_tmpPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            file.Write(payload);
            file.Flush(flushToDisk: true);
        }
        File.Move(_tmpPath, _path, overwrite: true);
        _snapshot = snapshot;
    }

    private void EnsureOpen()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CraftingRestartSafetyJournal));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lease.Dispose();
    }
}
