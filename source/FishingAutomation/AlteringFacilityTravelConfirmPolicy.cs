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

    // F01: a hidden facility title alone is NOT a travel transition. A brief
    // OCR miss can happen on a static/remote menu; CLI loading alone also
    // occurs during unrelated UI requests. For managed Fresh, allow only a
    // positive post-click auto-travel reading OR at least two CONSECUTIVE
    // transient CLI loading rejections observed while the header is absent.
    // The caller still requires stable return of the exact facility title,
    // a known idle CLI, no modal, and a delayed independent final check.
    internal static bool HasVerifiedManagedMoveTransition(
        bool moveClickSent,
        bool observedAutoTraveling,
        int consecutiveLoadingWithMissingHeader)
        => moveClickSent &&
           (observedAutoTraveling || consecutiveLoadingWithMissingHeader >= 2);

    // F01: a manager-directed Fresh move can leave a visible "move" button
    // even on-site. Only a POSITIVELY OBSERVED travel transition (not the
    // button's absence or merely a click) can replace that visual veto.
    // The caller also checks popup clearance and stable frames / final CLI.
    internal static bool IsManagedFreshArrivalObservation(
        bool facilityVisible,
        bool? autoTraveling,
        bool moveClickSent,
        bool transitionObserved)
        => facilityVisible &&
           autoTraveling == false &&
           moveClickSent &&
           transitionObserved;

    // V3.1.56 dual-anchor instant-arrival proof applies equally to managed
    // fresh registration and manager-directed receipt travel. V3.1.63
    // accidentally excluded receiptMode, so fast travel never reached stable
    // onsite verification and timed out even with a confirmed missing move
    // button. Receipt still uses its stricter facility-wide CLI and blue
    // collect-button checks AFTER arrival.
    internal static bool CanUseManagedInstantArrival(
        bool managedFreshArrival, bool receiptMode)
    {
        _ = receiptMode; // Receipt changes button classification, not arrival eligibility.
        return managedFreshArrival;
    }

    // A very short move (e.g. player already in front of the wood bench)
    // may finish before the first post-click capture. Require TWO separate
    // visual anchors: a POSITIVELY observed remote move button before the
    // click and the actual onsite close-X after the click, with the remote
    // move control absent. Still require CLI idle, no modal, 7 stable frames,
    // and an independent delayed final capture in the caller. Neither an
    // OCR title miss nor a click alone can establish this path.
    internal static bool HasVerifiedManagedInstantArrival(
        bool moveClickSent,
        bool preClickRemoteMoveButtonConfirmed,
        bool facilityVisible,
        bool onsiteCloseVisible,
        bool remoteMoveButtonVisible,
        bool? autoTraveling,
        bool anyModalVisible)
        => moveClickSent &&
           preClickRemoteMoveButtonConfirmed &&
           facilityVisible && onsiteCloseVisible &&
           !remoteMoveButtonVisible && autoTraveling == false &&
           !anyModalVisible;

    internal static bool HasStableOnsiteEvidence(
        int stableFrames,
        TimeSpan stableDuration)
        => stableFrames >= RequiredOnsiteStableFrames &&
           stableDuration >= RequiredOnsiteStableDuration;
}
