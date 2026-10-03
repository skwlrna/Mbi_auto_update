using DungeonVisionBot;
using FishingAutomation;
using System.Text.Json;

internal static class CraftingFlowTests
{
    internal static async Task RunAsync(Action<bool, string> check, AppLog log)
    {
        foreach (bool shortOutput in new[] { false, true })
        {
            var screen = new Screen { ShortOutput = shortOutput };
            int inventoryReads = 0;
            using var cancelled = new CancellationTokenSource();
            string Json(string command) => command switch
            {
                "get_items" => JsonSerializer.Serialize(new[] { new { DisplayName = "달걀", Location = "inventory", Count = screen.Stock } }),
                "get_craftable_items" => JsonSerializer.Serialize(new { items = new[] { new { DisplayName = "달걀", ProducedPerCraft = 1 } } }),
                _ => throw new Exception("unexpected CLI command: " + command)
            };
            var cli = new MabinogiMobileCli(log, true, (command, ct) =>
            {
                if (command == "get_items" && shortOutput && ++inventoryReads > 10) cancelled.Cancel();
                return Task.FromResult(new CliProcessOutput(0, Json(command), ""));
            });
            cli.RunFilteredQuery = (args, ct) => Task.FromResult(new CliProcessOutput(0, Json(args[0]), ""));
            var automation = new CraftingAutomation(new(cli), new(cli), screen, null!, (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
            try
            {
                await automation.RunAsync(new(CraftingCategory.Item, "달걀", 23, 1), cancelled.Token);
                check(!shortOutput && screen.Batches.SequenceEqual(new[] { 10, 10, 3 }) &&
                      automation.Gained == 23 && automation.CompletedCrafts == 23,
                    "real crafting engine verifies inventory for 10/10/3 batches");
            }
            catch (OperationCanceledException) when (shortOutput)
            {
                check(screen.Batches.SequenceEqual(new[] { 10 }) && automation.CompletedCrafts == 0,
                    "completion screen without full inventory gain cannot confirm or repeat a batch");
            }
        }

        {
            var screen = new RecoveringBatchScreen();
            string Json(string command) => command switch
            {
                "get_items" => JsonSerializer.Serialize(new[]
                {
                    new { DisplayName = "야채볶음", Location = "inventory", Count = screen.Stock }
                }),
                "get_craftable_items" => JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { DisplayName = "야채볶음", ProducedPerCraft = 1, Craftable = false,
                            MissingIngredients = Array.Empty<object>() }
                    }
                }),
                _ => throw new Exception("unexpected recovery-test CLI command: " + command)
            };
            var cli = new MabinogiMobileCli(log, true, (command, ct) =>
                Task.FromResult(new CliProcessOutput(0, Json(command), "")));
            cli.RunFilteredQuery = (args, ct) =>
                Task.FromResult(new CliProcessOutput(0, Json(args[0]), ""));

            var automation = new CraftingAutomation(
                new(cli), new(cli), screen, null!, (_, ct) => Task.CompletedTask);

            await automation.RunAsync(
                new(CraftingCategory.Food, "야채볶음", 20, 1),
                CancellationToken.None);

            check(screen.Batches.SequenceEqual(new[] { 10, 10, 10 }) &&
                  automation.CompletedCrafts == 20 &&
                  screen.CompletedBeforeRecovery == 10 &&
                  screen.RecoveryCalls == 1,
                "quest preparation recovery keeps confirmed prior batch, normalizes screen, and recreates only current batch");
        }

        {
            var screen = new NamelessDeficitScreen();
            string Json(string command) => command switch
            {
                "get_items" => JsonSerializer.Serialize(new[]
                {
                    new { DisplayName = "야채볶음", Location = "inventory", Count = screen.Stock }
                }),
                "get_craftable_items" => JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { DisplayName = "야채볶음", ProducedPerCraft = 1, Craftable = false,
                            MissingIngredients = Array.Empty<object>() }
                    }
                }),
                _ => throw new Exception(
                    "unnamed quest row must not require a material catalog: " + command)
            };
            var cli = new MabinogiMobileCli(log, true, (command, ct) =>
                Task.FromResult(new CliProcessOutput(0, Json(command), "")));
            cli.RunFilteredQuery = (args, ct) =>
                Task.FromResult(new CliProcessOutput(0, Json(args[0]), ""));

            var automation = new CraftingAutomation(
                new(cli), new(cli), screen, null!, (_, ct) => Task.CompletedTask);

            await automation.RunAsync(
                new(CraftingCategory.Food, "야채볶음", 1, 1),
                CancellationToken.None);

            check(screen.NamelessGathered &&
                  automation.CompletedCrafts == 1,
                "quest shortage row can be gathered when material-name OCR is unavailable");
        }

        int transientReads = 0;
        var retryCli = new MabinogiMobileCli(log, true, (command, ct) =>
        {
            if (command != "get_items")
                throw new Exception("unexpected retry-test CLI command: " + command);

            transientReads++;
            if (transientReads <= 2)
                return Task.FromResult(new CliProcessOutput(
                    0,
                    "{\"status\":\"rejected\",\"reason\":\"loading\"}",
                    ""));

            return Task.FromResult(new CliProcessOutput(
                0,
                JsonSerializer.Serialize(new[]
                {
                    new { DisplayName = "감자", Location = "inventory", Count = 80L }
                }),
                ""));
        });
        var retryData = new CraftingCliData(retryCli);
        long retriedCount = await retryData.InventoryOnlyCountWithLoadingRetryAsync(
            "감자", CancellationToken.None, null, 5);
        check(retriedCount == 80 && transientReads == 3,
            "quest material inventory retries transient loading cli_rejected instead of aborting");
    }

    private sealed class RecoveringBatchScreen : ICraftingScreen
    {
        internal long Stock = 50;
        internal int CompletedBeforeRecovery;
        internal int RecoveryCalls;
        internal List<int> Batches = new();
        private int _createCalls;
        private bool _failedSecondBatch;

        public string InputMode => "test";
        public event Action<string>? Log { add { } remove { } }

        public Task CreateQuestAsync(CraftingPlan plan, int count, CancellationToken ct)
        {
            _createCalls++;
            Batches.Add(count);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
            CraftingPlan plan,
            CancellationToken ct)
        {
            if (_createCalls == 2 && !_failedSecondBatch)
            {
                _failedSecondBatch = true;
                CompletedBeforeRecovery = 10;
                throw new InvalidOperationException("simulated quest OCR/read failure");
            }

            return Task.FromResult<IReadOnlyList<CraftingQuestDeficit>>(
                Array.Empty<CraftingQuestDeficit>());
        }

        public Task GatherQuestDeficitAsync(
            CraftingQuestDeficit deficit,
            CancellationToken ct)
            => throw new Exception("unexpected gather");

        public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct)
        {
            RecoveryCalls++;
            return Task.CompletedTask;
        }

        public Task CloseOverlayAsync(CancellationToken ct) => Task.CompletedTask;

        public Task ReturnToStationAndCraftAsync(
            CraftingPlan plan,
            int count,
            CancellationToken ct)
        {
            Stock += count;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class NamelessDeficitScreen : ICraftingScreen
    {
        internal long Stock = 10;
        internal bool NamelessGathered;
        private bool _servedDeficit;

        public string InputMode => "test";
        public event Action<string>? Log { add { } remove { } }

        public Task CreateQuestAsync(CraftingPlan plan, int count, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
            CraftingPlan plan,
            CancellationToken ct)
        {
            if (!_servedDeficit)
            {
                _servedDeficit = true;
                return Task.FromResult<IReadOnlyList<CraftingQuestDeficit>>(
                    new[] { new CraftingQuestDeficit("", 0, 60, 777) });
            }

            return Task.FromResult<IReadOnlyList<CraftingQuestDeficit>>(
                Array.Empty<CraftingQuestDeficit>());
        }

        public Task GatherQuestDeficitAsync(
            CraftingQuestDeficit deficit,
            CancellationToken ct)
        {
            NamelessGathered =
                string.IsNullOrWhiteSpace(deficit.DisplayName) &&
                deficit.Current == 0 &&
                deficit.Required == 60 &&
                deficit.RowY == 777;
            return Task.CompletedTask;
        }

        public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct) => Task.CompletedTask;

        public Task CloseOverlayAsync(CancellationToken ct) => Task.CompletedTask;

        public Task ReturnToStationAndCraftAsync(
            CraftingPlan plan,
            int count,
            CancellationToken ct)
        {
            Stock += count;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class Screen : ICraftingScreen
    {
        internal long Stock = 50;
        internal bool ShortOutput;
        internal List<int> Batches = new();
        public string InputMode => "test";
        public event Action<string>? Log { add { } remove { } }
        public Task CreateQuestAsync(CraftingPlan plan, int count, CancellationToken ct)
        { Batches.Add(count); return Task.CompletedTask; }
        public Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan plan, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CraftingQuestDeficit>>(Array.Empty<CraftingQuestDeficit>());
        public Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct) => throw new Exception("unexpected gather");
        public Task RecoverBatchPreparationAsync(CraftingPlan plan, CancellationToken ct) => Task.CompletedTask;

        public Task CloseOverlayAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ReturnToStationAndCraftAsync(CraftingPlan plan, int count, CancellationToken ct)
        { Stock += ShortOutput ? count - 1 : count; return Task.CompletedTask; }
        public void Dispose() { }
    }
}
