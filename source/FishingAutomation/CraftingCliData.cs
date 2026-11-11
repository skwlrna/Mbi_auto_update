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
        if (!response.Success || response.Data is null ||
            response.Data.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidDataException("가방 품목 수량 조회에 실패했습니다.");
        return ParseInventoryOnlyCount(response.Data.Value, displayName);
    }

    internal async Task<long> InventoryOnlyCountWithLoadingRetryAsync(
        string displayName,
        CancellationToken ct,
        Action<string>? log = null,
        int maxTransientRejects = 20)
    {
        var counts = await InventoryOnlyCountsWithLoadingRetryAsync(
            new[] { displayName }, ct, log, maxTransientRejects);
        return counts[displayName];
    }

    internal async Task<long> MaterialCountWithLoadingRetryAsync(
        string name, CancellationToken ct, Action<string>? log = null)
        => (await MaterialCountsWithLoadingRetryAsync(new[] { name }, ct, log))[name];

    // Food inputs include the bag, character storage and shared account storage.
    internal Task<IReadOnlyDictionary<string, long>> MaterialCountsWithLoadingRetryAsync(
        IEnumerable<string> names, CancellationToken ct, Action<string>? log = null)
        => CountsWithLoadingRetryAsync(names, ct, log, 20, true);

    // One fresh get_items response for all materials; never mix snapshots or
    // interpret a failed read as zero stock.
    internal async Task<IReadOnlyDictionary<string, long>> InventoryOnlyCountsWithLoadingRetryAsync(
        IEnumerable<string> displayNames, CancellationToken ct,
        Action<string>? log = null, int maxTransientRejects = 20)
        => await CountsWithLoadingRetryAsync(displayNames, ct, log, maxTransientRejects, false);

    private async Task<IReadOnlyDictionary<string, long>> CountsWithLoadingRetryAsync(
        IEnumerable<string> displayNames, CancellationToken ct,
        Action<string>? log, int maxTransientRejects, bool includeStorage)
    {
        if (maxTransientRejects < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTransientRejects));
        var names = displayNames.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToArray();
        int rejects = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await _cli.GetItemsAsync(ct);
            if (response.Success && response.Data is not null &&
                response.Data.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                if (!includeStorage)
                    return names.ToDictionary(name => name,
                        name => ParseInventoryOnlyCount(response.Data.Value, name), StringComparer.Ordinal);
                var aliases = names.ToDictionary(x => x, x => x, StringComparer.Ordinal);
                var totals = InventoryLootTracker.Parse(response.Data.Value, aliases)
                    ?? throw new InvalidDataException("가방/보관함 재료 수량 형식 오류 · 부족/0개로 판정하지 않습니다.");
                foreach (string name in names)
                {
                    long At(string location) => response.Data.Value.EnumerateArray()
                        .Where(x => System.Text.RegularExpressions.Regex.Replace(x.GetProperty("DisplayName").GetString()!,
                            @"</?color(?:=[^<>]*)?>", "").Trim() == name &&
                                    x.GetProperty("Location").GetString() == location)
                        .Sum(x => x.GetProperty("Count").GetInt64());
                    log?.Invoke($"[제작][재료 합산] {name} · 가방={At("inventory")} · " +
                        $"캐릭터 보관함={At("character_storage")} · 공용 보관함={At("account_storage")} · 합계={totals[name]}");
                }
                return totals;
            }

            if (!CliAutomationGuards.IsTransientLoadingRejection(response))
                throw new InvalidDataException("가방 품목 수량 조회 실패 · 재료 부족/0개로 판정하지 않습니다.");

            rejects++;
            if (rejects > maxTransientRejects)
                throw new InvalidDataException(
                    $"가방 품목 수량 조회가 지역 이동/로딩 중 {maxTransientRejects}회 연속 거절되어 정지합니다.");
            if (rejects == 1 || rejects % 5 == 0)
                log?.Invoke($"[제작] 지역 이동/채집 중 get_items CLI 일시 거부 · 재시도 {rejects}회");
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    private static long ParseInventoryOnlyCount(
        System.Text.Json.JsonElement items,
        string displayName)
    {
        long total = 0;
        foreach (var item in items.EnumerateArray())
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
