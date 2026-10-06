namespace FishingAutomation;

internal static class AlteringEtaEstimator
{
    internal static long? Estimate(
        int requiredWorks,
        int queuedWorks,
        int activeItemSlots,
        long? batchRemainingSeconds,
        long? estimatedWorkSeconds)
    {
        if (requiredWorks <= 0 || queuedWorks < 0 || queuedWorks > requiredWorks)
            return null;

        int futureWorks = Math.Max(0, requiredWorks - queuedWorks);
        long? cycle = estimatedWorkSeconds ?? batchRemainingSeconds;

        if (futureWorks == 0)
            return batchRemainingSeconds ?? 0;

        if (!cycle.HasValue || cycle.Value <= 0)
            return null;

        int slots = Math.Max(1, activeItemSlots);
        long futureCycles = (futureWorks + (long)slots - 1) / slots;
        long currentBatch = Math.Max(0, batchRemainingSeconds ?? 0);

        try
        {
            return checked(currentBatch + futureCycles * cycle.Value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}

internal sealed record AlteringStatusItem(
    string Key,
    string DisplayName,
    long ConfirmedQuantity,
    int TargetQuantity,
    long? RemainingSeconds,
    DateTimeOffset UpdatedAt)
{
    internal long? RemainingNow(DateTimeOffset now)
    {
        if (ConfirmedQuantity >= TargetQuantity)
            return 0;
        if (!RemainingSeconds.HasValue)
            return null;

        long elapsed = (long)Math.Max(0, Math.Floor((now - UpdatedAt).TotalSeconds));
        return Math.Max(0, RemainingSeconds.Value - elapsed);
    }
}

internal static class AlteringStatusFormatter
{
    internal static string FormatRemaining(long? seconds, bool completed)
    {
        if (completed)
            return "완료";
        if (!seconds.HasValue)
            return "계산 중";
        if (seconds.Value <= 0)
            return "완료 확인 중";

        long value = seconds.Value;
        if (value >= 3600)
            return $"{value / 3600}시간 {(value % 3600) / 60}분";
        if (value >= 60)
            return $"{value / 60}분 {value % 60}초";
        return $"{value}초";
    }

    internal static string Format(
        IEnumerable<AlteringStatusItem> items,
        long totalCurrent,
        long totalTarget,
        DateTimeOffset now)
    {
        var rows = items.ToArray();
        if (rows.Length == 0)
            return "";

        var lines = new List<string>
        {
            $"가공 진행: 전체 {totalCurrent:N0}/{totalTarget:N0} 완료"
        };

        foreach (var item in rows)
        {
            bool completed = item.ConfirmedQuantity >= item.TargetQuantity;
            lines.Add(
                $"{item.DisplayName}: {item.ConfirmedQuantity:N0}/{item.TargetQuantity:N0} 완료 · " +
                $"남은시간(예상) {FormatRemaining(item.RemainingNow(now), completed)}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
