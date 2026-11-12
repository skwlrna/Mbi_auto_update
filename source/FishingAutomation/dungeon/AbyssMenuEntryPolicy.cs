using System.Drawing;

namespace DungeonVisionBot;

/// <summary>800x1000 legacy and 2026-11-12 event-banner right menu layouts.</summary>
internal static class AbyssMenuEntryPolicy
{
    // 2026-10-12 standard menu layout.
    internal static readonly Rectangle AbyssCell = new(475, 298, 114, 107);
    internal static readonly Rectangle GuildLabel = new(478, 537, 100, 84);
    // 2026-11-12 event menu: Abyss ~y450; Guild ~y720.
    internal static readonly Rectangle AbyssCellEvent = new(478, 425, 122, 113);
    internal static readonly Rectangle GuildLabelEvent = new(558, 680, 100, 110);

    internal static bool IconEvidence(Rectangle b)
        => b.Width > 0 && b.Height > 0 &&
           (AbyssCell.Contains(b) || AbyssCellEvent.Contains(b));

    internal static Rectangle GuildLabelFor(Rectangle abyss)
        => AbyssCellEvent.Contains(abyss) ? GuildLabelEvent : GuildLabel;

    internal static bool ContextEvidence(Rectangle abyss, Rectangle guild)
        => IconEvidence(abyss) && guild.Width > 0 && guild.Height > 0 &&
           GuildLabelFor(abyss).Contains(guild);

    // Two fresh frames must agree on the *same* menu layout, never a mixed pair.
    internal static bool Stable(DetectionResult previous, DetectionResult current)
    {
        if (!previous.Found || !current.Found) return false;
        bool sameLegacy = AbyssCell.Contains(previous.Bounds) && AbyssCell.Contains(current.Bounds);
        bool sameEvent = AbyssCellEvent.Contains(previous.Bounds) && AbyssCellEvent.Contains(current.Bounds);
        return (sameLegacy || sameEvent) &&
               Math.Abs(previous.Center.X - current.Center.X) <= 28 &&
               Math.Abs(previous.Center.Y - current.Center.Y) <= 28;
    }

    internal static bool ShouldRunAnotherCycle(
        bool repeat, bool scheduledResultExit, bool outsideHudVerified,
        int scheduledRecoveryCycles, int limit)
        => repeat || (scheduledResultExit && !outsideHudVerified &&
                      scheduledRecoveryCycles < limit);
}
