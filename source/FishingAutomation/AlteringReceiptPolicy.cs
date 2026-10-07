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

    // A missed facility-return screen may be a normal FIELD, but reopening
    // requires multiple clean visual observations and a trustworthy idle CLI.
    internal static bool CanRecoverFieldAfterCompletion(
        bool fieldOnly,
        bool confirmationVisible,
        bool? autoTraveling,
        bool safeField,
        int? receiptWorkCountBefore)
        => fieldOnly &&
           !confirmationVisible &&
           autoTraveling == false &&
           safeField &&
           receiptWorkCountBefore is > 0;

    // Re-entering a facility is NOT proof of a receipt: queue must decrease.
    internal static bool IsProvenReceiptAfterReopen(int? before, int? after)
        => before is int start && after is int end &&
           IsCliReceiptConfirmed(start, end);
}
