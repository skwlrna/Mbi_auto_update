using System.Text;
using System.Text.Json;

namespace FishingAutomation;

// Local-only, bounded read-only evidence. Never captures account/character
// identity, raw packets, screenshot data, secrets, or executable instructions.
// A diagnostic snapshot does not authorize a game action or change batch.json.
internal static class MultiAlteringIncidentSnapshot
{
    internal static async Task<string> CaptureAsync(
        IReadOnlyList<AlteringPlan> plans, string errorType,
        Func<CancellationToken, Task<IReadOnlyList<AlteringRecipe>>> readRecipes,
        Func<CancellationToken, Task<IReadOnlyList<AlteringWork>>> readWorks,
        string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(readRecipes);
        ArgumentNullException.ThrowIfNull(readWorks);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        IReadOnlyList<AlteringRecipe> recipes = [];
        IReadOnlyList<AlteringWork> works = [];
        string recipeRead = "ok", workRead = "ok";
        try { recipes = await readRecipes(ct); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            recipeRead = ex is OperationCanceledException ? "timeout" : ex.GetType().Name;
        }
        try { works = await readWorks(ct); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            workRead = ex is OperationCanceledException ? "timeout" : ex.GetType().Name;
        }

        var selected = plans.Take(30).Select(plan =>
        {
            var recipe = recipes.Where(r => r.DisplayName == plan.DisplayName)
                .ElementAtOrDefault(plan.RecipeOrdinal - 1);
            return new
            {
                facility = plan.FacilityName,
                recipe = plan.DisplayName,
                target = plan.TargetQuantity,
                producedPerWork = plan.ProducedPerWork,
                hasRecipeSnapshot = recipe is not null,
                alterable = recipe?.Alterable,
                reason = recipe?.Reason,
                missing = recipe?.MissingIngredients.Take(12).Select(m => new
                {
                    name = m.DisplayName, required = m.Required, owned = m.Owned
                }).ToArray()
            };
        }).ToArray();
        var lanes = plans.Select(x => x.FacilityName)
            .Distinct(StringComparer.Ordinal).Take(8).Select(f =>
            {
                var local = works.Where(w => w.FacilityName == f).ToArray();
                return new
                {
                    facility = f,
                    occupied = local.Length,
                    completed = local.Count(w => w.IsCompleted),
                    running = local.Count(w => w.State == "InProgress"),
                    jobs = local.Take(7).Select(w => new
                    {
                        recipe = w.DisplayName, state = w.State,
                        completed = w.IsCompleted,
                        remainingSeconds = w.RemainingSeconds
                    }).ToArray()
                };
            }).ToArray();

        var content = JsonSerializer.Serialize(new
        {
            schema = 1,
            capturedAtUtc = DateTimeOffset.UtcNow,
            mode = "multi-altering",
            errorType = errorType.Length > 70 ? errorType[..70] : errorType,
            recipeRead, workRead,
            selected, lanes
        }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            "incident-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        string temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content, Encoding.UTF8);
            File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        // Keep a small local rolling window instead of accumulating unlimited
        // telemetry. A cleanup failure must not erase the current evidence.
        try
        {
            foreach (var older in Directory.EnumerateFiles(directory, "incident-*.json")
                .OrderByDescending(x => x, StringComparer.Ordinal).Skip(20))
                File.Delete(older);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return path;
    }
}
