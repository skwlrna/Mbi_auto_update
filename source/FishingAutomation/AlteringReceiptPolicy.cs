namespace FishingAutomation;

internal static class AlteringReceiptPolicy
{
    internal static bool CanConfirmCompletion(
        bool greenConfirmVisible,
        bool facilityVisible,
        bool travelDialogVisible,
        bool autoTraveling)
        => greenConfirmVisible &&
           !facilityVisible &&
           !travelDialogVisible &&
           !autoTraveling;
}
