namespace FishingAutomation;

// Conservative capacity limit for free-screen gathering. CLI supplies current and
// maximum *bag* weights, but not a verified per-material unit weight; therefore
// do not invent a quantity that is safe to gather or discard items automatically.
internal static class GatheringWeightPolicy
{
    internal const decimal WarningFraction = 0.80m;
    internal const decimal SafetyFraction = 0.90m;

    internal static GatheringWeightSnapshot Read(decimal current, decimal maximum)
    {
        if (current < 0m || maximum <= 0m)
            throw new InvalidDataException("가방 무게 CLI 결과가 올바르지 않습니다.");
        return new GatheringWeightSnapshot(current, maximum);
    }
}

internal readonly record struct GatheringWeightSnapshot(decimal Current, decimal Maximum)
{
    internal decimal FillPercent => Math.Round(Current / Maximum * 100m, 1);
    internal decimal RemainingToSafetyLimit =>
        Math.Max(0m, Maximum * GatheringWeightPolicy.SafetyFraction - Current);
    internal bool ShouldWarn => Current >= Maximum * GatheringWeightPolicy.WarningFraction;
    internal bool MustStop => Current >= Maximum * GatheringWeightPolicy.SafetyFraction;
}

internal sealed class GatheringBagCapacityException : InvalidOperationException
{
    internal long RemainingQuantity { get; }

    internal GatheringBagCapacityException(
        string itemName, long remainingQuantity, GatheringWeightSnapshot weight)
        : base($"가방 무게 안전 한도 90% 도달 · {weight.Current}/{weight.Maximum} ({weight.FillPercent}%) · " +
               $"{itemName} 남은 수량 {remainingQuantity}개 · 채집을 정지합니다. " +
               "가방 무게를 확보한 뒤 실제 재고를 다시 확인하고 이어서 진행하세요.")
    {
        RemainingQuantity = remainingQuantity;
    }
}
