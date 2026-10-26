using System.Drawing;

namespace FishingAutomation;

internal static class AlteringFacilityLayout
{
    // User-confirmed 800x1000 remote facility screen. The "설비로 이동" button
    // is always the same left-side teal pill. These coordinates are intentionally
    // independent of OCR so missing text recognition cannot be mistaken for on-site.
    internal static readonly Rectangle MoveButtonVisualArea = new(15, 205, 155, 65);
    internal static readonly Point MoveButtonPoint = new(85, 235);
    internal static readonly Rectangle MoveButtonAnchorArea = new(35, 218, 100, 34);

    // The top-right X remains an advisory on-site signal, but V3.1.43 live
    // evidence showed remote currency digits can still mimic it. A real remote
    // move button must therefore be proved independently at its fixed left-side
    // anchor and cannot be canceled by the ambiguous X detector.
    internal static readonly Rectangle OnsiteCloseVisualArea = new(744, 42, 44, 44);

    internal static bool ShouldAcceptMoveButton(
        bool onsiteCloseVisible,
        bool moveShapeVisible,
        bool moveAnchorVisible)
    {
        _ = onsiteCloseVisible; // advisory only; never veto proven fixed-anchor remote UI
        return moveShapeVisible && moveAnchorVisible;
    }

    internal static bool IsSafeMoveGeometry()
        => MoveButtonVisualArea.Contains(MoveButtonPoint) &&
           MoveButtonVisualArea.Left >= 0 &&
           MoveButtonVisualArea.Top >= 0 &&
           MoveButtonVisualArea.Right <= 800 &&
           MoveButtonVisualArea.Bottom <= 1000 &&
           MoveButtonAnchorArea.Contains(MoveButtonPoint) &&
           MoveButtonAnchorArea.Left >= MoveButtonVisualArea.Left &&
           MoveButtonAnchorArea.Top >= MoveButtonVisualArea.Top &&
           MoveButtonAnchorArea.Right <= MoveButtonVisualArea.Right &&
           MoveButtonAnchorArea.Bottom <= MoveButtonVisualArea.Bottom &&
           OnsiteCloseVisualArea.Left >= 0 &&
           OnsiteCloseVisualArea.Top >= 0 &&
           OnsiteCloseVisualArea.Right <= 800 &&
           OnsiteCloseVisualArea.Bottom <= 1000;

    // Canonical 800x1000 hub: two rows of three cards. Include only each large
    // title, excluding the repeated facility name and level badge below it.

    // Late K return is safe to use for one last card selection ONLY after
    // the normal guarded attempts already clicked a single target card.
    // A field, an unknown screen, an overlay, or an extra K is never authority.
    internal static bool CanRecoverFinalHubSelection(
        bool managedKIssuedAfterCard, int previousCardClicks,
        bool exactSixCardHub, bool blockingModal)
        => managedKIssuedAfterCard && previousCardClicks == 1 &&
           exactSixCardHub && !blockingModal;

    internal static bool CanRetryFixedHubSelection(
        int previousCardClicks, bool previousHub, bool freshHub, bool blockingModal)
        => previousCardClicks == 1 && previousHub && freshHub && !blockingModal;

    // Never click the same title-coordinate twice. The body/image is an
    // independently located point within the selected 800x1000 card.
    // This still requires a fresh exact OCR match of the *same* card title.
    internal static Point AlternateCardClickPoint(string screenTitle)
    {
        Rectangle title = TitleArea(screenTitle);
        return new Point(title.Left + title.Width / 2, title.Top - 55);
    }

    internal static bool IsSafeAlternateCardClick(
        string screenTitle, Point original, Point alternate)
    {
        Rectangle title = TitleArea(screenTitle);
        Rectangle card = new(title.Left, title.Top - 105, title.Width, 280);
        return card.Contains(alternate) && !title.Contains(alternate) &&
               Math.Abs(original.X - alternate.X) +
               Math.Abs(original.Y - alternate.Y) >= 40;
    }

    internal static Rectangle TitleArea(string screenTitle)
    {
        int index = Array.FindIndex(AlteringPlan.Facilities,
            name => AlteringText.Normalize(name.Replace(" 시설", "")) == AlteringText.Normalize(screenTitle));
        if (index < 0) throw new InvalidDataException("알 수 없는 가공 시설입니다.");
        return new Rectangle(40 + 242 * (index % 3), 240 + 345 * (index / 3), 236, 42);
    }
}
