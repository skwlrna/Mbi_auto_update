using System.Drawing;

namespace FishingAutomation;

internal static class AlteringFacilityLayout
{
    // User-confirmed 800x1000 remote facility screen. The "설비로 이동" button
    // is always the same left-side teal pill. These coordinates are intentionally
    // independent of OCR so missing text recognition cannot be mistaken for on-site.
    internal static readonly Rectangle MoveButtonVisualArea = new(15, 205, 155, 65);
    internal static readonly Point MoveButtonPoint = new(85, 235);

    internal static bool IsSafeMoveGeometry()
        => MoveButtonVisualArea.Contains(MoveButtonPoint) &&
           MoveButtonVisualArea.Left >= 0 &&
           MoveButtonVisualArea.Top >= 0 &&
           MoveButtonVisualArea.Right <= 800 &&
           MoveButtonVisualArea.Bottom <= 1000;

    // Canonical 800x1000 hub: two rows of three cards. Include only each large
    // title, excluding the repeated facility name and level badge below it.
    internal static Rectangle TitleArea(string screenTitle)
    {
        int index = Array.FindIndex(AlteringPlan.Facilities,
            name => AlteringText.Normalize(name.Replace(" 시설", "")) == AlteringText.Normalize(screenTitle));
        if (index < 0) throw new InvalidDataException("알 수 없는 가공 시설입니다.");
        return new Rectangle(40 + 242 * (index % 3), 240 + 345 * (index / 3), 236, 42);
    }
}
