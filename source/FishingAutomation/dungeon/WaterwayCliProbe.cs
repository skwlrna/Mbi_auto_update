using System.Text.Json;
using FishingAutomation;

namespace DungeonVisionBot;

// Inspection only. A query's presence, fields, or values do not establish
// game packet semantics or authorize any live dungeon input.
internal sealed record WaterwayCliInspection(
    string Command, string State, bool Readable, IReadOnlyList<string> JsonPaths,
    string Note);

internal sealed record WaterwayCliProbeReport(
    bool Connected, IReadOnlyList<string> AdvertisedCommands,
    IReadOnlyList<WaterwayCliInspection> Inspections, string SafetyNote);

internal static class WaterwayCliProbe
{
    private static readonly string[] OptionalQueries =
    {
        "get_current_environment", "get_activity", "get_my_info"
    };

    public static async Task<WaterwayCliProbeReport> InspectAsync(
        MabinogiMobileCli cli, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cli);
        // The existing MabinogiMobileCli process boundary only permits
        // allowlisted, read-only commands. Never call QueryAsync with a string
        // discovered in capabilities.
        var observations = new List<WaterwayCliInspection>();
        var status = await cli.StatusAsync(cancellationToken).ConfigureAwait(false);
        observations.Add(Inspect("status", status));
        if (!status.Success)
            return new(false, Array.Empty<string>(), observations,
                "Disconnected: no additional CLI commands were executed.");

        var capabilities = await cli.CapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        observations.Add(Inspect("capabilities", capabilities));
        if (!capabilities.Success || !capabilities.Data.HasValue)
            return new(true, Array.Empty<string>(), observations,
                "Capabilities unavailable: optional queries were not attempted.");

        var advertised = ReadAdvertisedCommands(capabilities.Data.Value);
        foreach (string command in OptionalQueries)
        {
            if (!advertised.Contains(command))
            {
                observations.Add(new(command, "not_advertised", false,
                    Array.Empty<string>(), "Not claimed to be supported."));
                continue;
            }

            MabinogiCliResult result = command switch
            {
                "get_current_environment" => await cli.GetCurrentEnvironmentAsync(cancellationToken).ConfigureAwait(false),
                "get_activity" => await cli.GetActivityAsync(cancellationToken).ConfigureAwait(false),
                "get_my_info" => await cli.GetMyInfoAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unknown local read-only query")
            };
            observations.Add(Inspect(command, result));
        }

        return new(true, advertised.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            observations,
            "Schema-only inventory. No values/identifiers are printed, no packet schema inferred, no gameplay action enabled.");
    }

    private static WaterwayCliInspection Inspect(string command, MabinogiCliResult result)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (result.Success && result.Data.HasValue)
            Walk(result.Data.Value, "$", paths, 0);

        return new(command, result.State, result.Success, paths.ToArray(),
            result.Success ? "Read-only CLI response structure observed; domain meaning unverified."
                           : result.Error ?? "No verified output.");
    }

    private static HashSet<string> ReadAdvertisedCommands(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        // Observes only an explicit 'commands' member, not guessed capabilities.
        if (root.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals("commands", StringComparison.OrdinalIgnoreCase) ||
                property.Value.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    if (item.GetString() is { } name) result.Add(name);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in item.EnumerateObject())
                        if (field.Name.Equals("command", StringComparison.OrdinalIgnoreCase) &&
                            field.Value.ValueKind == JsonValueKind.String &&
                            field.Value.GetString() is { } name)
                            result.Add(name);
                }
            }
        }
        return result;
    }

    // Intentionally bounded and schema-only; never log private CLI values.
    private static void Walk(JsonElement value, string prefix, SortedSet<string> paths, int depth)
    {
        if (depth >= 4 || paths.Count >= 80) return;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in value.EnumerateObject())
            {
                if (paths.Count >= 80) break;
                string next = prefix + "." + item.Name;
                paths.Add(next + ":" + TypeName(item.Value));
                Walk(item.Value, next, paths, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (paths.Count >= 80) return;
            // First element is used only to illustrate the structural shape.
            if (value.GetArrayLength() > 0)
                Walk(value[0], prefix + "[*]", paths, depth + 1);
        }
    }

    private static string TypeName(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "unknown"
    };
}
