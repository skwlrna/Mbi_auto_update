using DungeonVisionBot;

namespace FishingAutomation;

internal sealed class CraftingQuestRowRecognitionException(string message) : InvalidOperationException(message);

internal sealed record CraftingFoodQuestRow(
    CraftingQuestDeficit Row, IReadOnlyList<CraftingQuestDeficit> Targets);

internal static class CraftingFoodBatchPolicy
{
    internal static async Task<(int Crafts, CraftingFoodPurchaseShortage? Blocker)> SelectBatchAsync(
        CraftingFoodRecipe recipe, int maximum,
        Func<IEnumerable<string>, CancellationToken, Task<IReadOnlyDictionary<string, long>>> readStock,
        CancellationToken ct)
    {
        if (maximum is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximum));
        var names = recipe.Ingredients.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>(names);
        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (CraftingFoodMaterialPolicy.TryGetProcessing(name, out var processing))
                foreach (var input in processing.Ingredients)
                    if (names.Add(input.Name)) pending.Enqueue(input.Name);
        }
        var stock = await readStock(names, ct);
        // Consume ready ingredients before planning any gathering or processing.
        int ready = maximum;
        CraftingFoodPurchaseShortage? readyLimit = null;
        foreach (var input in recipe.Ingredients)
        {
            if (!stock.TryGetValue(input.Name, out long owned) || owned < 0)
                throw new InvalidDataException("레시피 재료 CLI 재고 누락/형식 오류: " + input.Name);
            int capacity = (int)Math.Min(maximum, owned / input.Amount);
            if (capacity < ready)
            {
                ready = capacity;
                readyLimit = new(input.Name, input.Amount * maximum - owned, owned, recipe.DisplayName);
            }
        }
        if (ready > 0) return (ready, readyLimit);
        CraftingFoodPurchaseShortage? blocker = null;
        for (int count = maximum; count >= 1; count--)
        {
            ct.ThrowIfCancellationRequested();
            var roots = recipe.ForBatch(count).Select(x =>
                new CraftingQuestDeficit(x.Name, stock[x.Name], x.Amount, 0)).ToArray();
            var shortage = await CraftingFoodMaterialPolicy.FindFirstPurchaseShortageAsync(
                roots, (name, _) => Task.FromResult(stock[name]), ct);
            if (shortage is null) return (count, blocker);
            blocker = shortage;
        }
        return (0, blocker);
    }

    internal static IReadOnlyList<CraftingQuestDeficit> Deficits(
        IReadOnlyList<CraftingFoodIngredient> batch, IReadOnlyDictionary<string, long> inventory)
    {
        var result = new List<CraftingQuestDeficit>();
        foreach (var input in batch)
        {
            if (input.Amount <= 0 || !inventory.TryGetValue(input.Name, out long count) || count < 0)
                throw new InvalidDataException("레시피 재료 CLI 재고 누락/형식 오류: " + input.Name);
            if (count < input.Amount)
                result.Add(new(input.Name, count, input.Amount, 0));
        }
        return result;
    }

    // A shortage row need not have an OCR-readable name. Required amount and
    // current CLI stock bind it to recipe inputs. Equal counters may represent
    // several inputs: use that row only when EVERY possible input is gatherable,
    // then verify any one of those named inventories before re-reading ALL inputs.
    // Never infer a particular identity from screen order or pick a processing/shop row.
    internal static CraftingFoodQuestRow? FindGatherRow(
        string requested, IReadOnlyList<CraftingQuestDeficit> deficits,
        IReadOnlyList<CraftingQuestDeficit> visible, IReadOnlySet<string> supported)
    {
        var bindings = new List<CraftingFoodQuestRow>();
        foreach (var row in visible.Where(x => x.Current >= 0 && x.Required > x.Current && x.RowY > 0))
        {
            if (row.DisplayName.Replace(" ", "", StringComparison.Ordinal)
                .Contains("제작대", StringComparison.Ordinal)) continue;
            var sameRequired = deficits.Where(x => x.Required == row.Required).ToArray();
            var candidates = sameRequired.Where(x => x.Current == row.Current).ToArray();
            // Displayed counts can differ from bag+storage totals. Current OCR
            // is only a positioning aid; allow the whole same-requirement group
            // only when the supported-input check below accepts every candidate.
            if (candidates.Length == 0)
                candidates = sameRequired;
            if (candidates.Length == 0 || candidates.Any(x => !supported.Contains(x.DisplayName)))
                continue;
            bindings.Add(new(candidates[0] with { RowY = row.RowY, Current = row.Current }, candidates));
        }
        return bindings.FirstOrDefault(x => x.Targets.Any(t => t.DisplayName == requested))
            ?? bindings.FirstOrDefault();
    }
}
