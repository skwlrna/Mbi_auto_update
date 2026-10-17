using System.Numerics;

namespace DungeonVisionBot;

// Logical, normalized observations only. These are NOT packet IDs, wire layouts, or OCR coordinates.
internal enum WaterwayObservationOrigin { AllowedReceivedPacket, Screen, Inference }

internal readonly record struct WaterwayEvidence(
    bool Confirmed,
    WaterwayObservationOrigin Origin,
    DateTimeOffset ObservedAt,
    string Description)
{
    public bool IsDirect => Confirmed &&
        Origin is WaterwayObservationOrigin.AllowedReceivedPacket or WaterwayObservationOrigin.Screen &&
        !string.IsNullOrWhiteSpace(Description);

    public bool IsFresh(DateTimeOffset now, TimeSpan measuredFreshnessWindow) =>
        IsDirect && measuredFreshnessWindow > TimeSpan.Zero &&
        ObservedAt <= now && now - ObservedAt <= measuredFreshnessWindow;
}

internal enum WaterwayStage
{
    Outside = 0,
    Room1Spear = 1,
    Room1Cleared = 2,
    Room2Combat = 3,
    Room2Cleared = 4,
    Room3Tiles = 5,
    BossDefeated = 6,
    ResultPrompt = 7,
    RewardConfirmed = 8
}

internal enum WaterwayMilestone
{
    Room1EnemiesDefeatedAndDoorOpen,
    Room2CombatEntered,
    Room2ClearedAndNextAreaOpen,
    Room3Entered,
    TerosaDefeated,
    TouchResultPromptVisible,
    RewardScreenVisible
}

internal readonly record struct WaterwayTransition(
    long EntryEpoch, WaterwayStage From, WaterwayStage To, WaterwayMilestone Milestone,
    WaterwayObservationOrigin Origin, DateTimeOffset At, string Description);

// A separate per-dungeon-run gate. It does not replace ScenarioEngine's existing result/exit FSM.
internal sealed class ScatteredWaterwayFlow
{
    private string? _entryMarker;
    private DateTimeOffset _stageStartedAt = DateTimeOffset.MinValue;
    private readonly List<WaterwayTransition> _history = new();

    public long EntryEpoch { get; private set; }
    public WaterwayStage Stage { get; private set; } = WaterwayStage.Outside;
    public IReadOnlyList<WaterwayTransition> History => _history;

    // entryMarker is a local confirmation nonce, never a claimed game packet field.
    // The caller MUST create a new nonce only on independent confirmation of a NEW entry.
    public bool TryBeginNewEntry(string entryMarker, WaterwayEvidence proof)
    {
        if (string.IsNullOrWhiteSpace(entryMarker) || !proof.IsDirect ||
            string.Equals(_entryMarker, entryMarker, StringComparison.Ordinal) ||
            proof.ObservedAt < _stageStartedAt)
            return false;

        _entryMarker = entryMarker;
        EntryEpoch++;
        Stage = WaterwayStage.Room1Spear;
        _stageStartedAt = proof.ObservedAt;
        _history.Clear();
        return true;
    }

    public bool TryAdvance(long epoch, WaterwayStage next,
        WaterwayMilestone milestone, WaterwayEvidence proof)
    {
        if (epoch != EntryEpoch || !proof.IsDirect || proof.ObservedAt < _stageStartedAt ||
            Stage is WaterwayStage.Outside or WaterwayStage.RewardConfirmed ||
            (int)next != (int)Stage + 1 || RequiredEvidence(next) != milestone)
            return false;

        _history.Add(new WaterwayTransition(epoch, Stage, next, milestone,
            proof.Origin, proof.ObservedAt, proof.Description));
        Stage = next;
        _stageStartedAt = proof.ObservedAt;
        return true;
    }

    private static WaterwayMilestone? RequiredEvidence(WaterwayStage destination) => destination switch
    {
        WaterwayStage.Room1Cleared => WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen,
        WaterwayStage.Room2Combat => WaterwayMilestone.Room2CombatEntered,
        WaterwayStage.Room2Cleared => WaterwayMilestone.Room2ClearedAndNextAreaOpen,
        WaterwayStage.Room3Tiles => WaterwayMilestone.Room3Entered,
        WaterwayStage.BossDefeated => WaterwayMilestone.TerosaDefeated,
        WaterwayStage.ResultPrompt => WaterwayMilestone.TouchResultPromptVisible,
        WaterwayStage.RewardConfirmed => WaterwayMilestone.RewardScreenVisible,
        _ => null
    };
}

internal static class WaterwaySpearGeometry
{
    // Source = thrower; mage = matching-shield shaman. Setback must be measured in play.
    public static Vector2? TargetBehind(Vector2 source, Vector2 mage, float measuredSetback)
    {
        Vector2 axis = mage - source;
        if (!Valid(source) || !Valid(mage) ||
            !float.IsFinite(measuredSetback) || measuredSetback <= 0 ||
            axis.LengthSquared() < 0.000001f)
            return null;

        return mage + Vector2.Normalize(axis) * measuredSetback;
    }

    // Geometric candidate only. Collision/hit still requires direct game evidence.
    public static bool IsInLine(Vector2 source, Vector2 mage, Vector2 player,
        float measuredHitRadius, float measuredMinSetback)
    {
        if (!Valid(source) || !Valid(mage) || !Valid(player) ||
            !float.IsFinite(measuredHitRadius) || measuredHitRadius <= 0 ||
            !float.IsFinite(measuredMinSetback) || measuredMinSetback <= 0)
            return false;

        Vector2 trajectory = player - source;
        float length2 = trajectory.LengthSquared();
        if (length2 < 0.000001f)
            return false;
        float t = Vector2.Dot(mage - source, trajectory) / length2;
        float separation = Vector2.Distance(mage, source + t * trajectory);
        float behind = Vector2.Dot(player - mage, mage - source);
        return t > 0 && t < 1 && separation <= measuredHitRadius &&
            behind >= measuredMinSetback * Vector2.Distance(source, mage);
    }

    private static bool Valid(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}

internal enum WaterwayTile { Empty, O, X, Unknown }

internal static class WaterwayTilePlanner
{
    private static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 }
    };
    private static readonly int[] Corners = { 0, 2, 6, 8 };
    private static readonly int[] Edges = { 1, 3, 5, 7 };

    // This function is a decision only; turn ownership and board freshness are required.
    public static int? Choose(IReadOnlyList<WaterwayTile>? board,
        bool observedMyTurn, bool observedSelectedPlayerIsMe, int? lastOwnCorner)
    {
        if (!observedMyTurn || !observedSelectedPlayerIsMe || board is null ||
            board.Count != 9 || board.Any(x => x == WaterwayTile.Unknown))
            return null;

        int? winning = Completion(board, WaterwayTile.O);
        if (winning.HasValue) return winning;
        int? blocking = Completion(board, WaterwayTile.X);
        if (blocking.HasValue) return blocking;

        // The opposite corner is preferred AMONG the free corners, not over a forced move.
        if (board[4] != WaterwayTile.X && lastOwnCorner.HasValue && Corners.Contains(lastOwnCorner.Value))
        {
            int opposite = 8 - lastOwnCorner.Value;
            if (board[opposite] == WaterwayTile.Empty)
                return opposite;
        }
        foreach (int corner in Corners)
            if (board[corner] == WaterwayTile.Empty) return corner;
        if (board[4] == WaterwayTile.Empty) return 4;
        foreach (int edge in Edges)
            if (board[edge] == WaterwayTile.Empty) return edge;
        return null;
    }

    private static int? Completion(IReadOnlyList<WaterwayTile> board, WaterwayTile player)
    {
        foreach (int[] line in Lines)
        {
            int count = 0, empty = -1;
            foreach (int pos in line)
            {
                if (board[pos] == player) count++;
                if (board[pos] == WaterwayTile.Empty) empty = pos;
            }
            if (count == 2 && empty >= 0)
                return empty;
        }
        return null;
    }
}

// No optimistic board updates: only confirmed observations may change the tracked board.
internal sealed class WaterwayBoardTracker
{
    private WaterwayTile[]? _board;
    private DateTimeOffset _lastObservedAt = DateTimeOffset.MinValue;
    public int BoardRevision { get; private set; }
    public int? LastConfirmedOwnCorner { get; private set; }
    public bool CenterXObserved { get; private set; }
    public IReadOnlyList<WaterwayTile>? Board => _board is null ? null :
        Array.AsReadOnly((WaterwayTile[])_board.Clone());

    public bool TryObserve(IReadOnlyList<WaterwayTile> cells, WaterwayEvidence proof,
        WaterwayEvidence? resetProof = null)
    {
        bool verifiedBoardReset = resetProof.HasValue && resetProof.Value.IsDirect &&
            resetProof.Value.ObservedAt >= _lastObservedAt &&
            resetProof.Value.ObservedAt <= proof.ObservedAt;
        if (!proof.IsDirect || proof.ObservedAt < _lastObservedAt ||
            (resetProof.HasValue && !verifiedBoardReset) ||
            cells.Count != 9 || cells.Any(x => x == WaterwayTile.Unknown))
            return false;
        if (_board is not null && !verifiedBoardReset)
        {
            for (int i = 0; i < 9; i++)
                if (_board[i] != WaterwayTile.Empty && _board[i] != cells[i])
                    return false;
        }
        if (verifiedBoardReset) LastConfirmedOwnCorner = null;
        _board = cells.ToArray();
        CenterXObserved |= _board[4] == WaterwayTile.X;
        BoardRevision++;
        _lastObservedAt = proof.ObservedAt;
        return true;
    }

    public bool ConfirmOwnO(int cell, WaterwayEvidence placement, bool selectedPlayerWasMe)
    {
        if (!selectedPlayerWasMe || !placement.IsDirect ||
            placement.ObservedAt < _lastObservedAt ||
            _board is null || cell < 0 || cell >= 9 ||
            _board[cell] != WaterwayTile.O)
            return false;
        if (cell is 0 or 2 or 6 or 8) LastConfirmedOwnCorner = cell;
        return true;
    }

    public void ResetForNewEntry()
    {
        _board = null;
        BoardRevision = 0;
        _lastObservedAt = DateTimeOffset.MinValue;
        LastConfirmedOwnCorner = null;
        CenterXObserved = false;
    }
}

[Flags]
internal enum WaterwayKeys { None = 0, W = 1, A = 2, S = 4, D = 8 }
internal readonly record struct WaterwayKeyChange(WaterwayKeys Release, WaterwayKeys Press);

// Screen axes must NOT be assumed equal to world axes. W and D are observed world deltas.
internal sealed class WaterwayCameraCalibration
{
    private Vector2 _w, _d;
    public bool Valid { get; private set; }

    public bool TryCalibrate(Vector2 measuredW, Vector2 measuredD)
    {
        Invalidate();
        if (!float.IsFinite(measuredW.X) || !float.IsFinite(measuredW.Y) ||
            !float.IsFinite(measuredD.X) || !float.IsFinite(measuredD.Y))
            return false;
        float det = measuredW.X * measuredD.Y - measuredW.Y * measuredD.X;
        if (MathF.Abs(det) < 0.000001f) return false;
        _w = measuredW;
        _d = measuredD;
        Valid = true;
        return true;
    }

    public bool TryResolve(Vector2 worldDelta, out WaterwayKeys keys)
    {
        keys = WaterwayKeys.None;
        if (!Valid || !float.IsFinite(worldDelta.X) || !float.IsFinite(worldDelta.Y))
            return false;
        float determinant = _w.X * _d.Y - _w.Y * _d.X;
        float forward = (worldDelta.X * _d.Y - worldDelta.Y * _d.X) / determinant;
        float right = (_w.X * worldDelta.Y - _w.Y * worldDelta.X) / determinant;
        if (MathF.Abs(forward) > 0.0001f)
            keys |= forward > 0 ? WaterwayKeys.W : WaterwayKeys.S;
        if (MathF.Abs(right) > 0.0001f)
            keys |= right > 0 ? WaterwayKeys.D : WaterwayKeys.A;
        return true;
    }

    public void Invalidate() => Valid = false;
}

// Diff-based held-key contract. The actual input transport MUST implement sustained key-down/up
// with guaranteed key-up on stop/disposal, rather than repeated TapScanCode calls.
internal sealed class WaterwayHeldKeyPlan
{
    public WaterwayKeys Held { get; private set; }

    public WaterwayKeyChange Update(WaterwayKeys desired,
        bool observedUserControl, bool positionStale, bool cameraUnverified,
        bool uiInterrupt = false)
    {
        if (observedUserControl || positionStale || cameraUnverified || uiInterrupt)
            desired = WaterwayKeys.None;

        WaterwayKeys release = Held & ~desired;
        WaterwayKeys press = desired & ~Held;
        Held = desired;
        return new WaterwayKeyChange(release, press);
    }

    public WaterwayKeyChange ReleaseAll() => Update(WaterwayKeys.None, false, false, false);
}

internal enum WaterwayAssistState { Unknown, On, Off }

// H is a TOGGLE. Never press it when the currently observed state is unknown,
// or again while awaiting confirmation of the previous toggle.
internal sealed class WaterwayAssistGate
{
    private WaterwayAssistState _pendingTarget = WaterwayAssistState.Unknown;

    public bool ShouldTapH(WaterwayAssistState observed, WaterwayAssistState desired)
    {
        if (observed == WaterwayAssistState.Unknown || desired == WaterwayAssistState.Unknown)
            return false;
        if (observed == desired)
        {
            _pendingTarget = WaterwayAssistState.Unknown;
            return false;
        }
        if (_pendingTarget != WaterwayAssistState.Unknown)
            return false;
        _pendingTarget = desired;
        return true;
    }

    public bool AwaitingConfirmation => _pendingTarget != WaterwayAssistState.Unknown;
    public void ResetOnVerifiedNewEntry() => _pendingTarget = WaterwayAssistState.Unknown;
}
