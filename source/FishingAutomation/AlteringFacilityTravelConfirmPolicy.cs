namespace FishingAutomation;

internal static class AlteringFacilityTravelConfirmPolicy
{
    internal const int RequiredOnsiteStableFrames = 7;
    internal static readonly TimeSpan RequiredOnsiteStableDuration = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan FinalOnsiteRecheckDelay = TimeSpan.FromMilliseconds(1200);

    internal static bool ShouldConfirm(
        bool greenConfirmationVisible,
        bool travelDialogVisible,
        bool confirmationAlreadySent)
        => greenConfirmationVisible &&
           travelDialogVisible &&
           !confirmationAlreadySent;

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
