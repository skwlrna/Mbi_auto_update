namespace FishingAutomation;

internal static class AlteringReceiptPolicy
{

    // After a receipt path has already sent the one-shot facility move input,
    // the broad teal detector is no longer authoritative by itself. The 04:05
    // V3.1.50 live failure showed the on-site screen can still contain enough teal
    // pixels to mimic the old remote button shape. Keep the exact label as the
    // second, independent signal before treating that post-click screen as remote.
    internal static bool IsRemoteMoveButtonAfterReceiptMove(
        bool visualMoveButton,
        bool exactMoveLabelVisible)
        => visualMoveButton && exactMoveLabelVisible;

    // Before on-site receipt state is proven, the legacy visual detector remains a
    // hard veto. After TravelToFacilityAsync has completed its stable on-site proof,
    // a visual-only teal false positive may not block the blue receive control;
    // an exact "설비로 이동" label still blocks Space.
    internal static bool ShouldBlockReceiptForMoveButton(
        bool trustedOnsiteFacility,
        bool visualMoveButton,
        bool exactMoveLabelVisible)
        => exactMoveLabelVisible ||
           (visualMoveButton && !trustedOnsiteFacility);

    // A manager-directed receipt has already chosen onsite reuse or completed
    // a fresh travel. On those paths the always-visible move label is not
    // evidence of physical distance. Facility/title, modal, activity, completed
    // queue, blue-button and receipt-CLI checks remain separately mandatory.
    internal static bool ShouldBlockReceiptForMoveButton(
        AlteringFacilityEntryDirective directive,
        bool trustedOnsiteFacility,
        bool visualMoveButton,
        bool exactMoveLabelVisible)
        => directive == AlteringFacilityEntryDirective.Automatic &&
           ShouldBlockReceiptForMoveButton(
               trustedOnsiteFacility, visualMoveButton, exactMoveLabelVisible);

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
