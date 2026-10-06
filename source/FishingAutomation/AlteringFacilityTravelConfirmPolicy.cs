namespace FishingAutomation;

internal static class AlteringFacilityTravelConfirmPolicy
{
    internal static bool ShouldConfirm(
        bool greenConfirmationVisible,
        bool travelDialogVisible,
        bool confirmationAlreadySent)
        => greenConfirmationVisible &&
           travelDialogVisible &&
           !confirmationAlreadySent;
}
