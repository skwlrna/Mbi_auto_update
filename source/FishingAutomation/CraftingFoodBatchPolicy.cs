using DungeonVisionBot;

namespace FishingAutomation;

internal sealed class CraftingQuestRowRecognitionException(string message) : InvalidOperationException(message);

internal sealed record CraftingFoodQuestRow(
    CraftingQuestDeficit Row, IReadOnlyList<CraftingQuestDeficit> Targets);

internal static class CraftingFoodBatchPolicy
{
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
            // Current-count OCR is only a positioning aid; a unique requirement
            // still identifies a recipe input when its current digits are unreadable.
            if (candidates.Length == 0 && sameRequired.Length == 1)
                candidates = sameRequired;
            if (candidates.Length == 0 || candidates.Any(x => !supported.Contains(x.DisplayName)))
                continue;
            bindings.Add(new(candidates[0] with { RowY = row.RowY, Current = row.Current }, candidates));
        }
        return bindings.FirstOrDefault(x => x.Targets.Any(t => t.DisplayName == requested))
            ?? bindings.FirstOrDefault();
    }
}
