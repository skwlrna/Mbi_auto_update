using DungeonVisionBot;
using FishingAutomation;
using System.Text.Json;

internal static class CraftingFoodMaterialIntegrationTests
{
    internal static async Task RunAsync(Action<bool, string> check, AppLog log)
    {
        await VerifyBlockedMaterialAsync("소금", 2, 0, check, log);
        await VerifyBlockedMaterialAsync("치즈", 3, 0, check, log);
        await VerifyBlockedMaterialAsync("셀러리", 2, 0, check, log, "셀러리");
        await VerifyBlockedMaterialAsync("생강", 2, 0, check, log, "생강");
        await VerifyBlockedMaterialAsync("조각 초콜릿", 5, 0, check, log, "조각 초콜릿");
        await VerifyBlockedMaterialAsync("카레 가루", 2, 0, check, log, "카레 가루");
        await VerifyBlockedMaterialAsync("파이크퍼치", 4, 0, check, log, "파이크퍼치");
        await VerifyBlockedMaterialAsync("숙성된 커다란 고기", 1, 0, check, log, "커다란 고기");
        await VerifyBlockedMaterialAsync("정체불명 특수 식재료", 1, 0, check, log,
            "정체불명 특수 식재료");
    }

    private static async Task VerifyBlockedMaterialAsync(
        string questMaterial, int required, long ownedSalt,
        Action<bool, string> check, AppLog log, string expectedName = "소금")
    {
        var screen = new ShopShortageScreen(questMaterial, required);
        string ReadResponse(string command) => command switch
        {
            "get_items" => JsonSerializer.Serialize(new[]
            {
                new { DisplayName = "야채볶음", Location = "inventory", Count = 10L },
                new { DisplayName = "소금", Location = "inventory", Count = ownedSalt }
            }),
            "get_gatherable_items" => "{\"items\":[]}",
            "get_craftable_items" => JsonSerializer.Serialize(new
            {
                items = new[] { new { DisplayName = "야채볶음", ProducedPerCraft = 1, Craftable = false } }
            }),
            "get_activity" => JsonSerializer.Serialize(new
            {
                IsDead = false, IsReviving = false, IsInCombat = false,
                IsAutoPlaying = false, IsAutoTraveling = false,
                IsDialoguePlaying = false, IsWaitingForSelection = false,
                Dungeon = new { State = "NotInDungeon" },
                Battlefield = new { IsInBattleField = false },
                Tutorial = new { IsPlaying = false },
                Scenario = new { IsInScenario = false },
                Performance = new { IsPlaying = false },
                Mode = new
                {
                    IsPlayingMiniGame = false, IsHousingEditMode = false,
                    MainButtonState = "Idle"
                },
                Interaction = new
                {
                    HasTarget = false,
                    AvailableInteractionType = "None",
                    LastRunningInteractionType = "None"
                }
            }),
            _ => throw new InvalidOperationException("Unexpected command: " + command)
        };
        var cli = new MabinogiMobileCli(log, true, (command, _) =>
            Task.FromResult(new CliProcessOutput(0, ReadResponse(command), "")));
        cli.RunFilteredQuery = (args, _) =>
            Task.FromResult(new CliProcessOutput(0, ReadResponse(args[0]), ""));
        var automation = new CraftingAutomation(
            new CraftingCliData(cli), new GatheringCliData(cli),
            screen, null!, (delay, _) => Task.CompletedTask);
        try
        {
            await automation.RunAsync(
                new CraftingPlan(CraftingCategory.Food, "야채볶음", 1, 1),
                CancellationToken.None);
            throw new Exception("Known purchased shortage was not skipped: " + questMaterial);
        }
        catch (CraftingMaterialAcquisitionException ex)
        {
            check(ex.MaterialName == expectedName && screen.RecoveryCount == 1 &&
                  screen.GatherCount == 0 && screen.CraftCount == 0,
                "real food engine skips without gathering " + questMaterial +
                ", safely recovers once, and surfaces material-only skip");
        }
    }

    private sealed class ShopShortageScreen : ICraftingScreen
    {
        private readonly string _material;
        private readonly int _required;
        internal int RecoveryCount;
        internal int GatherCount;
        internal int CraftCount;
        internal ShopShortageScreen(string material, int required)
        {
            _material = material;
            _required = required;
        }
        public string InputMode => "test";
        public event Action<string>? Log { add { } remove { } }
        public Task CreateQuestAsync(CraftingPlan plan, int count, CancellationToken ct)
            => Task.CompletedTask;
        public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
            CraftingPlan plan, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CraftingQuestDeficit>>(
                new[] { new CraftingQuestDeficit(_material, 0, _required, 712) });
        public Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
        {
            GatherCount++;
            throw new Exception("Shop-only material must never be gathered");
        }
        public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct)
        {
            RecoveryCount++;
            return Task.CompletedTask;
        }
        public Task CloseOverlayAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ReturnToStationAndCraftAsync(
            CraftingPlan plan, int count, CancellationToken ct)
        {
            CraftCount++;
            throw new Exception("Purchased-input shortage must block crafting");
        }
        public void Dispose() { }
    }
}
