namespace FishingAutomation;

/// <summary>
/// Batch scheduler for multi-altering. Each facility is an independent seven-slot
/// lane. Different facilities run in parallel, while plans that share one facility
/// are completed in user order, one recipe at a time, using up to seven slots.
/// The whole current lane must complete before receipt; independent facilities
/// remain parallel. Existing pre-F9 works are not cancelled.
/// </summary>
// A coordinator-scoped watchdog survives RunBatchAsync boundaries. Each
// facility is tracked independently, so healthy progress elsewhere cannot
// hide a stalled seven-slot lane. Read-only CLI observations never authorize
// new screen input, receipt or a facility move.
// A read-only diagnostic for selected facilities left empty while another
// lane's full queue masks the recipe. Never registers, collects, or moves.
// V3.2.39 per-facility read-only bottleneck evidence. A repeated episode
// is NOT a proven missing item. No game input or durable receipt changes.
// Stored only for the active F9 run; never store raw CLI payloads.
internal sealed class MultiAlteringFacilityBottleneckTracker
{
    private const int MaxFacilities = 16;
    private sealed class Entry
    {
        internal long QueueFull, Unavailable, Stalled, Uncertain;
        internal FacilityBottleneckCause? ActiveBlock;
        internal DateTimeOffset? BlockedSince;
        internal int Depth, Completed;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    internal MultiAlteringFacilityBottleneckTracker(Func<DateTimeOffset>? now = null)
        => _now = now ?? (() => DateTimeOffset.UtcNow);

    private Entry? Get(string facility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        if (_entries.TryGetValue(facility, out var entry)) return entry;
        if (_entries.Count >= MaxFacilities) return null;
        entry = new Entry();
        _entries[facility] = entry;
        return entry;
    }

    internal void ObserveWorks(string facility, IReadOnlyList<AlteringWork> works)
    {
        ArgumentNullException.ThrowIfNull(works);
        lock (_gate)
        {
            var entry = Get(facility);
            if (entry is null) return;
            entry.Depth = works.Count;
            entry.Completed = works.Count(x => x.IsCompleted);
            // Work may be an old already-completed slot. A nonempty count
            // alone does NOT prove the recipe mask was lifted; only fresh
            // selected-recipe evidence or leaving the mask path clears it.
        }
    }

    private static void Clear(Entry entry)
    {
        entry.ActiveBlock = null;
        entry.BlockedSince = null;
    }

    internal void ClearQueueBlock(string facility)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(facility, out var entry))
                Clear(entry);
        }
    }

    internal void NoteBlockedRecipe(string facility, string? cliReason)
    {
        var cause = string.Equals(cliReason, "queue_full", StringComparison.Ordinal)
            ? FacilityBottleneckCause.QueueFullMasked
            : FacilityBottleneckCause.RecipeUnavailable;
        lock (_gate)
        {
            var entry = Get(facility);
            if (entry is null) return;
            DateTimeOffset now = _now();
            // A clock rollback invalidates the elapsed-time evidence, but is
            // never permission to treat a repeated poll as a new incident.
            if (entry.ActiveBlock == cause)
            {
                if (entry.BlockedSince > now) entry.BlockedSince = now;
                return;
            }
            entry.ActiveBlock = cause;
            entry.BlockedSince = now;
            if (cause == FacilityBottleneckCause.QueueFullMasked) entry.QueueFull++;
            else entry.Unavailable++;
        }
    }

    internal void NoteStalledWork(string facility)
    {
        lock (_gate)
        {
            var entry = Get(facility);
            if (entry is not null) entry.Stalled++;
        }
    }

    internal void NoteUncertainBatch(string facility)
    {
        lock (_gate)
        {
            var entry = Get(facility);
            if (entry is not null) entry.Uncertain++;
        }
    }

    internal IReadOnlyList<FacilityBottleneckSnapshot> Snapshot()
    {
        lock (_gate)
        {
            var now = _now();
            return _entries.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x =>
                {
                    var entry = x.Value;
                    long events = entry.QueueFull + entry.Unavailable +
                        entry.Stalled + entry.Uncertain;
                    long? seconds = entry.BlockedSince.HasValue
                        ? (long)Math.Max(0, (now - entry.BlockedSince.Value).TotalSeconds)
                        : null;
                    return new FacilityBottleneckSnapshot(
                        x.Key, entry.QueueFull, entry.Unavailable, entry.Stalled,
                        entry.Uncertain, entry.ActiveBlock.HasValue,
                        seconds, entry.Depth, entry.Completed, events >= 2);
                }).ToArray();
        }
    }
}

internal static class MaterialSupplyRoutePolicy
{
    // Scan the currently selected recipe and recursive *known* producers
    // without relying on an absent catalog entry as proof of impossibility.
    // All counts are fresh read-only game CLI calls; no cached UI labels.
    internal static async Task<MaterialSupplyHold?> FindHoldAsync(
        AlteringPlan plan,
        AlteringRecipe selected,
        IReadOnlyList<AlteringRecipe> recipes,
        IReadOnlyList<GatherableItem> gathering,
        Func<string, CancellationToken, Task<long>> readCount,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(gathering);
        ArgumentNullException.ThrowIfNull(readCount);
        var path = new HashSet<string>(StringComparer.Ordinal);
        async Task<MaterialSupplyHold?> InspectAsync(
            string item, long required, int depth, long? recipeOwned = null)
        {
            ct.ThrowIfCancellationRequested();
            if (required <= 0) return null;
            long current = await readCount(item, ct);
            if (current < 0)
                throw new InvalidDataException("확인되지 않은 음수 재고는 재료 경로 판정에 사용하지 않습니다.");
            // A recipe's Owned and the separate get_items count can disagree
            // because of a stale snapshot, name/stack parsing, or in-flight
            // inventory changes. Do not turn that uncertainty into a durable
            // claim that raw hides or tannin have run out.
            if (recipeOwned.HasValue &&
                LeatherRawMaterialPolicy.IsExternalRaw(item) &&
                current != recipeOwned.Value)
                return new(item, required, current, "leather_recipe_inventory_mismatch");
            if (current >= required) return null;
            // Raw hides (including higher-tier hides) and tannin must be
            // supplied by the player. They are not automatic gather/craft inputs.
            if (LeatherRawMaterialPolicy.IsExternalRaw(item))
                return new(item, required, current, "external_leather_raw_missing");
            if (depth >= 6 || !path.Add(item))
                return new(item, required, current, "recursive_route_unverified");
            try
            {
                var matches = gathering.Where(x =>
                    string.Equals(x.DisplayName, item, StringComparison.Ordinal)).ToArray();
                if (matches.Length > 1)
                    return new(item, required, current, "ambiguous_gathering_route");
                if (matches.Length == 1)
                {
                    if (LivingSkillGatheringCatalog.IsQuestOnlyMaterial(item))
                        return new(item, required, current, "quest_only_not_bulk_gatherable");
                    if (!matches[0].ToolOk)
                        return new(item, required, current, "gathering_tool_unavailable");
                    if (!LivingSkillGatheringCatalog.TryResolveBulk(item, out _))
                        return new(item, required, current, "bulk_route_unverified");
                    return null;
                }

                if (LivingSkillGatheringCatalog.IsQuestOnlyMaterial(item))
                    return new(item, required, current, "quest_only_not_bulk_gatherable");

                var producers = recipes.Where(x =>
                    string.Equals(x.DisplayName, item, StringComparison.Ordinal) ||
                    string.Equals(System.Text.RegularExpressions.Regex.Replace(
                        x.DisplayName, @"\([^()]*\)$", "").Trim(), item,
                        StringComparison.Ordinal)).ToArray();
                if (producers.Length == 0)
                    return new(item, required, current, "acquisition_route_unverified");
                if (producers.Length != 1)
                    return new(item, required, current, "producer_identity_ambiguous");
                var producer = producers[0];
                if (producer.Alterable) return null;
                if (producer.ProducedPerWork <= 0 ||
                    producer.MissingIngredients.Count == 0)
                    return new(item, required, current, "producer_state_unverified");
                long works = checked((required - current + producer.ProducedPerWork - 1)
                    / producer.ProducedPerWork);
                foreach (var child in producer.MissingIngredients)
                {
                    long needed = checked((long)child.Required * works);
                    var hold = await InspectAsync(child.DisplayName, needed, depth + 1, child.Owned);
                    if (hold is not null) return hold;
                }
                return null; // existing producer screen/CLI checks remain necessary
            }
            finally { path.Remove(item); }
        }

        foreach (var ingredient in selected.MissingIngredients)
        {
            long needed = checked((long)ingredient.Required * plan.RequiredWorks);
            var hold = await InspectAsync(ingredient.DisplayName, needed, 0, ingredient.Owned);
            if (hold is not null) return hold;
        }
        return null;
    }
}

internal sealed class MultiAlteringLaneDeferralTracker
{
    private sealed record Observation(DateTimeOffset Since, string Signature, DateTimeOffset? LastReportedAt);
    private readonly Dictionary<string, Observation> _blocked = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _threshold;
    // Keep stalled but otherwise silent facilities visible in overnight logs
    // without spamming the UI or mistaking read-only polls for progress.
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(10);

    internal MultiAlteringLaneDeferralTracker(
        TimeSpan? threshold = null, Func<DateTimeOffset>? now = null)
    {
        _threshold = threshold ?? TimeSpan.FromMinutes(2);
        if (_threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal string? ObserveBlocked(string facility, string recipe, string reason,
        int localWorks, int otherFullFacilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe);
        // A lane containing live work is not "missing"; let the normal
        // facility watchdog handle that work's countdown.
        if (localWorks != 0 || otherFullFacilities <= 0)
        {
            Reset(facility);
            return null;
        }
        var signature = recipe + "|" + reason;
        var now = _now();
        if (!_blocked.TryGetValue(facility, out var last) ||
            !string.Equals(signature, last.Signature, StringComparison.Ordinal))
        {
            _blocked[facility] = new(now, signature, null);
            return null;
        }
        // A system clock adjustment cannot make stale evidence appear fresh.
        if (now < last.Since ||
            (last.LastReportedAt.HasValue && now < last.LastReportedAt.Value))
        {
            _blocked[facility] = new(now, signature, null);
            return null;
        }
        if (now - last.Since < _threshold ||
            (last.LastReportedAt.HasValue &&
             now - last.LastReportedAt.Value < ReminderInterval)) return null;
        _blocked[facility] = last with { LastReportedAt = now };
        return $"[다중가공][시설 누락 감시] {facility} · {recipe} · " +
            $"시설 작업 0/7 · 대기 {Math.Max(0, (now - last.Since).TotalSeconds):0}초 · " +
            $"타 시설 포화 {otherFullFacilities}개 · CLI Reason={reason} · " +
            "재료 목록 미확정 · 읽기 전용 재조회 지속 · 임의 클릭/모두 받기 금지 · " +
            (last.LastReportedAt.HasValue ? "장시간 보류 재알림" : "최초 보류 경고");
    }

    internal void Reset(string facility) => _blocked.Remove(facility);
}

internal sealed class MultiAlteringWaitWatchdog
{
    private sealed record Checkpoint(
        string Shape,
        long LowestRunningSeconds,
        DateTimeOffset LastProgressAt);

    private readonly Dictionary<string, Checkpoint> _last = new(StringComparer.Ordinal);
    private readonly TimeSpan _threshold;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset? _pauseStartedAt;

    // Only the scheduler's own stall clock pauses. Production remains active
    // on the server. Preserve the seconds already accrued before leaving, so
    // returning from an arbitrarily long Abyss run does not create a false
    // two-minute "stalled facility" alarm.
    internal void PauseForAbyss()
    {
        if (_pauseStartedAt.HasValue)
            throw new InvalidOperationException("다중가공 정체 감시가 이미 일시정지 상태입니다.");
        _pauseStartedAt = _now();
    }

    internal void ResumeAfterAbyssExit()
    {
        if (!_pauseStartedAt.HasValue)
            throw new InvalidOperationException("일시정지하지 않은 가공 정체 감시를 재개할 수 없습니다.");

        TimeSpan paused = _now() - _pauseStartedAt.Value;
        if (paused < TimeSpan.Zero)
            throw new InvalidOperationException("가공 정체 감시 시간 역행 감지 · 안전 정지");

        foreach (string facility in _last.Keys.ToArray())
        {
            Checkpoint checkpoint = _last[facility];
            _last[facility] = checkpoint with
            {
                LastProgressAt = checkpoint.LastProgressAt + paused
            };
        }
        _pauseStartedAt = null;
    }

    internal MultiAlteringWaitWatchdog(
        TimeSpan threshold,
        Func<DateTimeOffset>? now = null)
    {
        if (threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        _threshold = threshold;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // The CLI exposes no stable work IDs. Compare sorted recipe/status shapes
    // and the lowest in-progress countdown; a decreasing countdown is real
    // evidence of progress even if the queue count remains seven.
    internal void Observe(string facility, IReadOnlyList<AlteringWork> works, bool deferTimeout = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facility);
        ArgumentNullException.ThrowIfNull(works);
        if (_pauseStartedAt.HasValue)
            throw new InvalidOperationException("어비스 진행 중 가공 정체 감시/입력 상태를 조회할 수 없습니다.");

        string shape = string.Join("|",
            works.OrderBy(x => x.DisplayName, StringComparer.Ordinal)
                .ThenBy(x => x.State, StringComparer.Ordinal)
                .ThenBy(x => x.IsCompleted)
                .Select(x => $"{x.DisplayName}:{x.State}:{x.IsCompleted}"));
        long lowestRunning = works
            .Where(x => !x.IsCompleted && x.State == "InProgress")
            .Select(x => x.RemainingSeconds)
            .DefaultIfEmpty(long.MaxValue)
            .Min();

        // Idle facilities have no active work to time. The owner ledger still
        // rejects an unexplained queue shrink; an empty wait is not progress.
        if (works.Count == 0)
        {
            _last.Remove(facility);
            return;
        }

        DateTimeOffset now = _now();
        if (!_last.TryGetValue(facility, out var previous) ||
            !string.Equals(shape, previous.Shape, StringComparison.Ordinal))
        {
            _last[facility] = new(shape, lowestRunning, now);
            return;
        }

        // A timer jumping back UP is not proof of work being completed. Do not
        // keep resetting the watchdog on a noisy CLI countdown.
        if (lowestRunning < previous.LowestRunningSeconds)
        {
            _last[facility] = new(shape, lowestRunning, now);
            return;
        }

        TimeSpan idle = now - previous.LastProgressAt;
        if (idle < _threshold || deferTimeout)
            return;

        int completed = works.Count(x => x.IsCompleted);
        string remaining = lowestRunning == long.MaxValue
            ? "진행 타이머 없음"
            : $"최소 진행 남은시간 {lowestRunning}초";
        throw new InvalidOperationException(
            $"다중가공 시설별 정체 감지 · {facility} · CLI 작업 {works.Count}건 " +
            $"(완료 {completed}건) · {remaining} · " +
            $"실질 변화 없이 {Math.Max(0, idle.TotalSeconds):0}초 경과 " +
            $"(한도 {_threshold.TotalSeconds:0}초). " +
            "임의 설비 이동/모두 받기/Space 재시도 없이 안전 정지합니다.");
    }

    // Successful managed registration/collection or plan completion is
    // authoritative progress even if a same-count batch replaces old works.
    internal void ConfirmManagerProgress(string facility)
        => _last.Remove(facility);
}

// V3.2.43: observation-only facility efficiency. These counters are local
// to this run, are NOT durable work receipts, and cannot authorize input.
// Within each facility the user's recipe order is deliberately untouched.
internal sealed record MultiAlteringFacilityEfficiencySnapshot(
    string Facility, long ObservedPasses, long ReadyPasses,
    long RunningPasses, long HeldPasses, long UnservedReadyPasses,
    long BatchCalls, long VerifiedProgressTurns, long CompletedPlanTurns,
    int ReadyWaitPasses, string CurrentReadiness);

internal sealed class MultiAlteringFacilityEfficiencyTracker
{
    private sealed class Entry
    {
        internal long ObservedPasses, ReadyPasses, RunningPasses, HeldPasses;
        internal long UnservedReadyPasses, BatchCalls, VerifiedProgressTurns;
        internal long CompletedPlanTurns;
        internal int ReadyWaitPasses;
        internal string State = "unobserved";
        internal bool ReadyThisPass, ServedThisPass;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private Entry Get(string facility)
    {
        if (!_entries.TryGetValue(facility, out var e))
        {
            e = new Entry();
            _entries.Add(facility, e);
        }
        return e;
    }

    internal static string Readiness(
        IReadOnlyList<AlteringWork> works, bool held, bool initialVacancy,
        bool readQuarantined = false)
    {
        if (readQuarantined) return "read_quarantine";
        if (held) return "material_hold";
        if (works.Count > 0 && works.All(x => x.IsCompleted))
            return "completed_batch";
        if (works.Count == 0) return "empty";
        if (initialVacancy && works.Count < 7) return "initial_vacancy";
        return "running_or_partial";
    }

    internal void Observe(
        string facility, IReadOnlyList<AlteringWork> works, bool held,
        bool initialVacancy, bool readQuarantined = false)
    {
        var entry = Get(facility);
        entry.ObservedPasses++;
        entry.State = Readiness(works, held, initialVacancy, readQuarantined);
        entry.ReadyThisPass = entry.State is "completed_batch" or "empty" or
            "initial_vacancy";
        entry.ServedThisPass = false;
        if (entry.ReadyThisPass) entry.ReadyPasses++;
        else if (held || readQuarantined) entry.HeldPasses++;
        else entry.RunningPasses++;
    }

    // Reordering only independent facilities. All original CLI/onsite/7-slot/
    // F05 input fences remain in the exact same per-facility execution block.
    // Collectable complete queues before idle ones can free an occupied lane.
    // Within a readiness class older unserved lanes go first (stable tie-break).
    internal IReadOnlyList<string> Order(
        IReadOnlyList<string> selectedFacilities,
        bool optimize)
    {
        if (!optimize) return selectedFacilities.ToArray();
        static int Rank(string readiness) => readiness switch
        {
            "completed_batch" => 0,
            "empty" => 1,
            "initial_vacancy" => 2,
            "material_hold" or "read_quarantine" => 3,
            _ => 4
        };
        return selectedFacilities.Select((facility, index) => new
            {
                Facility = facility,
                Index = index,
                Entry = Get(facility)
            })
            .OrderBy(x => Rank(x.Entry.State))
            .ThenByDescending(x => x.Entry.ReadyWaitPasses)
            .ThenBy(x => x.Index)
            .Select(x => x.Facility).ToArray();
    }

    internal void NoteBatchAttempt(string facility)
    {
        var entry = Get(facility);
        entry.BatchCalls++;
        entry.ServedThisPass = true;
        entry.ReadyWaitPasses = 0;
    }

    internal void NoteVerifiedProgress(string facility, bool completedPlan)
    {
        var entry = Get(facility);
        entry.VerifiedProgressTurns++;
        if (completedPlan) entry.CompletedPlanTurns++;
    }

    internal void EndPass()
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.ReadyThisPass && !entry.ServedThisPass)
            {
                entry.UnservedReadyPasses++;
                entry.ReadyWaitPasses = Math.Min(1000,
                    entry.ReadyWaitPasses + 1);
            }
            else if (!entry.ReadyThisPass)
                entry.ReadyWaitPasses = 0;
            entry.ReadyThisPass = false;
            entry.ServedThisPass = false;
        }
    }

    internal IReadOnlyList<MultiAlteringFacilityEfficiencySnapshot> Snapshot()
        => _entries.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new MultiAlteringFacilityEfficiencySnapshot(
                x.Key, x.Value.ObservedPasses, x.Value.ReadyPasses,
                x.Value.RunningPasses, x.Value.HeldPasses,
                x.Value.UnservedReadyPasses, x.Value.BatchCalls,
                x.Value.VerifiedProgressTurns, x.Value.CompletedPlanTurns,
                x.Value.ReadyWaitPasses, x.Value.State)).ToArray();
}

internal sealed class MultiAlteringCoordinator
{
    private readonly FacilityLaneState? _laneState;
    private readonly FacilityLaneOwner _laneOwner;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _idleThreshold;
    private readonly Func<IReadOnlyList<AlteringPlan>, IReadOnlyList<AlteringPlan>>? _restoreCompleted;
    private readonly bool _fillInitialVacancies;
    private readonly bool _deferOtherLanesWhileQueueFull;
    private readonly bool _optimizeFacilityOrder;

    internal event Action<string>? Log;
    internal MultiAlteringFacilityBottleneckTracker Bottlenecks { get; }
    internal MultiAlteringFacilityEfficiencyTracker Efficiency { get; } = new();
    internal MultiAlteringReadQuarantineTracker ReadQuarantine { get; }

    internal MultiAlteringCoordinator(
        FacilityLaneState? laneState = null,
        FacilityLaneOwner laneOwner = FacilityLaneOwner.Main,
        Func<DateTimeOffset>? now = null,
        TimeSpan? idleThreshold = null,
        Func<IReadOnlyList<AlteringPlan>, IReadOnlyList<AlteringPlan>>? restoreCompleted = null,
        bool fillInitialVacancies = false,
        bool deferOtherLanesWhileQueueFull = false,
        bool optimizeFacilityOrder = false)
    {
        _laneState = laneState;
        _laneOwner = laneOwner;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Bottlenecks = new MultiAlteringFacilityBottleneckTracker(_now);
        ReadQuarantine = new MultiAlteringReadQuarantineTracker(_now);
        _idleThreshold = idleThreshold ?? TimeSpan.FromMinutes(2);
        _restoreCompleted = restoreCompleted;
        _fillInitialVacancies = fillInitialVacancies;
        _deferOtherLanesWhileQueueFull = deferOtherLanesWhileQueueFull;
        _optimizeFacilityOrder = optimizeFacilityOrder;
        if (_idleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleThreshold));
    }

    internal async Task RunAsync(
        IReadOnlyList<AlteringPlan> plans,
        Func<AlteringPlan, int, CancellationToken, Task<bool>> runBatch,
        Func<CancellationToken, Task<IReadOnlyList<AlteringWork>>> readWorks,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct,
        Func<AlteringPlan, CancellationToken, Task>? preparePlan = null,
        Func<CancellationToken, Task>? onIdleAbyssRound = null,
        long minimumAbyssWaitSeconds = AbyssAlteringSchedulePolicy.MinimumUsefulWaitSeconds,
        Func<CancellationToken, Task<IReadOnlyList<AlteringRecipe>>>? readRecipes = null,
        Func<AlteringPlan, MaterialSupplyHold, CancellationToken, Task<MaterialSupplyHold?>>? recheckUnavailableRoute = null,
        Func<IReadOnlyList<AlteringPlan>, IReadOnlyList<MultiAlteringMaterialHoldCheckpoint>>? restoreMaterialHolds = null,
        Action<AlteringPlan, MaterialSupplyHold>? persistMaterialHold = null,
        Action<AlteringPlan>? clearMaterialHold = null,
        Func<AlteringPlan, CancellationToken, Task>? verifySafePreInputRead = null,
        Action<MultiAlteringReadQuarantineSnapshot>? reportReadQuarantine = null)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runBatch);
        ArgumentNullException.ThrowIfNull(readWorks);
        ArgumentNullException.ThrowIfNull(delay);
        if (plans.Count == 0)
            throw new InvalidOperationException("다중가공 작업 목록이 비어 있습니다.");

        var seen = new HashSet<(string Facility, string Display, int Ordinal)>();
        var outputSeen = new HashSet<(string Facility, string Output)>();
        foreach (var plan in plans)
        {
            plan.Validate();
            var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
            if (!seen.Add(key))
                throw new InvalidOperationException(
                    $"다중가공 작업 목록에 같은 제법이 중복되어 있습니다: {plan.DisplayName}");

            // Works report the base output name, so two qualified recipes producing
            // the same output in one facility cannot be attributed safely.
            var outputKey = (plan.FacilityName, plan.OutputName);
            if (!outputSeen.Add(outputKey))
                throw new InvalidOperationException(
                    $"다중가공에서 같은 시설의 동일 결과물 제법을 동시에 구분할 수 없습니다: {plan.OutputName}");
        }

        var orderedFacilities = plans
            .Select(x => x.FacilityName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var plansByFacility = orderedFacilities.ToDictionary(
            facility => facility,
            facility => plans.Where(x => x.FacilityName == facility).ToArray(),
            StringComparer.Ordinal);
        var completed = new HashSet<(string Facility, string Display, int Ordinal)>();
        var prepared = new HashSet<(string Facility, string Display, int Ordinal)>();
        // A held recipe does not become a completed plan or permit the next
        // recipe in the same lane. Only independent facilities keep working.
        var held = new Dictionary<string,
            (AlteringPlan Plan, MaterialSupplyHold Reason, DateTimeOffset NextCheck)>(
                StringComparer.Ordinal);
        // Keep diagnostic evidence when the CLI refusal reason changes. Avoid
        // repeating an identical line every 30-second observation cycle.
        var globalQueueBlocked = new Dictionary<string, string>(StringComparer.Ordinal);
        var globalQueueUnblocked = new HashSet<string>(StringComparer.Ordinal);
        var idleWatchdog = new MultiAlteringWaitWatchdog(_idleThreshold, _now);
        var deferrals = new MultiAlteringLaneDeferralTracker(_idleThreshold, _now);
        string? lastOptimizedOrder = null;
        // Only the explicit new-F9 mode gets one initial pass into vacant
        // slots already occupied by old game jobs. Ordinary batching is unchanged.
        var startupVacancies = new HashSet<string>(
            orderedFacilities.Where(f => _fillInitialVacancies &&
                _laneState is not null &&
                _laneState.Snapshot(f).InitialObservedWorks > 0),
            StringComparer.Ordinal);

        (string Facility, string Display, int Ordinal) Key(AlteringPlan p)
            => (p.FacilityName, p.DisplayName, p.RecipeOrdinal);

        void RestoreCompleted()
        {
            if (_restoreCompleted is null) return;
            foreach (var restored in _restoreCompleted(plans))
            {
                if (!plans.Contains(restored))
                    throw new InvalidOperationException("복원 완료 품목이 현재 배치와 다릅니다.");
                completed.Add(Key(restored));
            }
        }
        RestoreCompleted();

        // V3.2.41: restore only quarantine evidence, never 'prepared' status,
        // facility onsite ownership, receipt, or registration permission.
        // Force an immediate fresh read even when the persisted stock was
        // recently checked before F10/crash. Explicit resume verification
        // and the durable work journal still gate every actual input.
        if (restoreMaterialHolds is not null)
        {
            if (recheckUnavailableRoute is null || persistMaterialHold is null ||
                clearMaterialHold is null)
                throw new InvalidOperationException(
                    "재료 보류 복원에 필요한 영속/재확인 경로가 없습니다 · 안전 정지");
            foreach (var saved in restoreMaterialHolds(plans))
            {
                var matches = plans.Where(p =>
                    p.FacilityName == saved.FacilityName &&
                    MultiAlteringBatchStore.Key(p) == saved.PlanKey).ToArray();
                if (matches.Length != 1 || completed.Contains(Key(matches[0])) ||
                    !held.TryAdd(saved.FacilityName,
                        (matches[0], new MaterialSupplyHold(
                            saved.Item, saved.Required, saved.Observed, saved.Category),
                         _now())))
                    throw new InvalidOperationException(
                        "저장된 재료 보류/현재 작업 목록 불일치 · 추가 등록 없이 정지");
                Log?.Invoke(
                    $"[다중가공][보류 복원] {saved.FacilityName} · {matches[0].DisplayName} · " +
                    $"{saved.Item} {saved.Observed}/{saved.Required} · " +
                    $"이전 확인 {saved.Rechecks}회 · 실제 재고/제법 신규 확인 전 등록 차단");
            }
        }

        int FindNextPendingIndex(string facility)
        {
            // Stick to the FIRST unfinished recipe. Receipt plus durable
            // completion is mandatory before the next recipe is selected.
            var facilityPlans = plansByFacility[facility];
            for (int index = 0; index < facilityPlans.Length; index++)
                if (!completed.Contains(Key(facilityPlans[index])))
                    return index;
            return -1;
        }

        int PendingCount(string facility)
            => plansByFacility[facility].Count(x => !completed.Contains(Key(x)));

        Log?.Invoke(
            $"[다중가공] 시설별 품목 순차 배치 계획 확정 · {plans.Count}종 / {orderedFacilities.Length}시설 · " +
            string.Join(" → ", plans.Select(x => $"{x.DisplayName} {x.TargetQuantity}개")));
        Log?.Invoke(
            "[다중가공] 운용 방식 · 시설별 최대 7칸 · 같은 시설 여러 품목은 앞 품목 완료 후 다음 품목 · " +
            "한 칸 완료마다 이동하지 않음 · 현재 배치 전체 완료 후 묶음 수령/재충전");

        while (completed.Count < plans.Count)
        {
            ct.ThrowIfCancellationRequested();
            var works = await readWorks(ct);
            bool acted = false;

            // Persist per-facility evidence across all 1-slot RunBatch returns.
            // Never reset just because the scheduler went around another loop.
            foreach (string facility in orderedFacilities)
            {
                if (PendingCount(facility) == 0)
                {
                    deferrals.Reset(facility);
                    continue;
                }
                var observed = works.Where(x => x.FacilityName == facility).ToArray();
                Bottlenecks.ObserveWorks(facility, observed);
                try
                {
                    // An all-completed lane needs one guarded manager turn for
                    // receipt. Do not reset its original stall deadline here.
                    idleWatchdog.Observe(facility, observed,
                        deferTimeout: observed.Length > 0 && observed.All(x => x.IsCompleted));
                }
                catch (InvalidOperationException ex)
                {
                    // A stalled facility is no longer a safe onsite assumption.
                    // Invalidate its shared authority and surface the CLI-only
                    // diagnostic; never start an autonomous movement or receipt.
                    Bottlenecks.NoteStalledWork(facility);
                    _laneState?.InvalidateOnsite(
                        $"시설별 대기 정체 · {facility} · 화면 재확인 필요");
                    Log?.Invoke($"[다중가공] {ex.Message}");
                    throw;
                }
            }

            // Performance-based ordering changes ONLY which independent lane
            // receives a turn first. Same-facility recipe order is fixed, and
            // all existing receipt/registration guards still run afterward.
            foreach (string facility in orderedFacilities)
            {
                if (PendingCount(facility) == 0) continue;
                var current = works.Where(x => x.FacilityName == facility).ToArray();
                Efficiency.Observe(facility, current, held.ContainsKey(facility),
                    startupVacancies.Contains(facility) && current.Length is > 0 and < 7,
                    readQuarantined: ReadQuarantine.HasUnverified(facility));
            }
            var facilityOrder = Efficiency.Order(orderedFacilities,
                _optimizeFacilityOrder);
            if (_optimizeFacilityOrder)
            {
                string signature = string.Join(" -> ", facilityOrder);
                if (!facilityOrder.SequenceEqual(orderedFacilities) &&
                    signature != lastOptimizedOrder)
                    Log?.Invoke(
                        "[다중가공][시설 효율] CLI 완료 배치 수령 우선 → 빈 시설 → 최초 빈칸 → " +
                        "재료 보류/진행 중 대기 · 독립 시설 순서만 조정 · " +
                        signature + " · 실제 이동/등록/수령은 기존 검증 유지");
                lastOptimizedOrder = signature;
            }
            foreach (string facility in facilityOrder)
            {
                ct.ThrowIfCancellationRequested();
                if (PendingCount(facility) == 0)
                    continue;

                if (held.TryGetValue(facility, out var waiting))
                {
                    if (recheckUnavailableRoute is null)
                        throw new InvalidOperationException(
                            "보류 재료 재확인 경로가 없어 자동 재개하지 않습니다.");
                    if (_now() < waiting.NextCheck)
                        continue;
                    // The same fresh read-only classification that caused the
                    // hold must re-verify stock and recipe identity. Old UI
                    // evidence, time alone or another lane's completion cannot
                    // unblock this plan.
                    var unresolved = await recheckUnavailableRoute(
                        waiting.Plan, waiting.Reason, ct);
                    if (unresolved is not null)
                    {
                        // Atomic checkpoint must succeed before an updated hold
                        // enters the in-memory scheduler. Failure stops all input.
                        persistMaterialHold?.Invoke(waiting.Plan, unresolved);
                        held[facility] = (waiting.Plan, unresolved,
                            _now() + TimeSpan.FromSeconds(30));
                        continue;
                    }
                    // Never remove the in-memory guard until its disk record
                    // has been atomically cleared. Every next input still
                    // passes the original recipe, queue and onsite guards.
                    clearMaterialHold?.Invoke(waiting.Plan);
                    held.Remove(facility);
                    Log?.Invoke($"[다중가공][재료 재확인] {facility} · {waiting.Plan.DisplayName} · " +
                        "부족 상태 재조회 완료 · 기존 F9 장부/화면 등록 검증을 다시 수행");
                }

                // V3.2.44: a typed read-only preflight failure can pause ONLY
                // its own facility. Never downgrade a state/ownership failure
                // or anything after game input to a lane-local incident.
                if (ReadQuarantine.HasUnverified(facility))
                {
                    if (verifySafePreInputRead is null)
                        throw new InvalidOperationException(
                            "Safe-read quarantine has no fresh verification callback");
                    if (ReadQuarantine.IsBlocked(facility, _now()))
                        continue;
                    var pending = plansByFacility[facility][FindNextPendingIndex(facility)];
                    var beforeRecovery = await readWorks(ct);
                    var local = beforeRecovery.Where(x =>
                        x.FacilityName == facility).ToArray();
                    _laneState?.Observe(facility, local, allowShrink: false);
                    if (local.Length != 0)
                        throw new InvalidOperationException(
                            "Read quarantine observed foreign or unconfirmed queue change; stop");
                    try
                    {
                        await verifySafePreInputRead(pending, ct);
                    }
                    catch (SafePreInputReadException transient)
                    {
                        var evidence = ReadQuarantine.NoteFailure(
                            pending, transient.Operation, TimeSpan.FromSeconds(30));
                        Log?.Invoke($"[다중가공][읽기 격리] {facility} · {pending.DisplayName} · " +
                            $"안전한 사전 CLI 재확인 실패 {evidence.ConsecutiveFailures}회 · " +
                            "30초 후 읽기만 재검증 · 다른 시설 진행 · 게임 입력 없음");
                        if (evidence.Repeated) reportReadQuarantine?.Invoke(evidence);
                        continue;
                    }
                    var recovered = ReadQuarantine.NoteRecovered(pending);
                    reportReadQuarantine?.Invoke(recovered);
                    Log?.Invoke($"[다중가공][읽기 복구] {facility} · {pending.DisplayName} · " +
                        "새 CLI 제법·대기열 확인 완료 · 기존 재료/현장/장부 가드 재실행");
                    works = await readWorks(ct);
                    if (works.Any(x => x.FacilityName == facility))
                        throw new InvalidOperationException(
                            "Read quarantine recovery queue changed; stop without input");
                }

                var facilityWorks = works
                    .Where(x => x.FacilityName == facility)
                    .ToArray();
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);

                bool laneReady =
                    facilityWorks.Length == 0 ||
                    facilityWorks.All(x => x.IsCompleted);
                bool initialVacancy = startupVacancies.Contains(facility) &&
                    facilityWorks.Length is > 0 and < 7;

                if (!laneReady && !initialVacancy)
                {
                    deferrals.Reset(facility);
                    Bottlenecks.ClearQueueBlock(facility);
                    continue;
                }

                // The first F9 CLI snapshot may report global queue_full just
                // because an unrelated facility has seven works. It is NOT a
                // lifetime embargo on an otherwise independent food/medicine
                // lane: re-query the currently selected recipe while slots are
                // available. Only affirmative live evidence may lift the mask.
                // MissingIngredients authorizes the existing resolver, NEVER
                // a registration click; AlteringAutomation rechecks Alterable
                // and live works before each actual UI operation.
                int otherFullLanes = orderedFacilities.Count(other =>
                    !string.Equals(other, facility, StringComparison.Ordinal) &&
                    works.Count(w => w.FacilityName == other) == 7);
                bool anotherFullLane = otherFullLanes > 0;
                if (_deferOtherLanesWhileQueueFull &&
                    facilityWorks.Length < 7 && anotherFullLane)
                {
                    if (readRecipes is null)
                        continue; // Legacy callers cannot prove independent readiness.

                    int next = FindNextPendingIndex(facility);
                    if (next < 0) continue;
                    var nextPlan = plansByFacility[facility][next];
                    var freshRecipes = await readRecipes(ct);
                    var selected = freshRecipes.Where(x =>
                        string.Equals(x.DisplayName, nextPlan.DisplayName,
                            StringComparison.Ordinal)).ToArray();
                    if (selected.Length < nextPlan.RecipeOrdinal ||
                        selected[nextPlan.RecipeOrdinal - 1].ProducedPerWork != nextPlan.ProducedPerWork ||
                        AlteringFacilityResolver.Resolve(
                            selected[nextPlan.RecipeOrdinal - 1]) != facility)
                        throw new InvalidOperationException(
                            $"전역 queue_full 재검증 중 제법 동일성 확인 실패 · {nextPlan.DisplayName} · 추가 등록 없이 정지");

                    var currentRecipe = selected[nextPlan.RecipeOrdinal - 1];
                    if (!currentRecipe.Alterable &&
                        currentRecipe.MissingIngredients.Count == 0)
                    {
                        string refusal = currentRecipe.Reason ?? "unknown";
                        string evidence = nextPlan.DisplayName + "\u001f" + refusal +
                            "\u001f" + facilityWorks.Length;
                        if (!globalQueueBlocked.TryGetValue(facility, out string? previous) ||
                            !string.Equals(previous, evidence, StringComparison.Ordinal))
                        {
                            globalQueueBlocked[facility] = evidence;
                            Log?.Invoke(
                                $"[다중가공] 독립 시설 보류 · {nextPlan.DisplayName} · " +
                                $"현재 CLI Reason={refusal} · 부족 재료 증거 없음 · " +
                                $"해당 시설 {facilityWorks.Length}/7칸, 타 시설 7/7칸 · " +
                                "다음 대기 주기에 실제 제법 재조회 · 무확인 등록 클릭 금지");
                        }
                        globalQueueUnblocked.Remove(facility);
                        Bottlenecks.NoteBlockedRecipe(facility, refusal);
                        string? starvation = deferrals.ObserveBlocked(
                            facility, nextPlan.DisplayName, refusal,
                            facilityWorks.Length, otherFullLanes);
                        if (starvation is not null) Log?.Invoke(starvation);
                        continue;
                    }

                    deferrals.Reset(facility);
                    Bottlenecks.ClearQueueBlock(facility);
                    globalQueueBlocked.Remove(facility);
                    if (globalQueueUnblocked.Add(facility))
                        Log?.Invoke(
                            $"[다중가공] 독립 시설 진행 승인 · {nextPlan.DisplayName} · " +
                            $"CLI {(currentRecipe.Alterable ? "가공 가능" : "구체적 부족 재료 있음")} · " +
                            $"해당 시설 {facilityWorks.Length}/7칸 · " +
                            "기존 화면·재료·등록 안전 검증 유지");
                }

                if (!(_deferOtherLanesWhileQueueFull &&
                    facilityWorks.Length < 7 && anotherFullLane))
                {
                    deferrals.Reset(facility);
                    Bottlenecks.ClearQueueBlock(facility);
                }

                if (initialVacancy)
                    Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} " +
                        $"기존 작업 그대로 · 빈 {7 - facilityWorks.Length}칸에 새 F9 등록");
                else if (facilityWorks.Length > 0)
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 배치 완료 · " +
                        $"{facilityWorks.Length}건 모아서 수령 후 같은 품목 재충전");
                else
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 빈 대기열 · 현재 품목 배치 등록 시작");

                int idleSelections = 0;

                while (PendingCount(facility) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    // A facility-wide receipt can complete another main item
                    // inside this turn or a recursive dependency. Rehydrate before
                    // selecting the active recipe, not just at startup.
                    RestoreCompleted();
                    if (PendingCount(facility) == 0) break;
                    works = await readWorks(ct);
                    facilityWorks = works.Where(x => x.FacilityName == facility).ToArray();
                    _laneState?.Observe(facility, facilityWorks, allowShrink: false);
                    bool completedLaneNeedsCollection =
                        facilityWorks.Length > 0 &&
                        facilityWorks.All(x => x.IsCompleted);
                    // A fast item may complete while the 7-slot mixed lane is
                    // still being seeded. Do not call an Automation whose entry
                    // could attempt a prohibited partial "collect all". Leave the
                    // running slots intact and revisit at the full batch boundary.
                    if (AlteringReceiptPolicy.IsPartialManagedFacility(facilityWorks, facility) &&
                        !initialVacancy)
                    {
                        Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} 부분 완료 · " +
                                    "수령/추가 등록 보류 · 시설 전체 완료까지 다른 시설로 양보");
                        break;
                    }
                    if (facilityWorks.Length >= 7 && !completedLaneNeedsCollection)
                        break;

                    int pendingBefore = PendingCount(facility);
                    int index = FindNextPendingIndex(facility);
                    if (index < 0)
                        break;

                    var plan = plansByFacility[facility][index];
                    int beforeCount = facilityWorks.Length;
                    // Snapshot the facility-specific durable registration evidence.
                    // CLI work counts have no stable work IDs: a guarded receipt
                    // followed by registration can still look like 1 -> 1.
                    var registrationBefore = _laneState?.Snapshot(facility);

                    // Keep the existing proven one-slot engine and receipt
                    // guards. Prepare materials just once, before the FIRST
                    // registration of each selected recipe.
                    _laneState?.AssertAccess(facility, _laneOwner);
                    if (!prepared.Contains(Key(plan)))
                    {
                        if (preparePlan is not null)
                        {
                            Log?.Invoke($"[다중가공] {facility.Replace(" 시설", "")} · {plan.DisplayName} 새 품목 재료 점검");
                            try
                            {
                                await preparePlan(plan, ct);
                            }
                            catch (SafePreInputReadException transient)
                                when (verifySafePreInputRead is not null &&
                                      facilityWorks.Length == 0)
                            {
                                // The typed exception is emitted ONLY by the
                                // three read-only preflight queries. No movement,
                                // gathering, receipt or registration began.
                                var evidence = ReadQuarantine.NoteFailure(
                                    plan, transient.Operation, TimeSpan.FromSeconds(30));
                                Log?.Invoke(
                                    $"[다중가공][읽기 격리] {facility} · {plan.DisplayName} · " +
                                    $"사전 CLI 오류 {evidence.ConsecutiveFailures}회 · " +
                                    "해당 시설만 보류 · 다른 시설 계속 · 30초 후 읽기 재검증");
                                if (evidence.Repeated)
                                {
                                    Log?.Invoke(
                                        $"[다중가공][반복 오류] {facility} · " +
                                        "동일 읽기 오류 연속 2회 이상 · 진단 기록/AutoFix 검토 대상 · " +
                                        "무인 코드 수정·배포/게임 입력 재실행 없음");
                                    reportReadQuarantine?.Invoke(evidence);
                                }
                                break;
                            }
                            catch (MaterialSupplyHoldException unavailable)
                                when (recheckUnavailableRoute is not null)
                            {
                                // Must be raised BEFORE any input for this plan.
                                // No completion, no work ticket and no next
                                // recipe in this same facility may be claimed.
                                persistMaterialHold?.Invoke(plan, unavailable.Evidence);
                                held[facility] = (plan, unavailable.Evidence,
                                    _now() + TimeSpan.FromSeconds(30));
                                Log?.Invoke($"[다중가공][재료 확보 대기] {facility} · " +
                                    $"{plan.DisplayName} · {unavailable.Evidence.Item} " +
                                    $"{unavailable.Evidence.Observed}/{unavailable.Evidence.Required} · " +
                                    $"분류={unavailable.Evidence.Category} · " +
                                    "다른 시설 진행 · 재고/제법 30초 간격 재검증 · " +
                                    "자동 구매·재등록 없음");
                                break;
                            }
                        }
                        // A failed/cancelled preparation never authorizes a click.
                        prepared.Add(Key(plan));
                    }
                    Efficiency.NoteBatchAttempt(facility);
                    bool planCompleted = await runBatch(plan, 1, ct);
                    acted = true;

                    if (planCompleted)
                    {
                        RestoreCompleted();
                        if (_restoreCompleted is not null && !completed.Contains(Key(plan)))
                            throw new InvalidOperationException("품목 완료의 영구 저장 증거 부족 · 안전 정지");
                        completed.Add(Key(plan));
                        Log?.Invoke(
                            $"[다중가공] 품목 처리 종료 {completed.Count}/{plans.Count} · {plan.DisplayName} · " +
                            "목표 전량 완료 또는 원재료 소진");
                    }

                    works = await readWorks(ct);
                    var afterFacilityWorks = works
                        .Where(x => x.FacilityName == facility)
                        .ToArray();
                    // Guarded receipt callbacks must reconcile their own verified
                    // shrink before control returns here. An unproven drop during
                    // runBatch must not be silently accepted as a valid receipt.
                    _laneState?.Observe(facility, afterFacilityWorks, allowShrink: false);
                    int afterCount = afterFacilityWorks.Length;
                    var registrationAfter = _laneState?.Snapshot(facility);
                    bool confirmedRegistration =
                        registrationBefore is not null &&
                        registrationAfter is not null &&
                        (registrationAfter.MainRegisteredWorks > registrationBefore.MainRegisteredWorks ||
                         registrationAfter.IntermediateRegisteredWorks > registrationBefore.IntermediateRegisteredWorks);
                    // Count-only deltas are observations, not durable progress
                    // proof. They must never inflate the verified efficiency rate.
                    if (planCompleted || confirmedRegistration)
                        Efficiency.NoteVerifiedProgress(facility, planCompleted);

                    if (planCompleted || confirmedRegistration || afterCount != beforeCount)
                    {
                        // Only verified manager progress refreshes this facility:
                        // CLI-confirmed NoteRegistration, a guarded queue change
                        // (including verified receipt), or durable plan completion.
                        // Never reset just because RunBatch returned or time passed.
                        idleWatchdog.ConfirmManagerProgress(facility);
                        if (confirmedRegistration && afterCount == beforeCount && !planCompleted)
                            Log?.Invoke(
                                $"[다중가공] {facility.Replace(" 시설", "")} 정체 감시 초기화 · " +
                                $"CLI 확인 신규 등록 · 시설 작업수 {beforeCount}->{afterCount} 동일 · " +
                                $"메인등록 {registrationBefore!.MainRegisteredWorks}->{registrationAfter!.MainRegisteredWorks} · " +
                                $"중간재료등록 {registrationBefore.IntermediateRegisteredWorks}->{registrationAfter.IntermediateRegisteredWorks}");
                        idleSelections = 0;
                    }
                    else
                    {
                        // A no-op manager must still enforce the ORIGINAL deadline.
                        // Never allow unbounded retries or a read-only CLI to
                        // authorize receipt, Space, or movement.
                        try
                        {
                            idleWatchdog.Observe(facility, afterFacilityWorks);
                        }
                        catch (InvalidOperationException)
                        {
                            Bottlenecks.NoteStalledWork(facility);
                            _laneState?.InvalidateOnsite(
                                "시설별 대기 정체 · " + facility + " · 화면 재확인 필요");
                            throw;
                        }
                        idleSelections++;
                        if (idleSelections >= Math.Max(1, pendingBefore))
                        {
                            Log?.Invoke(
                                $"[다중가공] {facility.Replace(" 시설", "")} 단일 품목 추가 등록 없음 · " +
                                "남은 품목은 이미 전량 등록되었거나 현재 슬롯 상태를 기다리는 중");
                            break;
                        }
                    }
                }

                // Never revisit a partially completed batch to top up slots
                // merely because a job finished after this initial fill pass.
                startupVacancies.Remove(facility);
                works = await readWorks(ct);
                facilityWorks = works.Where(x => x.FacilityName == facility).ToArray();
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);
                if (facilityWorks.Length > 0)
                {
                    string composition = string.Join(", ",
                        facilityWorks
                            .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
                            .Select(g => $"{g.Key} {g.Count()}칸"));
                    string ownership = _laneState is null
                        ? ""
                        : $" · 소유권 {_laneState.Describe(facility)}";
                    Log?.Invoke(
                        $"[다중가공] {facility.Replace(" 시설", "")} 현재 배치 {facilityWorks.Length}/7 · {composition}{ownership}");
                }
            }

            Efficiency.EndPass();
            if (completed.Count >= plans.Count)
                break;

            if (acted)
                continue;

            works = await readWorks(ct);
            foreach (string facility in orderedFacilities)
            {
                var facilityWorks = works
                    .Where(x => x.FacilityName == facility)
                    .ToArray();
                _laneState?.Observe(facility, facilityWorks, allowShrink: false);
            }

            var active = works
                .Where(x => orderedFacilities.Contains(x.FacilityName, StringComparer.Ordinal))
                .ToArray();

            // No facility is revisited merely because one slot completed. Wait until
            // its entire current batch drains to Completed, then collect in one trip.
            long waitSeconds = active
                .Where(x => x.State == "InProgress")
                .Select(x => x.RemainingSeconds)
                .DefaultIfEmpty(5)
                .Min();
            int completedSlots = active.Count(x => x.IsCompleted);
            int runningSlots = active.Count(x => !x.IsCompleted);
            Log?.Invoke(
                $"[다중가공] 병렬 대기 · 진행/대기 {runningSlots}건 · 완료 누적 {completedSlots}건 · " +
                $"다음 확인 약 {Math.Clamp(waitSeconds, 1, 30)}초 · 배치 전체 완료 전 이동 없음");

            // No crafting, receipt, or movement is in flight here. This callback
            // owns all input until the abyss has EXITED to the verified outside HUD.
            // Immediately re-query works afterwards; never act on stale observations.
            if (onIdleAbyssRound is not null &&
                AbyssAlteringSchedulePolicy.CanRun(active, minimumAbyssWaitSeconds))
            {
                Log?.Invoke($"[병행] 모든 진행 중 가공 {minimumAbyssWaitSeconds}초 이상 남음 -> 가공 감시 일시정지 · 어비스에 입력권 양도");
                // Suspend only manager/watchdog clock. A different-length
                // dungeon is fine: no scheduler countdown can end the Abyss.
                idleWatchdog.PauseForAbyss();
                await onIdleAbyssRound(ct); // MUST verify outside HUD before returning
                ct.ThrowIfCancellationRequested();
                // Never resume processing on exit failure/F10 cancellation.
                idleWatchdog.ResumeAfterAbyssExit();
                Log?.Invoke("[병행] 던전 밖 복귀 확정 -> 가공 감시 재개 · 실제 서버 가공 대기열 즉시 재조회");
                continue;
            }

            await delay(
                TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 30)),
                ct);
        }

        Log?.Invoke("[다중가공] 전체 작업 완료");
    }
}
