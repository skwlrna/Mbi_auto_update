namespace FishingAutomation;

/// <summary>
/// Sequencing layer for user-selected altering jobs. It never owns screen or
/// keyboard input; every job is delegated to the existing single-plan altering
/// runner so the verified 7-slot, recursive supply, collection, and stop rules
/// stay authoritative.
/// </summary>
internal sealed class MultiAlteringCoordinator
{
    internal event Action<string>? Log;

    internal async Task RunAsync(
        IReadOnlyList<AlteringPlan> plans,
        Func<AlteringPlan, CancellationToken, Task> runSingle,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(runSingle);
        if (plans.Count == 0)
            throw new InvalidOperationException("다중가공 작업 목록이 비어 있습니다.");

        var seen = new HashSet<(string Facility, string Display, int Ordinal)>();
        foreach (var plan in plans)
        {
            plan.Validate();
            var key = (plan.FacilityName, plan.DisplayName, plan.RecipeOrdinal);
            if (!seen.Add(key))
                throw new InvalidOperationException(
                    $"다중가공 작업 목록에 같은 제법이 중복되어 있습니다: {plan.DisplayName}");
        }

        Log?.Invoke(
            $"[다중가공] 계획 확정 · {plans.Count}종 · " +
            string.Join(" → ", plans.Select(x => $"{x.DisplayName} {x.TargetQuantity}개")));

        for (int index = 0; index < plans.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var plan = plans[index];
            Log?.Invoke(
                $"[다중가공] {index + 1}/{plans.Count} 시작 · " +
                $"{plan.ScreenTitle} · {plan.DisplayName} {plan.TargetQuantity}개");

            await runSingle(plan, ct);

            ct.ThrowIfCancellationRequested();
            Log?.Invoke(
                $"[다중가공] {index + 1}/{plans.Count} 완료 · {plan.DisplayName}");
        }

        Log?.Invoke("[다중가공] 전체 작업 완료");
    }
}
