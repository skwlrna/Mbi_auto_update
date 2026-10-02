namespace FishingAutomation;

internal sealed record CraftingPlan(
    CraftingCategory Category,
    string DisplayName,
    int TargetQuantity,
    int ProducedPerCraft)
{
    internal int RequiredCrafts => CraftingQueries.RequiredCrafts(TargetQuantity, ProducedPerCraft);
    internal long ExpectedQuantity => (long)RequiredCrafts * ProducedPerCraft;

    internal void Validate()
    {
        if (Category == CraftingCategory.Unknown ||
            string.IsNullOrWhiteSpace(DisplayName) ||
            TargetQuantity is < 1 or > 1_000_000 ||
            ProducedPerCraft < 1)
            throw new InvalidDataException("제작 설정이 올바르지 않습니다.");
    }
}
