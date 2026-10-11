namespace DungeonVisionBot;

// Screen coordinates are transient and tied to the CURRENT 800x1000 frame.
// No client files, memory, or packet capture are touched by this policy.
internal sealed class WaterwayMarkPolicy
{
    internal sealed record PriestSpot(string Symbol, int X, int Y, long SeenAt, double Confidence);
    internal sealed record TargetSpot(string Symbol, int MarkX, int MarkY, double Confidence);

    private readonly Dictionary<string, PriestSpot> _priests = new(StringComparer.Ordinal);
    private string? _bossSymbol;
    private long _bossSeenAt;

    internal const int BossMaxAgeMs = 4_000;
    internal const int PriestMaxAgeMs = 1_200;

    internal void Reset()
    {
        _priests.Clear();
        _bossSymbol = null;
        _bossSeenAt = 0;
    }

    internal void ObserveBoss(string symbol, long now)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        _bossSymbol = symbol;
        _bossSeenAt = now;
    }

    internal void ObservePriest(string symbol, int x, int y, double confidence, long now)
    {
        if (string.IsNullOrWhiteSpace(symbol) ||
            x is < 0 or >= 800 || y is < 0 or >= 1000 ||
            !double.IsFinite(confidence) || confidence is < 0 or > 1)
            return;

        _priests[symbol] = new PriestSpot(symbol, x, y, now, confidence);
    }

    internal string? RecentBoss(long now) =>
        _bossSymbol is not null && now >= _bossSeenAt &&
        now - _bossSeenAt <= BossMaxAgeMs ? _bossSymbol : null;

    internal int RecentPriestCount(long now) =>
        _priests.Values.Count(p => now >= p.SeenAt &&
            now - p.SeenAt <= PriestMaxAgeMs);

    // The character's actual head glyph is the target notification.
    // Compare BOSS SYMBOL -> PLAYER TARGET GLYPH -> MATCHING PRIEST SYMBOL.
    // A result is a candidate priest MARKER location, not an auto-move route
    // or the priest's feet position.
    internal TargetSpot? Choose(string? playerTargetSymbol, long now)
    {
        var boss = RecentBoss(now);
        if (boss is null || playerTargetSymbol != boss ||
            !_priests.TryGetValue(boss, out var priest) ||
            now < priest.SeenAt || now - priest.SeenAt > PriestMaxAgeMs)
            return null;

        return new TargetSpot(boss, priest.X, priest.Y, priest.Confidence);
    }
}
