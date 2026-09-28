using System.Drawing;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

// Pure decision logic shared by production and regression tests.
internal static class HallucinationLootPolicy
{
    internal const double MinimumIconScore = 0.85;
    internal static bool IsExactName(string? text) => Regex.IsMatch(
        text ?? "", @"^\s*허상의\s*마력석\s*(?:[xX×]?\s*[1-9]\d*\s*(?:개)?)?\s*$",
        RegexOptions.CultureInvariant);

    internal static bool NameBelongsToIcon(DetectionResult icon, DetectionResult line, Size frameSize)
    {
        if (!line.Found || !IsExactName(line.ReadText)) return false;
        double sx = frameSize.Width / 800.0, sy = frameSize.Height / 1000.0;
        var nameArea = Rectangle.FromLTRB(
            icon.Bounds.Left - (int)(12 * sx), icon.Bounds.Top - (int)(12 * sy),
            icon.Bounds.Right + (int)(360 * sx), icon.Bounds.Bottom + (int)(60 * sy));
        return nameArea.Contains(line.Bounds);
    }

    internal static bool Confirm(DetectionResult first, DetectionResult second,
        IReadOnlyList<DetectionResult> firstLines, IReadOnlyList<DetectionResult> secondLines,
        Size firstSize, Size secondSize)
    {
        if (firstSize != secondSize || !first.Found || !second.Found ||
            !double.IsFinite(first.Score) || !double.IsFinite(second.Score) ||
            first.Score < MinimumIconScore || second.Score < MinimumIconScore ||
            first.Bounds.Width <= 0 || first.Bounds.Height <= 0 ||
            second.Bounds.Width <= 0 || second.Bounds.Height <= 0 ||
            !first.Bounds.IntersectsWith(second.Bounds)) return false;
        // The exact label must be associated with the same stable icon in both frames.
        return firstLines.Any(a => NameBelongsToIcon(first, a, firstSize) &&
            secondLines.Any(b => NameBelongsToIcon(second, b, secondSize) &&
                a.Bounds.IntersectsWith(b.Bounds)));
    }
}
