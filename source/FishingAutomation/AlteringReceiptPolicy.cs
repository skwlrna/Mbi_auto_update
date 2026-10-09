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

    // The blue "모두 받기" belongs to the entire facility, not the
    // selected recipe. Managed mixed-batch receipt requires all works in this
    // facility to be completed before that single UI input, and requires zero
    // works afterwards. Automatic/single-altering keeps its existing bounded
    // decrease verification because unfinished neighboring work can coexist.
    // F02: partial completion is a normal waiting/registration state, not a
    // blanket authorization to collect and not a fatal scheduler error.
    internal static bool IsPartialManagedFacility(
        IReadOnlyList<AlteringWork> works, string facilityName)
    {
        var facilityWorks = works.Where(x => x.FacilityName == facilityName).ToArray();
        return facilityWorks.Any(x => x.IsCompleted) &&
               facilityWorks.Any(x => !x.IsCompleted);
    }

    internal static bool CanCollectManagedFacility(
        IReadOnlyList<AlteringWork> works,
        string facilityName)
    {
        ArgumentNullException.ThrowIfNull(works);
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityName);
        var facilityWorks = works.Where(x =>
            x.FacilityName == facilityName).ToArray();
        return facilityWorks.Length > 0 &&
               facilityWorks.All(x => x.IsCompleted);
    }

    internal static bool IsManagedFacilityReceiptConfirmed(int before, int after)
        => before > 0 && after == 0;

    internal static bool IsCliReceiptConfirmed(int before, int after)
        => before >= 0 && after >= 0 && after < before;

    // V3.1.66: a failed confirmation after the FIRST receive Space is not
    // authorization for a blind second Space. The entire facility work count
    // must be EXACTLY unchanged and still all complete, with the actual blue
    // receive control on a fresh, onsite, idle, modal-free frame. A partial
    // decrease, empty lane, unknown CLI or completion popup is never retried.
    internal static bool CanRetryManagedBlueReceipt(
        bool managedReceipt,
        int? beforeCount,
        int? afterCount,
        bool allFacilityWorksComplete,
        bool blueButtonVisible,
        bool facilityVisible,
        bool onsiteCloseVisible,
        bool anyModalVisible,
        bool? autoTraveling,
        bool alreadyRetried)
        => managedReceipt && !alreadyRetried &&
           beforeCount is > 0 && afterCount == beforeCount &&
           allFacilityWorksComplete && blueButtonVisible &&
           facilityVisible && onsiteCloseVisible &&
           !anyModalVisible && autoTraveling == false;

    // A facility title or a lowered CLI work count is not physical location
    // evidence if a result/travel dialog remains, or movement is unresolved.
    // Caller must observe this condition in two consecutive fresh frames.
    internal static bool CanConfirmReceiptFacilityReturn(
        bool facilityHeaderVisible,
        bool completionModalVisible,
        bool travelDialogVisible,
        bool? autoTraveling)
        => facilityHeaderVisible &&
           !completionModalVisible &&
           !travelDialogVisible &&
           autoTraveling == false;

    // F04: unlike Automatic single processing, manager-controlled receipt must
    // positively observe the character not traveling before EVERY close Space.
    internal static bool CanSendCompletionCloseSpace(bool managedReceipt, bool? autoTraveling)
        => !managedReceipt || autoTraveling == false;

    // F04: a green button may belong to a travel or unrelated modal.
    // Only a positively identified processing-completed result can authorize
    // manager-controlled confirmation Space. Unknown activity fails closed.
    internal static bool CanCloseManagedCompletionResult(
        bool greenConfirmVisible,
        bool completedTitleVisible,
        bool facilityVisible,
        bool travelDialogVisible,
        bool? autoTraveling)
        => greenConfirmVisible && completedTitleVisible &&
           !facilityVisible && !travelDialogVisible &&
           autoTraveling == false;

    // Manager-only OCR-independent completion close. The visual reward layout
    // is an alternative to result-title OCR, NEVER an alternative to a confirmed
    // whole-facility receipt, modal/onsite separation or fresh idle CLI.
    // Keep the original five-parameter predicate for legacy call/test coverage.
    internal static bool CanCloseManagedCompletionResult(
        bool greenConfirmVisible,
        bool completedTitleVisible,
        bool fixedRewardLayoutVisible,
        bool cliWholeFacilityReceiptConfirmed,
        bool facilityVisible,
        bool travelDialogVisible,
        bool? autoTraveling)
        => cliWholeFacilityReceiptConfirmed &&
           CanCloseManagedCompletionResult(
               greenConfirmVisible,
               completedTitleVisible || fixedRewardLayoutVisible,
               facilityVisible,
               travelDialogVisible,
               autoTraveling);

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
