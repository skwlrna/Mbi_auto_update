using FishingAutomation;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private InventoryLootTracker? _inventoryLoot;
    private bool _inventoryNextRoundClicked;

    private void InvalidateInventoryLootRound()
    {
        _inventoryLoot?.Invalidate();
        _inventoryNextRoundClicked = false;
        _abyssLootCountedForCurrentResult = false;
    }

    private async Task BeginInventoryLootRoundAsync(CancellationToken ct)
    {
        _inventoryNextRoundClicked = false;
        _abyssLootCountedForCurrentResult = false;
        bool ready = _inventoryLoot is not null && await _inventoryLoot.BeginRoundAsync(ct);
        Log?.Invoke(ready ? "[어비스 전리품 CLI] 입장 전 기준 수량 저장" : "[어비스 전리품 CLI] 입장 전 조회 실패 -> 이번 판 확인 불가");
    }

    private async Task CountInventoryLootAsync(CancellationToken ct)
    {
        if (_abyssLootCountedForCurrentResult) return;
        _abyssLootCountedForCurrentResult = true;
        var result = _inventoryLoot is null ? null : await _inventoryLoot.CompleteRoundAsync(ct);
        if (result is null || !result.Confirmed)
        {
            Log?.Invoke($"[어비스 전리품 CLI] 확인 불가 ({result?.Reason ?? "cli_unavailable"}) -> 기존 다시 하기 흐름 계속");
            return;
        }
        LootStats.RecordAmounts(result.Gains);
        Log?.Invoke(result.Gains.Count == 0 ? "[어비스 전리품 CLI] 수량 변화 없음 -> 카운트 +0" :
            "[어비스 전리품 CLI] 이번 판 획득: " + string.Join(", ", result.Gains.Select(x => $"{LootStats.GetDisplayName(x.Key)} +{x.Value}")));
    }

    private async Task PrepareInventoryLootRetryAsync(CancellationToken ct)
    {
        bool ready = _inventoryLoot is not null && await _inventoryLoot.PrepareNextRoundAsync(ct);
        Log?.Invoke(ready ? "[어비스 전리품 CLI] 다시 하기 전 다음 판 기준 수량 저장" : "[어비스 전리품 CLI] 다음 판 기준 조회 실패 -> 다음 판 확인 불가");
    }

    private void CommitInventoryLootRetry()
    {
        if (!_inventoryNextRoundClicked) return;
        _inventoryLoot?.CommitNextRound();
        _inventoryNextRoundClicked = false;
        _abyssLootCountedForCurrentResult = false;
    }
}
