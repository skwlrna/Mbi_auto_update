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
        Reentering
    }

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
            if (next == AbyssFlowState.CombatClearWait && previous == AbyssFlowState.Reentering)
                _abyssCombatStartedAt = null;
        }

        if (previous != next)
            Log?.Invoke($"[어비스 상태] {previous} -> {next} · {reason}");
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
