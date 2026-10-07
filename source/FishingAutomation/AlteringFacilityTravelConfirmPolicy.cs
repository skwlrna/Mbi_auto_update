namespace FishingAutomation;

internal static class AlteringFacilityTravelConfirmPolicy
{
    internal const int RequiredOnsiteStableFrames = 7;
    internal const int MaxTravelConfirmationSpaces = 2;
    internal static readonly TimeSpan RequiredOnsiteStableDuration = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan FinalOnsiteRecheckDelay = TimeSpan.FromMilliseconds(1200);

    internal static bool ShouldConfirmAfterMoveClick(
        bool travelPopupVisual,
        int confirmationSpaceCount,
        bool departureAlreadySeen)
        => travelPopupVisual &&
           !departureAlreadySeen &&
           confirmationSpaceCount >= 0 &&
           confirmationSpaceCount < MaxTravelConfirmationSpaces;

    internal static bool IsOnsiteObservation(
        bool facilityVisible,
        bool moveButtonVisible,
        bool? autoTraveling)
        => facilityVisible &&
           !moveButtonVisible &&
           autoTraveling == false;

    internal static bool HasStableOnsiteEvidence(
        int stableFrames,
        TimeSpan stableDuration)
        => stableFrames >= RequiredOnsiteStableFrames &&
           stableDuration >= RequiredOnsiteStableDuration;
}
