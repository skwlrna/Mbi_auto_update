namespace FishingAutomation;

internal static class AlteringRemoteProcessGuard
{
    // A single visual/OCR spike on a proven on-site detail screen is not enough
    // to veto processing. Only two consecutive remote-state observations block input.
    internal static bool ShouldBlock(bool firstRemote, bool secondRemote)
        => firstRemote && secondRemote;
}
