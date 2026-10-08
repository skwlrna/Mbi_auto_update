using System.Drawing;
using System.Text.RegularExpressions;

namespace FishingAutomation;

// F07/F08: managed processing should require two independent positive detail
// identity observations across an await boundary. Any missing/contradictory
// observation fails closed. This policy does not itself inject UI input.
internal static class AlteringRecipeIdentityPolicy
{
    // F07: the detail header may omit an ingredient qualifier (for example
    // both "철괴(광석)" and "철괴(철 광석)" render as "철괴").
    // A base-output OCR hit proves the selected recipe only when the CLI
    // catalog has exactly ONE matching output recipe. Never confuse two
    // different recipes merely because their finished item names coincide.
    internal static bool MayUseBaseOutputTitle(
        string displayName, string outputName, IEnumerable<string> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(outputName) ||
            string.Equals(displayName, outputName, StringComparison.Ordinal))
            return false;
        return catalog.Count(name =>
            string.Equals(Regex.Replace(name, @"\([^()]*\)$", "").Trim(),
                outputName, StringComparison.Ordinal)) == 1;
    }

    // N01: live button centers from two captured frames must agree before
    // clicking. The old center is used only as a displacement safety check;
    // the input itself always takes the center from the FINAL fresh frame.
    internal static bool IsStableFreshFreeActionTarget(
        Point previous, Point latest, Rectangle allowedArea)
        => allowedArea.Contains(previous) && allowedArea.Contains(latest) &&
           Math.Abs(previous.X - latest.X) <= 45 &&
           Math.Abs(previous.Y - latest.Y) <= 26;

    internal static bool IsNearMedicineFirstResult(
        int observedX, int observedY, int expectedX, int expectedY)
        => Math.Abs(observedX - expectedX) <= 180 &&
           Math.Abs(observedY - expectedY) <= 65;

    internal static async Task<bool> VerifyTwoFreshObservationsAsync(
        Func<CancellationToken, Task<bool>> observeExpectedDetail,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observeExpectedDetail);
        ArgumentNullException.ThrowIfNull(delay);
        ct.ThrowIfCancellationRequested();
        if (!await observeExpectedDetail(ct))
            return false;
        await delay(TimeSpan.FromMilliseconds(180), ct);
        ct.ThrowIfCancellationRequested();
        return await observeExpectedDetail(ct);
    }
}
