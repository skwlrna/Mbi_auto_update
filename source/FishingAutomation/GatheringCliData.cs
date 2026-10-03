namespace FishingAutomation;

internal sealed class GatheringCliData : IGatheringData
{
    private readonly MabinogiMobileCli _cli;

    internal GatheringCliData(MabinogiMobileCli cli) => _cli = cli;

    public async Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct)
        => GatheringQueries.ParseCatalog(await _cli.GetGatherableItemsAsync(ct));

    public async Task<GatheringActivity> ActivityAsync(CancellationToken ct)
    {
        int rejects = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await _cli.GetActivityAsync(ct);
            if (response.Success)
                return GatheringQueries.ParseActivity(response);
            if (!CliAutomationGuards.IsTransientLoadingRejection(response))
                return GatheringQueries.ParseActivity(response);

            rejects++;
            if (rejects > 20)
                throw new InvalidDataException(
                    "채집 상태 조회가 지역 이동/로딩 중 20회 연속 거절되어 정지합니다.");
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    public async Task<(decimal Current, decimal Maximum)> WeightAsync(CancellationToken ct)
    {
        int rejects = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await _cli.GetInventoryAsync(ct);
            if (response.Success)
                return GatheringQueries.ParseWeight(response);
            if (!CliAutomationGuards.IsTransientLoadingRejection(response))
                return GatheringQueries.ParseWeight(response);

            rejects++;
            if (rejects > 20)
                throw new InvalidDataException(
                    "가방 무게 조회가 지역 이동/로딩 중 20회 연속 거절되어 정지합니다.");
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    public async Task<long> ItemCountAsync(string name, CancellationToken ct)
    {
        int rejects = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await _cli.GetItemsAsync(ct);
            if (response.Success && response.Data is not null)
            {
                var total = InventoryLootTracker.Parse(
                    response.Data.Value,
                    new Dictionary<string,string>(StringComparer.Ordinal) { [name] = name });
                return total is not null
                    ? total[name]
                    : throw new InvalidDataException("채집 수량 형식이 올바르지 않습니다.");
            }

            if (!CliAutomationGuards.IsTransientLoadingRejection(response))
                throw new InvalidDataException("채집 수량 조회에 실패했습니다.");

            rejects++;
            if (rejects > 20)
                throw new InvalidDataException(
                    "채집 수량 조회가 지역 이동/로딩 중 20회 연속 거절되어 정지합니다.");
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }
}
