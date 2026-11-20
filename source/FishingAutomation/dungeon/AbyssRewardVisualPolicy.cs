using System.Drawing;
namespace DungeonVisionBot;

// Reward layout shared by the dungeon and Abyss result flows.
// Only the chest + three separate actions establish a reward screen; no OCR or combat veto.
internal static class AbyssRewardVisualPolicy
{
    internal static bool TryDetect(Bitmap frame, out Rectangle retry, out string diagnostic) =>
        TryDetectLayout(frame, out retry, out diagnostic, compareGapContrast: false);

    internal static bool TryDetectDungeon(Bitmap frame, out Rectangle retry, out string diagnostic) =>
        TryDetectLayout(frame, out retry, out diagnostic, compareGapContrast: true);

    private static bool TryDetectLayout(Bitmap frame, out Rectangle retry, out string diagnostic,
        bool compareGapContrast)
    {
        retry = Rectangle.Empty;
        diagnostic = "invalid reward frame";
        if (frame.Height <= 0 || frame.Width / (double)frame.Height is < 0.74 or > 0.92)
            return false;
        Rectangle Scale(int x, int y, int w, int h) => new(
            (int)Math.Round(x * frame.Width / 800.0),
            (int)Math.Round(y * frame.Height / 1000.0),
            Math.Max(1, (int)Math.Round(w * frame.Width / 800.0)),
            Math.Max(1, (int)Math.Round(h * frame.Height / 1000.0)));
        double Ratio(Rectangle area, bool gold = false)
        {
            int hit = 0, total = 0;
            for (int y = area.Top; y < Math.Min(frame.Height, area.Bottom); y += 2)
                for (int x = area.Left; x < Math.Min(frame.Width, area.Right); x += 2)
                {
                    var c = frame.GetPixel(x, y); total++;
                    if (gold ? c.R >= 130 && c.G >= 95 && c.R > c.B + 35 && c.G > c.B + 20
                             : c.G >= 70 && c.G >= c.R + 12 && c.G + 18 >= c.B) hit++;
                }
            return total == 0 ? 0 : hit / (double)total;
        }
        double MeanGreen(Rectangle area)
        {
            int sum = 0, count = 0;
            for (int y = area.Top; y < Math.Min(frame.Height, area.Bottom); y += 2)
                for (int x = area.Left; x < Math.Min(frame.Width, area.Right); x += 2)
                {
                    sum += frame.GetPixel(x, y).G;
                    count++;
                }
            return count == 0 ? 0 : sum / (double)count;
        }

        double GapContrast(Rectangle gap, Rectangle before, Rectangle after) =>
            Math.Min(MeanGreen(before), MeanGreen(after)) - MeanGreen(gap);

        double chest = Ratio(Scale(350, 105, 100, 100), true);
        double left = Ratio(Scale(180, 925, 140, 50));
        double middle = Ratio(Scale(330, 925, 140, 50));
        double right = Ratio(Scale(480, 925, 140, 50));
        var leftGap = Scale(320, 935, 9, 30);
        var rightGap = Scale(471, 935, 9, 30);
        double gap1 = compareGapContrast
            ? GapContrast(leftGap, Scale(300, 935, 16, 30), Scale(335, 935, 16, 30))
            : Ratio(leftGap);
        double gap2 = compareGapContrast
            ? GapContrast(rightGap, Scale(448, 935, 16, 30), Scale(486, 935, 16, 30))
            : Ratio(rightGap);
        const double minimumGapContrast = 32;
        bool gapsSeparated = compareGapContrast
            ? gap1 >= minimumGapContrast && gap2 >= minimumGapContrast
            : gap1 <= 0.15 && gap2 <= 0.15;
        string gapMetric = compareGapContrast ? "gapGreenContrast" : "gapGreenRatio";
        double center = Ratio(Scale(376, 906, 48, 48));
        diagnostic = $"chest={chest:0.000}; buttons={left:0.000}/{middle:0.000}/{right:0.000}; {gapMetric}={gap1:0.000}/{gap2:0.000}; center={center:0.000}";
        if (chest < 0.15 || left < 0.45 || middle < 0.45 || right < 0.45 ||
            !gapsSeparated || center < 0.25) return false;
        retry = Scale(310, 905, 180, 85);
        return true;
    }
}
