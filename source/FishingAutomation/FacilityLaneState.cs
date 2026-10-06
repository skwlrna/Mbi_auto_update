namespace FishingAutomation;

internal enum FacilityLaneOwner
{
    Main,
    Intermediate
}

internal sealed record FacilityLaneSnapshot(
    string FacilityName,
    int InitialObservedWorks,
    int MainRegisteredWorks,
    int IntermediateRegisteredWorks,
    int LiveWorks,
    bool IntermediateLease);

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
        internal bool IntermediateLease;
    }

    private readonly Dictionary<string, Lane> _lanes =
        new(StringComparer.Ordinal);

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

    internal void AssertAccess(string facilityName, FacilityLaneOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        var lane = GetLane(facilityName);

        if (owner == FacilityLaneOwner.Main && lane.IntermediateLease)
            throw new InvalidOperationException(
                $"{facilityName}은(는) 중간재료 작업이 소유 중이라 메인 다중가공 입력을 허용하지 않습니다.");

        if (owner == FacilityLaneOwner.Intermediate && !lane.IntermediateLease)
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

    internal void AcquireIntermediate(
        string facilityName,
        IReadOnlyList<AlteringWork> liveWorks)
    {
        Observe(facilityName, liveWorks, allowShrink: true);
        var lane = GetLane(facilityName);

        if (lane.IntermediateLease)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 시설 소유권이 이미 활성화되어 있습니다.");
        if (lane.LastObservedWorks != 0)
            throw new InvalidOperationException(
                $"{facilityName}에 {lane.LastObservedWorks}건의 작업이 남아 있어 중간재료가 시설을 소유할 수 없습니다.");

        lane.IntermediateLease = true;
        Log?.Invoke(
            $"[시설 소유권] {facilityName.Replace(" 시설", "")} · 중간재료 전용 소유권 확보 · 현재 대기열 0/7");
    }

    internal void ReleaseIntermediate(
        string facilityName,
        IReadOnlyList<AlteringWork> liveWorks)
    {
        var lane = GetLane(facilityName);
        if (!lane.IntermediateLease)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 시설 소유권이 없는 상태에서 해제를 시도했습니다.");

        Observe(facilityName, liveWorks, allowShrink: true);
        if (lane.LastObservedWorks != 0)
            throw new InvalidOperationException(
                $"{facilityName} 중간재료 작업 {lane.LastObservedWorks}건이 남아 있어 시설 소유권을 해제하지 않습니다.");

        lane.IntermediateLease = false;
        Log?.Invoke(
            $"[시설 소유권] {facilityName.Replace(" 시설", "")} · 중간재료 소유권 해제 · 메인 다중가공 사용 가능");
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
            lane.IntermediateLease);
    }

    internal string Describe(string facilityName)
    {
        var s = Snapshot(facilityName);
        return
            $"기존 {s.InitialObservedWorks} · 메인등록 {s.MainRegisteredWorks} · " +
            $"중간재료등록 {s.IntermediateRegisteredWorks} · 현재 {s.LiveWorks}/7" +
            (s.IntermediateLease ? " · 중간재료 소유중" : "");
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
