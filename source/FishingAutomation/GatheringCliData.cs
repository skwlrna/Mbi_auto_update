namespace FishingAutomation;

internal sealed class GatheringCliData(MabinogiMobileCli cli) : IGatheringData
{
    public async Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct)
        => GatheringQueries.ParseCatalog(await cli.GetGatherableItemsAsync(ct));
    public async Task<GatheringActivity> ActivityAsync(CancellationToken ct)
        => GatheringQueries.ParseActivity(await cli.GetActivityAsync(ct));
    public async Task<(decimal Current, decimal Maximum)> WeightAsync(CancellationToken ct)
        => GatheringQueries.ParseWeight(await cli.GetInventoryAsync(ct));
    public async Task<long> ItemCountAsync(string name, CancellationToken ct)
    {
        var response = await cli.GetItemsAsync(ct);
        if(!response.Success || response.Data is null) throw new InvalidDataException("채집 수량 조회에 실패했습니다.");
        var total = InventoryLootTracker.Parse(response.Data.Value,
            new Dictionary<string,string>(StringComparer.Ordinal) { [name] = name });
        return total is not null ? total[name] : throw new InvalidDataException("채집 수량 형식이 올바르지 않습니다.");
    }
}
