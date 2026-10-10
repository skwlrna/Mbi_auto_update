namespace FishingAutomation;

internal enum FacilityLaneOwner
{
    Main,
    Intermediate
}

// M3: a saved work/session ledger is evidence of queue ownership, NOT of
// the character's current physical location. Cold-start Unknown is also NOT
// proof that the player is remote; the first entry simply needs a conservative
// manager-authorized travel, unless this run establishes real onsite proof.
internal enum FacilityLocationProof
{
    ColdStartUnknown,
    ConfirmedThisRun,
    RuntimeUncertain
}

internal static class FacilityStartupLocationPolicy
{
    internal static AlteringFacilityEntryDirective DecideEntry(
        string requestedFacility,
        string? confirmedFacility,
        FacilityLocationProof proof)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFacility);
        return proof == FacilityLocationProof.ConfirmedThisRun &&
               string.Equals(confirmedFacility, requestedFacility, StringComparison.Ordinal)
            ? AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite
            : AlteringFacilityEntryDirective.FreshMoveRequired;
    }
}

internal sealed record FacilityLaneSnapshot(
    string FacilityName,
    int InitialObservedWorks,
    int MainRegisteredWorks,
    int IntermediateRegisteredWorks,
    int LiveWorks,
    bool IntermediateLease,
    int IntermediateDepth);

/// <summary>
/// Conservative ownership ledger for one seven-slot altering facility lane.
///
/// The CLI does not expose stable work IDs, so this class never pretends it can
/// identify an individual work after mixed receipt/cancellation. Instead it owns
/// the facts we can prove safely:
///   - how many works were already present when multi-altering started,
///   - how many registrations this automation actually confirmed,
///   - whether an intermediate-material scheduler currently owns the empty lane,
///   - and whether the live queue changed while no automation action could explain it.
///
/// This prevents a foreign registration, manual receipt/cancel, or overlapping
/// intermediate/main input from silently becoming part of the automation's accounting.
/// </summary>
internal sealed class FacilityLaneState
{
    private sealed class Lane
    {
        internal int InitialObservedWorks;
        internal int MainRegisteredWorks;
        internal int IntermediateRegisteredWorks;
        internal int LastObservedWorks;
        internal int ExpectedGrowth;
        internal readonly List<string> IntermediateOwners = new();
    }

    private readonly Dictionary<string, Lane> _lanes =
        new(StringComparer.Ordinal);
    private string? _confirmedOnsiteFacility;
    private FacilityLocationProof _locationProof = FacilityLocationProof.ColdStartUnknown;
    private readonly HashSet<string> _reportedColdStartFacilities =
        new(StringComparer.Ordinal);

    internal FacilityLocationProof LocationProof => _locationProof;
    internal event Action<string>? Log;

    internal FacilityLaneState(IReadOnlyList<AlteringWork> initialWorks)
    {
        ArgumentNullException.ThrowIfNull(initialWorks);

        foreach (var group in initialWorks.GroupBy(x => x.FacilityName, StringComparer.Ordinal))
        {
            int count = group.Count();
            _lanes[group.Key] = new Lane
            {
                InitialObservedWorks = count,
                LastObservedWorks = count
            };
        }
    }

    internal void Observe(
        string facilityName,
        IReadOnlyList<AlteringWork> works,
        bool allowShrink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        ArgumentNullException.ThrowIfNull(works);

        var lane = GetLane(facilityName);
        int live = works.Count(x =>
            x.FacilityName.Equals(facilityName, StringComparison.Ordinal));
        int delta = live - lane.LastObservedWorks;

        if (delta > lane.ExpectedGrowth)
        {
            throw new InvalidOperationException(
                $"{facilityName} 대기열에 자동화가 등록하지 않은 작업 증가가 감지되었습니다: " +
                $"이전 {lane.LastObservedWorks}건 / 현재 {live}건 / 자동화 예상 증가 최대 {lane.ExpectedGrowth}건. " +
                "외부 등록 작업과 자동화 작업을 구분할 수 없어 입력 없이 정지합니다.");
        }

        if (delta < 0 && !allowShrink)
        {
            throw new InvalidOperationException(
                $"{facilityName} 대기열이 자동화 수령 구간이 아닌데 감소했습니다: " +
                $"이전 {lane.LastObservedWorks}건 / 현재 {live}건. " +
                "외부 수령 또는 취소 가능성이 있어 작업 소유권 보호를 위해 정지합니다.");
        }

        lane.LastObservedWorks = live;
        // A confirmed registration may be collected inside the same guarded action,
        // so a zero/negative net delta still consumes the one-observation allowance.
        lane.ExpectedGrowth = 0;
    }

    internal AlteringFacilityEntryDirective QueueDirectiveFor(string facilityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);

        AlteringFacilityEntryDirective directive =
            FacilityStartupLocationPolicy.DecideEntry(
                facilityName, _confirmedOnsiteFacility, _locationProof);

        // Log each cold-start target once, not every round-robin batch turn.
        // Do not infer physical remoteness from the always-visible move button,
        // stored session JSON, or an existing/complete CLI facility work list.
        if (_locationProof == FacilityLocationProof.ColdStartUnknown &&
            _reportedColdStartFacilities.Add(facilityName))
            Log?.Invoke(
                $"[시설 소유권] 시작/이어하기 · {facilityName} · " +
                "초기 위치 미확정(원격 확정 아님) · 기존 작업/저장 세션은 현장 증거가 아님 · " +
                "중간관리자 최초 Fresh 이동 1회 우선 · 임의 Reuse 금지");

        return directive;
    }

    internal void ConfirmOnsite(string facilityName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        bool changed = !string.Equals(
            _confirmedOnsiteFacility,
            facilityName,
            StringComparison.Ordinal);
        _confirmedOnsiteFacility = facilityName;
        _locationProof = FacilityLocationProof.ConfirmedThisRun;

        Log?.Invoke(
            $"[시설 소유권] {facilityName.Replace(" 시설", "")} · 현장확정" +
            (changed ? " 갱신" : " 유지") +
            $" · 중간관리자 근거={reason} · 다음 같은 시설 등록은 설비 이동 재판정 금지");
    }

    internal void InvalidateOnsite(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string? previous = _confirmedOnsiteFacility;
        _confirmedOnsiteFacility = null;
        _locationProof = FacilityLocationProof.RuntimeUncertain;
        if (previous is null)
            return;

        Log?.Invoke(
            $"[시설 소유권] {previous.Replace(" 시설", "")} · 현장확정 해제 · " +
            $"중간관리자 근거={reason} · 다음 등록은 새 시설 이동 경로 사용");
    }

    internal bool IsOnsiteConfirmed(string facilityName)
        => _locationProof == FacilityLocationProof.ConfirmedThisRun &&
           string.Equals(
               _confirmedOnsiteFacility,
               facilityName,
               StringComparison.Ordinal);

    internal void AssertAccess(string facilityName, FacilityLaneOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        var lane = GetLane(facilityName);

        if (owner == FacilityLaneOwner.Main && lane.IntermediateOwners.Count > 0)
            throw new InvalidOperationException(
                $"{facilityName}은(는) 중간재료 작업이 소유 중이라 메인 다중가공 입력을 허용하지 않습니다.");

        if (owner == FacilityLaneOwner.Intermediate && lane.IntermediateOwners.Count == 0)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 작업이 시설 소유권을 확보하지 않은 상태에서 입력을 시도했습니다.");
    }

    internal void NoteRegistration(
        AlteringPlan plan,
        FacilityLaneOwner owner,
        int confirmedRegistrations)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (confirmedRegistrations < 0 || confirmedRegistrations > 7)
            throw new ArgumentOutOfRangeException(
                nameof(confirmedRegistrations),
                "한 번의 시설 소유권 갱신에서 등록 가능한 작업은 0~7건입니다.");
        if (confirmedRegistrations == 0)
            return;

        AssertAccess(plan.FacilityName, owner);
        var lane = GetLane(plan.FacilityName);

        lane.ExpectedGrowth = checked(
            lane.ExpectedGrowth + confirmedRegistrations);

        if (owner == FacilityLaneOwner.Main)
            lane.MainRegisteredWorks = checked(
                lane.MainRegisteredWorks + confirmedRegistrations);
        else
            lane.IntermediateRegisteredWorks = checked(
                lane.IntermediateRegisteredWorks + confirmedRegistrations);

        string ownerText = owner == FacilityLaneOwner.Main ? "메인" : "중간재료";
        Log?.Invoke(
            $"[시설 소유권] {plan.ScreenTitle} · {ownerText} 등록 확정 +{confirmedRegistrations}건 · " +
            $"누적 메인 {lane.MainRegisteredWorks} / 중간재료 {lane.IntermediateRegisteredWorks} · " +
            $"시작 전 기존 {lane.InitialObservedWorks}건");
    }

    /// <summary>
    /// The dependency scheduler invokes nested production synchronously while the
    /// parent waits. A child may borrow the same facility only after every parent
    /// work has completed and been received. Preserve the parent lease on a stack;
    /// never release it merely because the nested child's queue is empty.
    /// </summary>
    internal void AcquireIntermediate(
        string facilityName,
        IReadOnlyList<AlteringWork> liveWorks,
        string? ownerKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        string key = ownerKey ?? facilityName;
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var lane = GetLane(facilityName);

        // Do not mutate the ledger before checking for recursive/double ownership.
        if (lane.IntermediateOwners.Contains(key, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 {key} 소유권이 이미 활성화되어 있습니다.");

        // Taking/releasing a facility lease is never proof of a receipt.
        // The verified receipt callback must have reconciled any shrink first.
        Observe(facilityName, liveWorks, allowShrink: false);
        if (lane.LastObservedWorks != 0)
            throw new InvalidOperationException(
                $"{facilityName}에 {lane.LastObservedWorks}건의 작업이 남아 있어 중간재료가 시설을 소유할 수 없습니다.");

        lane.IntermediateOwners.Add(key);
        Log?.Invoke(
            $"[시설 소유권] {facilityName.Replace(" 시설", "")} · 중간재료 소유권 확보 · " +
            $"중첩 깊이 {lane.IntermediateOwners.Count} · 현재 대기열 0/7");
    }

    internal void ReleaseIntermediate(
        string facilityName,
        IReadOnlyList<AlteringWork> liveWorks,
        string? ownerKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        string key = ownerKey ?? facilityName;
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var lane = GetLane(facilityName);

        if (lane.IntermediateOwners.Count == 0)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 시설 소유권이 없는 상태에서 해제를 시도했습니다.");

        string currentOwner = lane.IntermediateOwners[^1];
        if (!currentOwner.Equals(key, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{facilityName} 소유권 해제 순서 불일치: 현재 {currentOwner} / 요청 {key}. " +
                "자식 중간재료가 소유 중에는 부모 소유권을 해제하지 않습니다.");

        Observe(facilityName, liveWorks, allowShrink: true);
        if (lane.LastObservedWorks != 0)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 작업 {lane.LastObservedWorks}건이 남아 있어 시설 소유권을 해제하지 않습니다.");

        lane.IntermediateOwners.RemoveAt(lane.IntermediateOwners.Count - 1);
        Log?.Invoke(
            $"[시설 소유권] {facilityName.Replace(" 시설", "")} · 중간재료 소유권 해제 · " +
            (lane.IntermediateOwners.Count > 0
                ? $"부모 중간재료 소유권 복원 · 깊이 {lane.IntermediateOwners.Count}"
                : "메인 다중가공 사용 가능"));
    }

    internal FacilityLaneSnapshot Snapshot(string facilityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        var lane = GetLane(facilityName);
        return new(
            facilityName,
            lane.InitialObservedWorks,
            lane.MainRegisteredWorks,
            lane.IntermediateRegisteredWorks,
            lane.LastObservedWorks,
            lane.IntermediateOwners.Count > 0,
            lane.IntermediateOwners.Count);
    }

    internal string Describe(string facilityName)
    {
        var s = Snapshot(facilityName);
        return
            $"기존 {s.InitialObservedWorks} · 메인등록 {s.MainRegisteredWorks} · " +
            $"중간재료등록 {s.IntermediateRegisteredWorks} · 현재 {s.LiveWorks}/7" +
            (s.IntermediateLease ? $" · 중간재료 소유중({s.IntermediateDepth}단계)" : "");
    }

    private Lane GetLane(string facilityName)
    {
        if (_lanes.TryGetValue(facilityName, out var lane))
            return lane;

        lane = new Lane();
        _lanes.Add(facilityName, lane);
        return lane;
    }
}
