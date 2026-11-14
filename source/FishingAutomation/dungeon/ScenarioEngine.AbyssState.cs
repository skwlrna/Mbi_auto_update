namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private enum AbyssFlowState
    {
        Unknown = 0,
        CombatClearWait,
        ClearConfirmed,
        TouchReady,
        TouchClicked,
        ResultConfirmed,
        RetryClicked,
        Reentering,
        Recruiting
    }

    private sealed class AbyssResumeVerifiedCombatException : Exception { }
    private int _abyssFalseResultCombatRecoveries;
    private readonly object _abyssFlowStateLock = new();
    private AbyssFlowState _abyssFlowState = AbyssFlowState.Unknown;

    private static bool AbyssCanTransition(AbyssFlowState from, AbyssFlowState to)
    {
        if (from == to)
            return true;

        return (from, to) switch
        {
            // A direct confirmed clear/result screen is valid during recovery,
            // where the process may attach after the normal earlier states.
            (AbyssFlowState.Unknown, AbyssFlowState.CombatClearWait) => true,
            (AbyssFlowState.Unknown, AbyssFlowState.ClearConfirmed) => true,
            (AbyssFlowState.Unknown, AbyssFlowState.ResultConfirmed) => true,

            (AbyssFlowState.CombatClearWait, AbyssFlowState.ClearConfirmed) => true,
            (AbyssFlowState.CombatClearWait, AbyssFlowState.ResultConfirmed) => true,

            (AbyssFlowState.ClearConfirmed, AbyssFlowState.TouchReady) => true,
            (AbyssFlowState.ClearConfirmed, AbyssFlowState.ResultConfirmed) => true,

            (AbyssFlowState.TouchReady, AbyssFlowState.TouchClicked) => true,
            (AbyssFlowState.TouchReady, AbyssFlowState.ResultConfirmed) => true,

            (AbyssFlowState.TouchClicked, AbyssFlowState.ResultConfirmed) => true,

            (AbyssFlowState.ResultConfirmed, AbyssFlowState.RetryClicked) => true,
            (AbyssFlowState.RetryClicked, AbyssFlowState.Reentering) => true,
            (AbyssFlowState.Reentering, AbyssFlowState.CombatClearWait) => true,
            (AbyssFlowState.Reentering, AbyssFlowState.Recruiting) => true,
            (AbyssFlowState.Recruiting, AbyssFlowState.CombatClearWait) => true,

            _ => false
        };
    }

    private AbyssFlowState GetAbyssFlowState()
    {
        lock (_abyssFlowStateLock)
            return _abyssFlowState;
    }

    private string AbyssFlowStateText()
    {
        return GetAbyssFlowState().ToString();
    }

    private void AbyssResetFlowState(string reason)
    {
        if (!IsAbyss)
            return;

        ResetAbyssExitState();
        _abyssCombatStartedAt = null;
        _abyssFalseResultCombatRecoveries = 0;
        AbyssFlowState previous;
        lock (_abyssFlowStateLock)
        {
            previous = _abyssFlowState;
            _abyssFlowState = AbyssFlowState.Unknown;
        }

        Log?.Invoke($"[어비스 상태] RESET {previous} -> Unknown · {reason}");
    }

    private void AbyssTransitionTo(AbyssFlowState next, string reason)
    {
        if (!IsAbyss)
            return;

        AbyssFlowState previous;
        lock (_abyssFlowStateLock)
        {
            previous = _abyssFlowState;
            if (!AbyssCanTransition(previous, next))
            {
                throw new InvalidOperationException(
                    $"어비스 상태 전이 차단: {previous} -> {next} · {reason}");
            }

            _abyssFlowState = next;
            if (next == AbyssFlowState.CombatClearWait && (previous == AbyssFlowState.Reentering || previous == AbyssFlowState.Recruiting))
            {
                // A verified re-entry starts a NEW fight; only now may the
                // old round's recovery budget/timer be cleared.
                _abyssCombatStartedAt = null;
                _abyssFalseResultCombatRecoveries = 0;
            }
        }

        if (previous != next)
            Log?.Invoke($"[어비스 상태] {previous} -> {next} · {reason}");
    }

    // V3.3.19: a ResultConfirmed entered from reward OCR is not permission
    // to skip an independently verified, still-visible clear touch overlay.
    // The caller must prove both clear-title and bottom touch prompt in TWO
    // consecutive fresh frames before this narrowly scoped reversal.
    private void AbyssRestoreVerifiedClearTouchFromPrematureResult(string reason)
    {
        if (!IsAbyss) throw new InvalidOperationException("어비스 클리어 복구는 어비스에서만 허용됩니다.");
        lock (_abyssFlowStateLock)
        {
            if (_abyssFlowState != AbyssFlowState.ResultConfirmed)
                throw new InvalidOperationException(
                    $"검증된 클리어 터치 복귀 상태 불일치: {_abyssFlowState}");
            _abyssFlowState = AbyssFlowState.ClearConfirmed;
        }
        _abyssClearPairedEvidenceFrames = 0;
        Log?.Invoke("[어비스 상태] ResultConfirmed -> ClearConfirmed · " + reason);
    }

    // V3.3.22: ResultConfirmed was reached by a false screen classification.
    // Only the result step may call this after THREE independent, consecutive
    // active-combat HUD frames, with no valid retry button. Never reset the
    // original ten-minute clock, inventory journal, or scheduler input lease.
    private void AbyssRestoreVerifiedCombatFromPrematureResult(string reason)
    {
        if (!IsAbyss || !_abyssCombatStartedAt.HasValue)
            throw new InvalidOperationException("전투 상태 복귀에는 기존 어비스 전투 타이머가 필요합니다.");
        lock (_abyssFlowStateLock)
        {
            if (_abyssFlowState != AbyssFlowState.ResultConfirmed)
                throw new InvalidOperationException(
                    $"검증된 전투 복귀 상태 불일치: {_abyssFlowState}");
            if (++_abyssFalseResultCombatRecoveries > 3)
                throw new InvalidOperationException("한 판에서 결과 오인식 복구 3회 초과 -> 안전 정지합니다.");
            _abyssFlowState = AbyssFlowState.CombatClearWait;
        }
        _abyssClearPairedEvidenceFrames = 0;
        Log?.Invoke($"[어비스 상태] ResultConfirmed -> CombatClearWait · {reason} · 기존 전투 타이머 유지 · 복구={_abyssFalseResultCombatRecoveries}/3");
    }

    private void AbyssRequireState(AbyssFlowState expected, string operation)
    {
        if (!IsAbyss)
            return;

        AbyssFlowState current = GetAbyssFlowState();
        if (current != expected)
        {
            throw new InvalidOperationException(
                $"어비스 상태 불일치로 입력 차단: operation={operation}, expected={expected}, actual={current}");
        }
    }

    private void AbyssEnterCombatClearWait(string reason)
    {
        if (!IsAbyss)
            return;

        AbyssFlowState current = GetAbyssFlowState();
        if (current == AbyssFlowState.CombatClearWait)
            return;

        AbyssTransitionTo(AbyssFlowState.CombatClearWait, reason);
    }
}
