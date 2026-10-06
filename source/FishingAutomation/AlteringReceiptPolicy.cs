namespace FishingAutomation;

internal static class AlteringReceiptPolicy
{
    internal static bool IsCliReceiptConfirmed(int before, int after)
        => before >= 0 && after >= 0 && after < before;

    internal static bool CanConfirmCompletion(
        bool greenConfirmVisible,
        bool facilityVisible,
        bool travelDialogVisible,
        bool autoTraveling)
        => greenConfirmVisible &&
           !facilityVisible &&
           !travelDialogVisible &&
           !autoTraveling;

    internal static bool CanRetryCompletionClose(
        bool greenConfirmVisible,
        bool facilityVisible,
        bool travelDialogVisible,
        bool autoTraveling)
        => greenConfirmVisible &&
           !facilityVisible &&
           !travelDialogVisible &&
           !autoTraveling;

    internal static bool CanConfirmCliReceiptCompletion(
        bool greenConfirmVisible,
        bool facilityVisible,
        bool travelDialogVisible)
        => greenConfirmVisible &&
           !facilityVisible &&
           !travelDialogVisible;

    internal static bool CanRetryCliReceiptCompletionClose(
        bool greenConfirmVisible,
        bool facilityVisible,
        bool travelDialogVisible)
        => greenConfirmVisible &&
           !facilityVisible &&
           !travelDialogVisible;
}
