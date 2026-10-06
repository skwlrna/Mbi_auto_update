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
}
