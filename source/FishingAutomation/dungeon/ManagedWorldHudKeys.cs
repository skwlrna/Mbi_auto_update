using System.Drawing;

namespace DungeonVisionBot;

// A key-label observation is visual evidence only. It never authorizes input
// without a second actual capture, the independent field HUD and fresh CLI.
internal sealed record ManagedWorldHudKeys(
    string Method, Rectangle Home, Rectangle End, Rectangle Escape, string Scores)
{
    // Pair evidence is used only with both lower K/I labels and two fresh frames.
    // It does not change Found/IsConsecutiveWith, which still require all three keys.
    internal static bool HasHomeEndLayout(Rectangle home, Rectangle end)
    {
        if (home.IsEmpty || end.IsEmpty) return false;
        var h = new Point(home.Left + home.Width / 2, home.Top + home.Height / 2);
        var e = new Point(end.Left + end.Width / 2, end.Top + end.Height / 2);
        return h.X is >= 590 and <= 670 && e.X is >= 690 and <= 725 &&
               h.Y is >= 75 and <= 95 && e.Y is >= 75 and <= 95 &&
               Math.Abs(h.Y - e.Y) <= 5 &&
               (e.X - h.X is >= 38 and <= 64 || e.X - h.X is >= 88 and <= 116);
    }

    internal bool Found => HasKnownLayout(Home, End, Escape);

    internal static bool HasKnownLayout(Rectangle home, Rectangle end, Rectangle escape)
    {
        if (home.IsEmpty || end.IsEmpty || escape.IsEmpty) return false;
        static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
        Point h = Center(home), e = Center(end), x = Center(escape);
        // Observed 800x1000 layouts: Home/End/ESC, or Home/Y/End/ESC.
        // Anchor on End+ESC; do not require Home to stay at its old X.
        return h.X is >= 590 and <= 670 && e.X is >= 690 and <= 725 &&
               x.X is >= 755 and <= 790 &&
               h.Y is >= 75 and <= 95 && e.Y is >= 75 and <= 95 && x.Y is >= 75 and <= 95 &&
               Math.Abs(h.Y - e.Y) <= 5 && Math.Abs(e.Y - x.Y) <= 5 &&
               x.X - e.X is >= 50 and <= 85 &&
               (e.X - h.X is >= 38 and <= 64 || e.X - h.X is >= 88 and <= 116);
    }

    internal bool IsConsecutiveWith(ManagedWorldHudKeys other)
        => Found && other.Found && Method == other.Method &&
           Stable(Home, other.Home) && Stable(End, other.End) && Stable(Escape, other.Escape);

    private static bool Stable(Rectangle a, Rectangle b)
        => Math.Abs(a.Left - b.Left) <= 4 && Math.Abs(a.Top - b.Top) <= 3 &&
           Math.Abs(a.Width - b.Width) <= 4 && Math.Abs(a.Height - b.Height) <= 3;
}
