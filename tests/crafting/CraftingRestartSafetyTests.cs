using System.Text.Json;
using FishingAutomation;

internal static class CraftingRestartSafetyTests
{
    internal static void Run(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "mabi-crafting-restart-" + Guid.NewGuid().ToString("N"));
        var first = new CraftingPlan(CraftingCategory.Food, "가든 샐러드", 10, 1);
        var second = new CraftingPlan(CraftingCategory.Food, "감자수프", 10, 1);
        var identity = new CliIdentityContext("char-1", "test", null, "test-realm");
        try
        {
            using (var journal = new CraftingRestartSafetyJournal(directory))
            {
                check(journal.ReviewRequired is null,
                    "fresh crafting run has no prior restart obligation");
                journal.StartFresh(new[] { first }, identity);
                journal.BeginBatch(0, first, 3, 40, 3);
                check(journal.ReviewRequired?.PendingBatch is
                      { PlanIndex: 0, Attempts: 3, InventoryBefore: 40, ExpectedIncrease: 3 },
                    "before-click intent is durably recorded with original item and inventory");
                bool secondRunnerBlocked = false;
                try { using var secondRunner = new CraftingRestartSafetyJournal(directory); }
                catch (IOException) { secondRunnerBlocked = true; }
                check(secondRunnerBlocked,
                    "second concurrent crafting instance cannot take the journal lease");
                // Simulate F10 or process exit with an unconfirmed craft click.
                // Disposal never marks the run completed.
            }

            using (var recovered = new CraftingRestartSafetyJournal(directory))
            {
                var beforeReview = recovered.ReviewRequired;
                check(beforeReview?.PendingBatch is not null && !beforeReview.Completed,
                    "unconfirmed craft remains pending after process restart");
                bool refused = false;
                try { recovered.StartFresh(new[] { first }, identity); }
                catch (InvalidOperationException) { refused = true; }
                check(refused, "new F9 cannot silently replay previous target after crash");

                bool wrongConsentBlocked = false;
                try { recovered.StartFresh(new[] { first }, identity, Guid.NewGuid().ToString("N")); }
                catch (InvalidOperationException) { wrongConsentBlocked = true; }
                check(wrongConsentBlocked, "stale or unrelated manual consent cannot clear previous run");

                recovered.StartFresh(new[] { first }, identity, beforeReview!.RunId);
                string archived = Path.Combine(directory, "reviewed", beforeReview.RunId + ".json");
                check(File.Exists(archived) &&
                      JsonSerializer.Deserialize<CraftingRestartSnapshot>(
                          File.ReadAllText(archived))?.PendingBatch?.Attempts == 3,
                    "explicit NEW-run approval archives previous unresolved click evidence");
                check(recovered.ReviewRequired?.PendingBatch is null,
                    "acknowledged NEW run never imports old craft attempt or credit");

                recovered.BeginBatch(0, first, 2, 60, 2);
                bool missingProofBlocked = false;
                try { recovered.ConfirmBatch(0, first, 61); }
                catch (InvalidOperationException) { missingProofBlocked = true; }
                check(missingProofBlocked && recovered.ReviewRequired?.PendingBatch is not null,
                    "insufficient post-click inventory evidence cannot clear pending intent");
                recovered.ConfirmBatch(0, first, 62);
                recovered.MarkPlanCompleted(0, first);
                recovered.FinishQueue(1, 0);
                check(recovered.ReviewRequired is null,
                    "completed and fully confirmed queue clears restart-review gate");
            }

            using (var another = new CraftingRestartSafetyJournal(directory))
            {
                another.StartFresh(new[] { first, second }, identity);
                another.BeginBatch(0, first, 1, 1, 1);
                another.ConfirmBatch(0, first, 2);
                another.MarkPlanCompleted(0, first);
                another.MarkMaterialSkipped(1, second);
                another.FinishQueue(1, 1);
                check(another.ReviewRequired is { Completed: false } &&
                      another.ReviewRequired.SkippedIndices.SequenceEqual(new[] { 1 }),
                    "skipped material recipe is never falsely marked fully complete");
            }

            using (var pending = new CraftingRestartSafetyJournal(directory))
            {
                var old = pending.ReviewRequired!;
                pending.StartFresh(new[] { first }, identity, old.RunId);
                // F10 before the first craft still needs explicit review; the
                // fresh full target cannot be quietly executed a second time.
                check(pending.ReviewRequired is { PendingBatch: null, Completed: false },
                    "stopped-before-click crafting remains a reviewable incomplete plan");
            }
            using (var pending = new CraftingRestartSafetyJournal(directory))
            {
                check(pending.ReviewRequired is { Completed: false },
                    "F10 before first batch is retained across process restarts");
            }

            // Missing/corrupt proof is not equivalent to a blank journal.
            File.WriteAllText(Path.Combine(directory, "restart-safety.json"), "{not-json");
            bool corruptBlocked = false;
            try { using var broken = new CraftingRestartSafetyJournal(directory); }
            catch (InvalidDataException) { corruptBlocked = true; }
            check(corruptBlocked, "invalid journal fails closed; original bytes remain intact");
            check(File.ReadAllText(Path.Combine(directory, "restart-safety.json")) == "{not-json",
                "corrupted manifest is preserved for manual review");

            File.WriteAllText(Path.Combine(directory, "restart-safety.json.tmp"), "unfinished");
            bool tempBlocked = false;
            try { using var broken = new CraftingRestartSafetyJournal(directory); }
            catch (InvalidDataException) { tempBlocked = true; }
            check(tempBlocked, "incomplete atomic-write temp file blocks unsafe fresh run");
            check(File.Exists(Path.Combine(directory, "restart-safety.json.tmp")),
                "incomplete temp file is not silently deleted");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* Test-only temporary folder; no production file cleanup. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
