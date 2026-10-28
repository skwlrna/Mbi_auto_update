using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonVisionBot;

/// <summary>
/// Recover a quest material name only from a previous fully named shortage
/// snapshot for the SAME quest/batch, corroborated by current inventory.
/// This does not infer a recipe ingredient from its quantity alone.
/// </summary>
internal static class CraftingQuestMaterialRecoveryPolicy
{
    internal static CraftingQuestDeficit? IdentifyUnique(
        IReadOnlyList<CraftingQuestDeficit> previouslyNamed,
        IReadOnlyList<CraftingQuestDeficit> currentlyVisible,
        IReadOnlyDictionary<string, long> freshInventory)
    {
        if (previouslyNamed.Count == 0 || currentlyVisible.Count == 0 ||
            currentlyVisible.Count > previouslyNamed.Count ||
            previouslyNamed.Any(x => string.IsNullOrWhiteSpace(x.DisplayName)) ||
            previouslyNamed.Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count() != previouslyNamed.Count)
            return null;

        var unknown = currentlyVisible.Where(x => string.IsNullOrWhiteSpace(x.DisplayName)).ToArray();
        if (unknown.Length != 1)
            return null;

        CraftingQuestDeficit row = unknown[0];
        if (row.Current < 0 || row.Required <= 0 || row.Current >= row.Required)
            return null;

        // Reject a changed or partially new quest: every readable current row
        // must still have the same name/required count as the old snapshot.
        var known = currentlyVisible.Where(x => !string.IsNullOrWhiteSpace(x.DisplayName)).ToArray();
        if (known.Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count() != known.Length)
            return null;
        foreach (var item in known)
        {
            if (!previouslyNamed.Any(p => p.DisplayName == item.DisplayName &&
                                           p.Required == item.Required) ||
                !freshInventory.TryGetValue(item.DisplayName, out long stock) ||
                stock != item.Current)
                return null;
        }

        // 2026-10-28 pumpkin soup: potato 8/60 -> 66/60, onion still
        // 6/60 but its dim label vanished. Reuse ONLY a unique prior 6/60
        // identity with the exact same fresh inventory quantity.
        var candidates = previouslyNamed.Where(p =>
            !known.Any(k => k.DisplayName == p.DisplayName) &&
            p.Current == row.Current &&
            p.Required == row.Required &&
            freshInventory.TryGetValue(p.DisplayName, out long stock) &&
            stock == row.Current).ToArray();
        if (candidates.Length != 1)
            return null;

        // Other earlier shortages must now be either still visibly named
        // or independently completed; otherwise this may be a different row.
        foreach (var old in previouslyNamed)
        {
            if (old.DisplayName == candidates[0].DisplayName ||
                known.Any(k => k.DisplayName == old.DisplayName))
                continue;
            if (!freshInventory.TryGetValue(old.DisplayName, out long stock) ||
                stock < old.Required)
                return null;
        }

        return row with { DisplayName = candidates[0].DisplayName };
    }
}
