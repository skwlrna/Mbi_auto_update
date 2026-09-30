using System.Text.Json;
using System.Text.RegularExpressions;

namespace FishingAutomation;

internal sealed record InventoryLootResult(bool Confirmed, IReadOnlyDictionary<string, int> Gains, string Reason);

/// <summary>One baseline and one result per dungeon round. Never falls back to OCR.</summary>
internal sealed class InventoryLootTracker
{
    private readonly Func<CancellationToken, Task<MabinogiCliResult>> _query;
    private readonly IReadOnlyDictionary<string, string> _names;
    private Dictionary<string, long>? _baseline;
    private Dictionary<string, long>? _prepared;
    private bool _completed;

    internal InventoryLootTracker(Func<CancellationToken, Task<MabinogiCliResult>> query,
        IReadOnlyDictionary<string, string> names)
    { _query = query; _names = names; }

    internal void Invalidate()
    { _baseline = null; _prepared = null; _completed = false; }

    internal async Task<bool> BeginRoundAsync(CancellationToken ct)
    {
        Invalidate();
        _baseline = await ReadAsync(ct);
        return _baseline is not null;
    }

    internal async Task<bool> PrepareNextRoundAsync(CancellationToken ct)
    {
        _prepared = await ReadAsync(ct);
        return _prepared is not null;
    }

    internal void CommitNextRound()
    { _baseline = _prepared; _prepared = null; _completed = false; }

    internal async Task<InventoryLootResult> CompleteRoundAsync(CancellationToken ct)
    {
        if (_completed) return Unknown("already_recorded");
        // Latch before reading: reconnect/repeated result screens cannot count again.
        _completed = true;
        if (_baseline is null) return Unknown("missing_baseline");
        var first = await ReadAsync(ct);
        if (first is null) return Unknown("query_failed");
        await Task.Delay(350, ct);
        var second = await ReadAsync(ct);
        if (second is null) return Unknown("query_failed");
        // Do not label unstable/delayed rewards as zero. Recheck once, then report unknown.
        if (!Same(first, second))
        {
            await Task.Delay(350, ct);
            var third = await ReadAsync(ct);
            if (third is null || !Same(second, third)) return Unknown("inventory_unstable");
            second = third;
        }
        var gains = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in _names.Keys)
        {
            long delta = second[key] - _baseline[key];
            if (delta > int.MaxValue) return Unknown("quantity_overflow");
            if (delta > 0) gains[key] = (int)delta;
        }
        return new(true, gains, "confirmed");
    }

    private static bool Same(Dictionary<string, long> a, Dictionary<string, long> b)
        => a.All(x => b.TryGetValue(x.Key, out long value) && value == x.Value);

    private static InventoryLootResult Unknown(string reason)
        => new(false, new Dictionary<string, int>(), reason);

    private async Task<Dictionary<string, long>?> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await _query(timeout.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return response.Success && response.Data is JsonElement data ? Parse(data, _names) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    internal static Dictionary<string, long>? Parse(JsonElement data, IReadOnlyDictionary<string, string> names)
    {
        if (data.ValueKind != JsonValueKind.Array) return null;
        var totals = names.Keys.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
        var keysByName = names.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
        try
        {
            foreach (var item in data.EnumerateArray())
            {
                // A partial/malformed inventory is never a valid zero baseline.
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("DisplayName", out var name) || name.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("Location", out var location) || location.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("Count", out var count) || !count.TryGetInt64(out long quantity) || quantity < 0)
                    return null;
                if (location.GetString() is not ("inventory" or "account_storage" or "character_storage")) return null;
                // CLI decorates the star with a color tag. Strip that markup only; keep + and star exact.
                string displayName = Regex.Replace(name.GetString()!, @"</?color(?:=[^<>]*)?>", "", RegexOptions.CultureInvariant).Trim();
                if (keysByName.TryGetValue(displayName, out string? key))
                    totals[key] = checked(totals[key] + quantity);
            }
        }
        catch (Exception ex) when (ex is OverflowException or InvalidOperationException) { return null; }
        return totals;
    }
}
