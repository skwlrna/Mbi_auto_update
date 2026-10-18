using System.Text.Json;

namespace FishingAutomation.Telemetry;

/// <summary>
/// Explicit, one-shot bridge to existing MabiAuto read-only CLI query methods.
/// No executable path, raw payload, character ID, item label or process state
/// appears in results. No game action, capture or periodic worker is created.
/// </summary>
internal static class ReadOnlyCliObservationBridge
{
    internal static ReadOnlyCliObservationCollector Create(
        MabinogiMobileCli cli, CommonObservationEngine engine,
        bool explicitlyEnabled = false, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(cli);
        return new(engine,
            (query, token) => QueryExistingCliAsync(cli, query, token),
            explicitlyEnabled, now);
    }

    /// <summary>
    /// Exposes only the actual local SchedulerRunMonitor.IsRunning property.
    /// A finished/nonrunning scheduler cannot be inferred to have succeeded.
    /// </summary>
    internal static CommonObservationAdmission ObserveRunningScheduler(
        ReadOnlyCliObservationCollector collector, SchedulerRunMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(monitor);
        if (!collector.Enabled)
            return new(false, "disabled");
        return monitor.IsRunning ? collector.ObserveSchedulerRunning() :
            new(false, "scheduler_not_running");
    }

    /// <summary>
    /// Compare existing local production stage with a CLI work snapshot.
    /// This is an advisory only. VerifyInventory and other transitional
    /// stages are not interpreted as complete or running.
    /// </summary>
    internal static MultiAlteringObservationAudit.Result AuditLocalProductionStage(
        ReadOnlyCliObservationCollector collector,
        ProductionStage localStage, CliDiagnosticReadResult? workRead)
    {
        ArgumentNullException.ThrowIfNull(collector);
        CommonObservationState? state = localStage switch
        {
            ProductionStage.Process => CommonObservationState.InProgress,
            ProductionStage.Complete => CommonObservationState.Completed,
            _ => null
        };
        return collector.CompareMultiAltering(state, workRead);
    }

    private static async Task<CliDiagnosticProjection> QueryExistingCliAsync(
        MabinogiMobileCli cli, CliDiagnosticQuery query, CancellationToken token)
    {
        // This exact switch is the only path into a real query.
        // MabinogiMobileCli.ActionAsync is NEVER used.
        MabinogiCliResult result = query switch
        {
            CliDiagnosticQuery.Status => await cli.StatusAsync(token).ConfigureAwait(false),
            CliDiagnosticQuery.Activity => await cli.GetActivityAsync(token).ConfigureAwait(false),
            CliDiagnosticQuery.AlteringWorks => await cli.GetAlteringWorksAsync(token).ConfigureAwait(false),
            CliDiagnosticQuery.Items => await cli.GetItemsAsync(token).ConfigureAwait(false),
            CliDiagnosticQuery.AlteringCatalog => await cli.GetAlterableItemsAsync(token).ConfigureAwait(false),
            _ => throw new InvalidOperationException("query_not_allowed")
        };

        if (query == CliDiagnosticQuery.Status)
            return ReadOnlyCliObservationProjection.Connection(
                result.Command, result.Success, result.State, result.Error);
        if (!result.Success || result.Data is null)
            return new("cli_query_not_verified");

        try
        {
            switch (query)
            {
                case CliDiagnosticQuery.Activity:
                {
                    // Only the existing FULL schema parser may create a
                    // typed activity observation. No missing field is guessed.
                    GatheringActivity activity = GatheringQueries.ParseActivity(result);
                    return ReadOnlyCliObservationProjection.Activity(
                        activity.IsAutoTraveling, activity.IsInCombat,
                        activity.IsAutoPlaying, activity.IsGathering, activity.IsFishing);
                }
                case CliDiagnosticQuery.AlteringWorks:
                {
                    IReadOnlyList<AlteringWork> works = AlteringQueries.ParseWorks(result);
                    int running = works.Count(x => x.State == "InProgress");
                    int completed = works.Count(x => x.IsCompleted);
                    return ReadOnlyCliObservationProjection.AlteringWorks(
                        works.Count, running, completed,
                        works.Count - running - completed);
                }
                case CliDiagnosticQuery.Items:
                {
                    // Reuse the production's strict shape parser with no
                    // selected names; totals and identity are not stored.
                    bool valid = result.Data is JsonElement data &&
                        data.ValueKind == JsonValueKind.Array &&
                        data.GetArrayLength() <= 100000 &&
                        InventoryLootTracker.Parse(data,
                            new Dictionary<string, string>(StringComparer.Ordinal))
                        is not null;
                    return ReadOnlyCliObservationProjection.InventoryShape(
                        result.Success, valid,
                        valid ? result.Data.Value.GetArrayLength() : 0);
                }
                case CliDiagnosticQuery.AlteringCatalog:
                {
                    IReadOnlyList<AlteringRecipe> recipes =
                        AlteringQueries.ParseRecipes(result);
                    return recipes.Count > 1024 ?
                        new("catalog_over_limit") :
                        new("recipe_catalog_snapshot_only", Count: recipes.Count);
                }
                default:
                    return new("query_not_allowed");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or
            InvalidOperationException or KeyNotFoundException or FormatException or
            OverflowException or ArgumentOutOfRangeException)
        {
            return new("cli_schema_unverified");
        }
    }
}
