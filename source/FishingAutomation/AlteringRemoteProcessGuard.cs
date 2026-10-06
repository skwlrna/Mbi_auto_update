namespace FishingAutomation;

internal static class AlteringRemoteProcessGuard
{
    // A single visual/OCR spike on a proven on-site detail screen is not enough
    // to veto processing. Only two consecutive remote-state observations block input.
    internal static bool ShouldBlock(bool firstRemote, bool secondRemote)
        => ShouldBlock(
            firstRemote,
            secondRemote,
            onsiteFacilityConfirmed: false,
            firstOnsiteActionVisible: false,
            secondOnsiteActionVisible: false);

    // If the facility itself was already proven on-site and the dedicated on-site
    // action button body is visible in both fresh frames, a simultaneous
    // "가공하러 가기" OCR read is treated as a text false-positive. This preserves
    // the two-frame remote veto everywhere else.
    internal static bool ShouldBlock(
        bool firstRemote,
        bool secondRemote,
        bool onsiteFacilityConfirmed,
        bool firstOnsiteActionVisible,
        bool secondOnsiteActionVisible)
    {
        if (!firstRemote || !secondRemote)
            return false;

        bool provenOnsiteAction =
            onsiteFacilityConfirmed &&
            firstOnsiteActionVisible &&
            secondOnsiteActionVisible;

        return !provenOnsiteAction;
    }
}
