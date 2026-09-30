namespace FishingAutomation;

internal sealed class AlteringCliData : IAlteringData
{
    private readonly MabinogiMobileCli _cli;
    internal AlteringCliData(MabinogiMobileCli cli) => _cli = cli;
    public async Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
        => AlteringQueries.ParseRecipes(await _cli.GetAlterableItemsAsync(ct));
    public async Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
        => AlteringQueries.ParseWorks(await _cli.GetAlteringWorksAsync(ct));
    public async Task<long> ItemCountAsync(string name, CancellationToken ct)
    {
        var response = await _cli.GetItemsAsync(ct);
        if (!response.Success || response.Data is null) throw new InvalidDataException("인벤토리 조회 실패");
        var total = InventoryLootTracker.Parse(response.Data.Value, new Dictionary<string, string> { ["output"] = name });
        return total is not null ? total["output"] : throw new InvalidDataException("인벤토리 수량 파싱 실패");
    }
}
