using System.Drawing;

namespace DungeonVisionBot;

// Conservative loot decision rules. A tracked item is counted only when:
// 1) the same icon is strong and stable in three frames,
// 2) it wins clearly over competing tracked templates, and
// 3) its exact item name is adjacent to that icon in at least two frames.
// Ambiguous evidence must resolve to +0 rather than a guessed item.
internal static class StrictLootPolicy
{
    internal const double MinimumIconScore = 0.84;
    internal const double MinimumWinnerMargin = 0.04;

    internal static bool IsStable(
        DetectionResult first,
        DetectionResult second,
        DetectionResult third,
        Size firstSize,
        Size secondSize,
        Size thirdSize)
    {
        if (firstSize != secondSize || firstSize != thirdSize)
            return false;

        foreach (var hit in new[] { first, second, third })
        {
            if (!hit.Found || !double.IsFinite(hit.Score) || hit.Score < MinimumIconScore ||
                hit.Bounds.Width <= 0 || hit.Bounds.Height <= 0)
                return false;
        }

        return SamePlace(first.Bounds, second.Bounds, firstSize) &&
               SamePlace(second.Bounds, third.Bounds, firstSize) &&
               SamePlace(first.Bounds, third.Bounds, firstSize);
    }

    internal static bool IsUnambiguousWinner(
        string winnerKey,
        IReadOnlyDictionary<string, DetectionResult> first,
        IReadOnlyDictionary<string, DetectionResult> second,
        IReadOnlyDictionary<string, DetectionResult> third)
        => HasMargin(winnerKey, first) &&
           HasMargin(winnerKey, second) &&
           HasMargin(winnerKey, third);

    internal static bool HasExactAdjacentName(
        string expectedDisplayName,
        DetectionResult icon,
        IReadOnlyList<DetectionResult> lines,
        Size frameSize)
        => lines.Any(line =>
            line.Found &&
            ExactNameMatches(expectedDisplayName, line.ReadText) &&
            NameBelongsToIcon(icon, line, frameSize));

    internal static bool ExactNameMatches(string expectedDisplayName, string? rawText)
    {
        string expected = Normalize(expectedDisplayName);
        string actual = Normalize(rawText ?? "");
        if (expected.Length == 0 || actual.Length == 0)
            return false;

        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // Result rows may render an explicit quantity. The tracked special drop is
        // one per round, so only an exact ×1 / x1 / 1 / 1개 suffix is accepted.
        foreach (string suffix in new[] { "1", "x1", "1개", "x1개" })
        {
            if (actual.Equals(expected + Normalize(suffix), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool NameBelongsToIcon(
        DetectionResult icon,
        DetectionResult line,
        Size frameSize)
    {
        if (!line.Found || icon.Bounds.Width <= 0 || icon.Bounds.Height <= 0)
            return false;

        double sx = frameSize.Width / 800.0;
        double sy = frameSize.Height / 1000.0;
        var nameArea = Rectangle.FromLTRB(
            icon.Bounds.Left - (int)Math.Round(12 * sx),
            icon.Bounds.Top - (int)Math.Round(14 * sy),
            icon.Bounds.Right + (int)Math.Round(380 * sx),
            icon.Bounds.Bottom + (int)Math.Round(70 * sy));

        return nameArea.Contains(line.Bounds);
    }

    private static bool HasMargin(
        string winnerKey,
        IReadOnlyDictionary<string, DetectionResult> frame)
    {
        if (!frame.TryGetValue(winnerKey, out var winner) ||
            !winner.Found || !double.IsFinite(winner.Score) ||
            winner.Score < MinimumIconScore)
            return false;

        double runnerUp = double.NegativeInfinity;
        foreach (var kv in frame)
        {
            if (string.Equals(kv.Key, winnerKey, StringComparison.Ordinal))
                continue;

            double score = kv.Value.Score;
            if (double.IsFinite(score))
                runnerUp = Math.Max(runnerUp, score);
        }

        return double.IsNegativeInfinity(runnerUp) ||
               winner.Score - runnerUp >= MinimumWinnerMargin;
    }

    private static bool SamePlace(Rectangle a, Rectangle b, Size frameSize)
    {
        Rectangle overlap = Rectangle.Intersect(a, b);
        if (overlap.Width <= 0 || overlap.Height <= 0)
            return false;

        double minArea = Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height);
        double overlapArea = (double)overlap.Width * overlap.Height;
        if (minArea <= 0 || overlapArea / minArea < 0.50)
            return false;

        double sx = frameSize.Width / 800.0;
        double sy = frameSize.Height / 1000.0;
        int maxDx = Math.Max(6, (int)Math.Round(18 * sx));
        int maxDy = Math.Max(6, (int)Math.Round(18 * sy));

        return Math.Abs(a.Left + a.Width / 2 - (b.Left + b.Width / 2)) <= maxDx &&
               Math.Abs(a.Top + a.Height / 2 - (b.Top + b.Height / 2)) <= maxDy;
    }

    private static string Normalize(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
