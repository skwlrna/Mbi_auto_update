namespace FishingAutomation;

public sealed partial class MainForm
{
    private readonly Dictionary<string, AlteringStatusItem> _alteringStatusItems =
        new(StringComparer.Ordinal);
    private readonly List<string> _alteringStatusOrder = new();

    private static string AlteringStatusKey(AlteringPlan plan)
        => $"{plan.FacilityName}\u001f{plan.DisplayName}\u001f{plan.RecipeOrdinal}";

    private void ResetAlteringStatus(IEnumerable<AlteringPlan> plans)
    {
        _alteringStatusItems.Clear();
        _alteringStatusOrder.Clear();
        var now = DateTimeOffset.Now;

        foreach (var plan in plans)
        {
            string key = AlteringStatusKey(plan);
            if (_alteringStatusItems.ContainsKey(key))
                continue;

            _alteringStatusOrder.Add(key);
            _alteringStatusItems[key] = new(
                key, plan.DisplayName, 0, plan.TargetQuantity, null, now);
        }
    }

    private void SeedAlteringStatus(
        AlteringPlan plan,
        long confirmedQuantity,
        long? remainingSeconds = null)
    {
        string key = AlteringStatusKey(plan);
        if (!_alteringStatusItems.ContainsKey(key))
            _alteringStatusOrder.Add(key);

        _alteringStatusItems[key] = new(
            key,
            plan.DisplayName,
            Math.Clamp(confirmedQuantity, 0, plan.TargetQuantity),
            plan.TargetQuantity,
            remainingSeconds,
            DateTimeOffset.Now);
    }

    private void UpdateAlteringStatus(AlteringPlan plan, AlteringProgress progress)
    {
        string key = AlteringStatusKey(plan);
        if (!_alteringStatusItems.ContainsKey(key))
            _alteringStatusOrder.Add(key);

        _alteringStatusItems[key] = new(
            key,
            plan.DisplayName,
            Math.Clamp(progress.ConfirmedQuantity, 0, progress.TargetQuantity),
            progress.TargetQuantity,
            progress.TotalRemainingSeconds ??
                progress.BatchRemainingSeconds ??
                progress.NextCompletionSeconds,
            DateTimeOffset.Now);
    }

    private string GetAlteringRemoteStatus()
    {
        var ordered = _alteringStatusOrder
            .Where(_alteringStatusItems.ContainsKey)
            .Select(key => _alteringStatusItems[key])
            .ToArray();

        return AlteringStatusFormatter.Format(
            ordered,
            _productionCurrentQuantity,
            _productionTargetQuantity,
            DateTimeOffset.Now);
    }
}
