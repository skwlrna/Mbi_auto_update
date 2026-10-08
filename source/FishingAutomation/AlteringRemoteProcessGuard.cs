namespace FishingAutomation;

internal static class AlteringRemoteProcessGuard
{
    // A single OCR spike is not enough to prove the remote paid-action state.
    // Two consecutive fresh observations are required before changing navigation.
    internal static bool ShouldBlock(bool firstRemote, bool secondRemote)
        => firstRemote && secondRemote;

    // A confirmed remote-detail state gets exactly one bounded recovery attempt:
    // close the detail, return to the facility screen, perform the known free
    // facility move once, then reselect the recipe. If the paid-action state is
    // still confirmed after that, input must stop instead of looping or re-clicking.
    internal static bool CanRecover(bool remoteConfirmed, bool recoveryAlreadyUsed)
        => remoteConfirmed && !recoveryAlreadyUsed;

    // Two-frame OCR disagreement is a reportable conflict, not permission for
    // the screen to revoke manager authority or execute another facility move.
    internal static bool MustReportToCoordinator(
        AlteringFacilityEntryDirective directive,
        bool remoteConfirmed)
        => directive != AlteringFacilityEntryDirective.Automatic &&
           remoteConfirmed;

    // Compatibility overload retained for the existing regression project.
    // Production QueueAsync no longer uses this branch in V3.1.31.
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
