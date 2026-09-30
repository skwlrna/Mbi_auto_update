using System.Drawing;

namespace FishingAutomation;

internal static class AlteringFacilityLayout
{
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
