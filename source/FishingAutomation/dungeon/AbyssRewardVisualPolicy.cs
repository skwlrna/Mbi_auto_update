using System.Drawing;
namespace DungeonVisionBot;

// Reward layout shared by the dungeon and Abyss result flows.
// Only the chest + three separate actions establish a reward screen; no OCR or combat veto.
internal static class AbyssRewardVisualPolicy
{
    internal static bool TryDetect(Bitmap frame, out Rectangle retry, out string diagnostic) =>
        TryDetectLayout(frame, out retry, out diagnostic, searchNearbyGaps: true);

    internal static bool TryDetectDungeon(Bitmap frame, out Rectangle retry, out string diagnostic) =>
        TryDetectLayout(frame, out retry, out diagnostic, searchNearbyGaps: false);

    private static bool TryDetectLayout(Bitmap frame, out Rectangle retry, out string diagnostic,
        bool searchNearbyGaps)
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

        const double minimumGapContrast = 32;

        double GapContrast(int expectedX, int beforeX, int afterX)
        {
            double buttonGreen = Math.Min(MeanGreen(Scale(beforeX, 935, 16, 30)),
                MeanGreen(Scale(afterX, 935, 16, 30)));
            double lowestGreen = MeanGreen(Scale(expectedX, 935, 9, 30));
            if (searchNearbyGaps)
            {
                // The button edges can shift a few pixels relative to the reference layout.
                for (int offset = 1; offset <= 8 && buttonGreen - lowestGreen < minimumGapContrast; offset++)
                {
                    lowestGreen = Math.Min(lowestGreen, MeanGreen(Scale(expectedX - offset, 935, 9, 30)));
                    lowestGreen = Math.Min(lowestGreen, MeanGreen(Scale(expectedX + offset, 935, 9, 30)));
                }
            }
            return buttonGreen - lowestGreen;
        }

        double chest = Ratio(Scale(350, 105, 100, 100), true);
        double left = Ratio(Scale(180, 925, 140, 50));
        double middle = Ratio(Scale(330, 925, 140, 50));
        double right = Ratio(Scale(480, 925, 140, 50));
        double gap1 = GapContrast(320, 300, 335);
        double gap2 = GapContrast(471, 448, 486);
        bool gapsSeparated = gap1 >= minimumGapContrast && gap2 >= minimumGapContrast;
        double center = Ratio(Scale(376, 906, 48, 48));
        diagnostic = $"chest={chest:0.000}; buttons={left:0.000}/{middle:0.000}/{right:0.000}; gapGreenContrast={gap1:0.000}/{gap2:0.000}; center={center:0.000}";
        if (chest < 0.15 || left < 0.45 || middle < 0.45 || right < 0.45 ||
            !gapsSeparated || center < 0.25) return false;
        retry = Scale(310, 905, 180, 85);
        return true;
    }
}
