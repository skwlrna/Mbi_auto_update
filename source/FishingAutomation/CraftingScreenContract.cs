using FishingAutomation;

namespace DungeonVisionBot;

internal sealed record CraftingQuestDeficit(string DisplayName, long Current, long Required, int RowY);

internal interface ICraftingScreen : IDisposable
{
    string InputMode { get; }
    event Action<string>? Log;
    Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct);
    Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan plan, CancellationToken ct);
    Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct);
    Task CloseOverlayAsync(CancellationToken ct);
    Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct);
}
