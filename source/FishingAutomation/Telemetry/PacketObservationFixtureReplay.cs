using System.Globalization;
using System.Text.Json;

namespace FishingAutomation.Telemetry;

/// <summary>
/// Synthetic/offline fixture importer only. This is NOT a Mobilay JSON export
/// decoder, packet decoder, live adapter, or source-authentication mechanism.
/// No socket, capture, file watcher, or game-input capability.
/// </summary>
internal static class PacketObservationFixtureReplay
{
    internal const string FixtureAdapterId = "fixture/offline-v1";
    private const int MaximumRecords = 128;
    private const int MaximumLineLength = 2048;
    private const int MaximumTotalCharacters = 64 * 1024;

    internal sealed record ReplayResult(
        string Status,
        int Records,
        int Accepted,
        int Rejected);

    /// <summary>
    /// Replays an explicitly opted-in, synthetically authored JSONL fixture.
    /// The entire fixture is validated before any hint is admitted. Untrusted
    /// JSON cannot select its own adapter ID or grant gameplay authority.
    /// </summary>
    internal static ReplayResult Replay(
        TextReader reader,
        PacketObservationInbox inbox,
        DateTimeOffset nowUtc,
        bool explicitlyEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(inbox);
        if (!explicitlyEnabled)
            return new("disabled", 0, 0, 0);

        var parsed = new List<PacketObservation>();
        int total = 0;
        try
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                total += line.Length;
                if (line.Length == 0 || line.Length > MaximumLineLength ||
                    total > MaximumTotalCharacters || parsed.Count >= MaximumRecords)
                    return new("invalid_or_oversized_fixture", 0, 0, 0);
                if (!TryParse(line, out var observation))
                    return new("invalid_fixture_schema", 0, 0, 0);
                parsed.Add(observation!);
            }
        }
        catch (Exception e) when (
            e is IOException or System.Text.DecoderFallbackException)
        {
            return new("fixture_unreadable", 0, 0, 0);
        }

        if (parsed.Count == 0)
            return new("empty_fixture", 0, 0, 0);
        int accepted = 0, rejected = 0;
        foreach (var observation in parsed)
        {
            if (inbox.TryObserve(observation, nowUtc).Accepted)
                accepted++;
            else
                rejected++;
        }
        return new("replayed", parsed.Count, accepted, rejected);
    }

    private static bool TryParse(string line, out PacketObservation? observation)
    {
        observation = null;
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name) ||
                    property.Name is not ("schemaVersion" or "kind" or "sequence" or
                    "observedAtUtc" or "expiresAtUtc" or "value"))
                    return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
                return false;
            if (!root.TryGetProperty("kind", out var kindValue) ||
                kindValue.ValueKind != JsonValueKind.String)
                return false;
            PacketObservationKind kind = kindValue.GetString() switch
            {
                "combat_activity" => PacketObservationKind.CombatActivityHint,
                "target_health_fraction" => PacketObservationKind.TargetHealthFractionHint,
                "encounter_ended" => PacketObservationKind.EncounterEndedHint,
                _ => 0
            };
            if (kind == 0)
                return false;

            if (!root.TryGetProperty("sequence", out var sequenceValue) ||
                sequenceValue.ValueKind != JsonValueKind.Number ||
                !sequenceValue.TryGetInt64(out long sequence) || sequence <= 0)
                return false;

            if (!TryReadUtc(root, "observedAtUtc", out var observed) ||
                !TryReadUtc(root, "expiresAtUtc", out var expires))
                return false;

            double? value = null;
            bool hasValue = root.TryGetProperty("value", out var valueValue);
            if (kind == PacketObservationKind.TargetHealthFractionHint)
            {
                if (!hasValue || valueValue.ValueKind != JsonValueKind.Number ||
                    !valueValue.TryGetDouble(out var fraction) ||
                    !double.IsFinite(fraction) || fraction < 0 || fraction > 1)
                    return false;
                value = fraction;
            }
            else if (hasValue)
            {
                // Even explicit null is rejected for a no-value hint.
                return false;
            }

            observation = new PacketObservation(
                kind, FixtureAdapterId, sequence, observed, expires, value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadUtc(JsonElement root, string key, out DateTimeOffset value)
    {
        value = default;
        if (!root.TryGetProperty(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString();
        // UTC-only replay timestamps; neither local time nor an implicit offset
        // may silently change observation freshness.
        return text is not null &&
            text.EndsWith("Z", StringComparison.Ordinal) &&
            DateTimeOffset.TryParseExact(
                text, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out value) &&
            value.Offset == TimeSpan.Zero;
    }
}
