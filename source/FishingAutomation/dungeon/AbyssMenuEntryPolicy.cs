using System.Drawing;

namespace DungeonVisionBot;

/// <summary>
/// 800x1000 menu evidence from the real V3.1.87 2026-10-12 captured screen.
/// Never treat the field ESC menu button as the only way to start Abyss:
/// the right-hand menu may already be open after processing closes its UI.
/// </summary>
internal static class AbyssMenuEntryPolicy
{
    // Menu panel is x=391..799. The Abyss cell and its Korean label occupy
    // x=~495..560, y=~318..378. The unrelated Guild label is at y=~579.
    internal static readonly Rectangle AbyssCell = new(475, 298, 114, 107);
    internal static readonly Rectangle GuildLabel = new(478, 537, 100, 84);

    internal static bool IconEvidence(Rectangle bounds)
        => bounds.Width > 0 && bounds.Height > 0 && AbyssCell.Contains(bounds);

    internal static bool ContextEvidence(Rectangle bounds)
        => bounds.Width > 0 && bounds.Height > 0 && GuildLabel.Contains(bounds);

    // Require consecutive fresh-frame evidence in the same *right-hand menu*
    // cell. A text hit outside the visible menu can never skip or click.
    internal static bool Stable(DetectionResult previous, DetectionResult current)
    {
        if (!previous.Found || !current.Found ||
            !IconEvidence(previous.Bounds) || !IconEvidence(current.Bounds))
            return false;

        return Math.Abs(previous.Center.X - current.Center.X) <= 28 &&
               Math.Abs(previous.Center.Y - current.Center.Y) <= 28;
    }

    // The scheduler's non-repeat execution must continue an actual recovered
    // entry step, yet may finish only after separately validated outside HUD.
    internal static bool ShouldRunAnotherCycle(
        bool repeat, bool scheduledResultExit, bool outsideHudVerified,
        int scheduledRecoveryCycles, int limit)
        => repeat || (scheduledResultExit && !outsideHudVerified &&
                      scheduledRecoveryCycles < limit);
}
