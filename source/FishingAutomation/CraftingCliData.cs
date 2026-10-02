namespace FishingAutomation;

internal sealed class CraftingCliData
{
    private readonly MabinogiMobileCli _cli;
    internal CraftingCliData(MabinogiMobileCli cli) => _cli = cli;

    internal async Task<IReadOnlyList<CraftableItem>> CatalogAsync(CancellationToken ct)
        => CraftingQueries.ParseCatalog(await _cli.GetCraftableItemsAsync(ct));

    internal async Task<CraftableItem> ExactAsync(string displayName, CancellationToken ct)
        => CraftingQueries.Exact(
            CraftingQueries.ParseCatalog(await _cli.GetCraftableItemsAsync(displayName, ct)),
            displayName);

    internal async Task<long> ItemCountAsync(string displayName, CancellationToken ct)
    {
        var response = await _cli.GetItemsAsync(ct);
        if (!response.Success || response.Data is null)
            throw new InvalidDataException("제작 품목 보유 수량 조회에 실패했습니다.");
        var total = InventoryLootTracker.Parse(response.Data.Value,
            new Dictionary<string, string>(StringComparer.Ordinal) { [displayName] = displayName });
        return total is not null
            ? total[displayName]
            : throw new InvalidDataException("제작 품목 보유 수량 형식이 올바르지 않습니다.");
    }

    internal async Task<long> InventoryOnlyCountAsync(string displayName, CancellationToken ct)
    {
        var response = await _cli.GetItemsAsync(ct);
        if (!response.Success || response.Data is null || response.Data.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidDataException("가방 품목 수량 조회에 실패했습니다.");

        long total = 0;
        foreach (var item in response.Data.Value.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !item.TryGetProperty("DisplayName", out var name) ||
                !item.TryGetProperty("Location", out var location) ||
                !item.TryGetProperty("Count", out var count) ||
                name.ValueKind != System.Text.Json.JsonValueKind.String ||
                location.ValueKind != System.Text.Json.JsonValueKind.String ||
                !count.TryGetInt64(out long quantity) || quantity < 0)
                throw new InvalidDataException("가방 품목 수량 형식이 올바르지 않습니다.");

            if (string.Equals(name.GetString(), displayName, StringComparison.Ordinal) &&
                string.Equals(location.GetString(), "inventory", StringComparison.Ordinal))
                total = checked(total + quantity);
        }
        return total;
    }
}
