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

// Read-only, per-facility hold: weight is expressed in tenths of a percent
// (90.0% = 900) to preserve the original 90% boundary without inventing item
// weights. It is NOT a material, registration ticket or inventory authority.
internal static class GatheringWeightLaneHoldPolicy
{
    internal const string Category = "gathering_bag_capacity";
    internal const string Item = "가방 무게";
    internal const long ThresholdTenths = 900;

    internal static MaterialSupplyHold Create(GatheringWeightSnapshot weight)
    {
        if (!weight.MustStop)
            throw new InvalidOperationException("90% 미만 가방 무게로 보류를 만들 수 없습니다.");
        long observed = checked((long)Math.Floor(weight.FillPercent * 10m));
        return new MaterialSupplyHold(Item, ThresholdTenths,
            Math.Max(ThresholdTenths, observed), Category);
    }

    internal static bool IsHold(MaterialSupplyHold evidence)
        => evidence.Category == Category &&
           evidence.Item == Item && evidence.Required == ThresholdTenths &&
           evidence.Observed >= ThresholdTenths && evidence.Observed <= 1000;
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
