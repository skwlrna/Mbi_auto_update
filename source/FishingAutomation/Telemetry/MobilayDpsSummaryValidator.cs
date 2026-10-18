using System.Text.Json;

namespace FishingAutomation.Telemetry;

/// <summary>
/// Private, offline-only check of the observed v4 DPS export shape. It does
/// not authenticate a third-party export, decode binary packets, or claim
/// a live game event. The raw JSON and player/monster identifiers never
/// appear in the returned result.
/// </summary>
internal static class MobilayDpsSummaryValidator
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    internal const int MaximumSeconds = 24 * 60 * 60;

    internal sealed record Result(
        string Status,
        int? FormatVersion = null,
        int EventCount = 0,
        int RetainedCount = 0,
        int DroppedCount = 0,
        int SecondBuckets = 0,
        bool IsRealtime = false,
        bool SemanticsVerified = false,
        bool GameplayAuthorized = false);

    internal static Result Inspect(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0)
            return new("empty");
        if (utf8.Length > MaximumBytes)
            return new("too_large");

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(),
                new JsonDocumentOptions { MaxDepth = 24,
                    AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                HasDuplicateKeys(root))
                return new("invalid_summary");

            if (!GetInt(root, "Version", out int version))
                return new("invalid_summary");
            if (version != 4)
                return new("unsupported_version", FormatVersion: version);

            if (!root.TryGetProperty("SavedAt", out var savedAt) ||
                savedAt.ValueKind != JsonValueKind.String ||
                !savedAt.TryGetDateTimeOffset(out _) ||
                !GetInt(root, "EventCount", out int events) ||
                !GetInt(root, "RetainedEventCount", out int retained) ||
                !GetInt(root, "DroppedEventCount", out int dropped) ||
                events < 0 || retained < 0 || dropped < 0 ||
                retained > events || dropped > events ||
                !root.TryGetProperty("BattleSeconds", out var battle) ||
                battle.ValueKind != JsonValueKind.Number ||
                !battle.TryGetDouble(out double seconds) ||
                !double.IsFinite(seconds) || seconds < 0 ||
                !root.TryGetProperty("TotalDamage", out var damage) ||
                damage.ValueKind != JsonValueKind.Number ||
                !damage.TryGetInt64(out long totalDamage) || totalDamage < 0 ||
                !root.TryGetProperty("CompactData", out var compact) ||
                compact.ValueKind != JsonValueKind.Object ||
                HasDuplicateKeys(compact) ||
                !compact.TryGetProperty("s", out var buckets) ||
                buckets.ValueKind != JsonValueKind.Array ||
                buckets.GetArrayLength() > MaximumSeconds)
                return new("invalid_summary");

            int previous = -1;
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object ||
                    !GetInt(bucket, "s", out int index) ||
                    index <= previous || index < 0 || index >= MaximumSeconds)
                    return new("invalid_summary");
                previous = index;
            }
            // Counts are statistics only. A valid EventCount is NOT a
            // replayable array of raw events. Unknown nested JSON isn't
            // interpreted, logged, or promoted to the common live engine.
            return new("offline_summary_shape_only", version,
                events, retained, dropped, buckets.GetArrayLength());
        }
        catch (JsonException)
        {
            return new("invalid_json");
        }
    }

    private static bool GetInt(JsonElement parent, string key, out int value)
    {
        value = 0;
        return parent.TryGetProperty(key, out var item) &&
            item.ValueKind == JsonValueKind.Number &&
            item.TryGetInt32(out value);
    }

    private static bool HasDuplicateKeys(JsonElement element)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name))
                return true;
        return false;
    }
}
