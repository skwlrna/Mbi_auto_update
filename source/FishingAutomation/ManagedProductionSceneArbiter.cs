namespace FishingAutomation;

// F9-only scene policy. This classifier never sends input or grants onsite/ledger ownership.
internal enum ManagedProductionSceneKind
{
    Unknown, WorldHud, ProcessingHub, FacilityPanel, FoodMenu, ArmorMenu,
    OtherProductionMenu, RecipeDetail, MoveDialog, ReceiptResult,
    OtherModal, LoadingTransition, Ambiguous
}

internal enum ManagedProductionAction
{
    EnterKMenu, SelectProcessingTab, SelectFacilityCard, ClickFacilityMove,
    SelectRecipe, RegisterRecipe, ReceiveAll, CloseKnownPanel, BeginGathering
}

internal readonly record struct ManagedProductionSceneSignals(
    bool WorldHud = false,
    bool ProcessingHub = false,
    bool FacilityPanel = false,
    bool FoodMenu = false,
    bool ArmorMenu = false,
    bool OtherProductionMenu = false,
    bool RecipeDetail = false,
    bool MoveDialog = false,
    bool ReceiptResult = false,
    bool OtherModal = false,
    bool Loading = false,
    bool ViewportValid = true);

internal readonly record struct ManagedProductionActionEvidence(
    ManagedProductionSceneKind Scene,
    bool TwoDistinctFrames = false,
    bool LatestFrameMatches = false,
    bool CliIdle = false,
    bool ExclusiveUiLease = false,
    bool NotCancelled = false,
    bool NoBlockingModal = false,
    bool ExactFacilityIdentity = false,
    bool CoordinatorOnsite = false,
    bool LaneOwnership = false,
    bool JournalReady = false,
    bool ExactRecipeIdentity = false,
    bool WithinSevenSlots = false,
    bool AllFacilityWorksCompleted = false,
    bool CorrectTabVerified = false,
    bool WeightBelowLimit = false,
    bool ViewportValid = true);

internal static class ManagedProductionSceneArbiter
{
    // A popup or a loading phase overrides underlying panel graphics. Two competing
    // normal scene candidates are ambiguous; "not a facility" never means "world".
    internal static ManagedProductionSceneKind Classify(ManagedProductionSceneSignals s)
    {
        if (!s.ViewportValid) return ManagedProductionSceneKind.Unknown;
        if (s.MoveDialog) return ManagedProductionSceneKind.MoveDialog;
        if (s.ReceiptResult) return ManagedProductionSceneKind.ReceiptResult;
        if (s.OtherModal) return ManagedProductionSceneKind.OtherModal;
        if (s.Loading) return ManagedProductionSceneKind.LoadingTransition;

        int count = (s.WorldHud ? 1 : 0) + (s.ProcessingHub ? 1 : 0) +
                    (s.FacilityPanel ? 1 : 0) + (s.FoodMenu ? 1 : 0) +
                    (s.ArmorMenu ? 1 : 0) + (s.OtherProductionMenu ? 1 : 0) +
                    (s.RecipeDetail ? 1 : 0);
        if (count > 1) return ManagedProductionSceneKind.Ambiguous;
        if (s.RecipeDetail) return ManagedProductionSceneKind.RecipeDetail;
        if (s.FacilityPanel) return ManagedProductionSceneKind.FacilityPanel;
        if (s.ProcessingHub) return ManagedProductionSceneKind.ProcessingHub;
        if (s.ArmorMenu) return ManagedProductionSceneKind.ArmorMenu;
        if (s.FoodMenu) return ManagedProductionSceneKind.FoodMenu;
        if (s.OtherProductionMenu) return ManagedProductionSceneKind.OtherProductionMenu;
        if (s.WorldHud) return ManagedProductionSceneKind.WorldHud;
        return ManagedProductionSceneKind.Unknown;
    }

    // A single-use approval decision: it MUST be re-evaluated against the
    // capture obtained by the final ClickVerifiedFresh/TapVerifiedFresh boundary.
    // Never persist this answer or convert unknown CLI/identity to false.
    internal static bool Allows(ManagedProductionAction action, ManagedProductionActionEvidence e)
    {
        if (!e.ViewportValid || !e.NotCancelled || !e.ExclusiveUiLease || !e.CliIdle ||
            !e.NoBlockingModal || !e.TwoDistinctFrames || !e.LatestFrameMatches)
            return false;
        return action switch
        {
            ManagedProductionAction.EnterKMenu =>
                e.Scene == ManagedProductionSceneKind.WorldHud,
            ManagedProductionAction.SelectProcessingTab =>
                (e.Scene == ManagedProductionSceneKind.FoodMenu ||
                 e.Scene == ManagedProductionSceneKind.OtherProductionMenu) &&
                e.CorrectTabVerified,
            ManagedProductionAction.SelectFacilityCard =>
                e.Scene == ManagedProductionSceneKind.ProcessingHub && e.LaneOwnership,
            ManagedProductionAction.ClickFacilityMove =>
                e.Scene == ManagedProductionSceneKind.FacilityPanel &&
                e.ExactFacilityIdentity && e.LaneOwnership,
            ManagedProductionAction.SelectRecipe =>
                e.Scene == ManagedProductionSceneKind.FacilityPanel &&
                e.ExactFacilityIdentity && e.CoordinatorOnsite &&
                e.LaneOwnership && e.JournalReady,
            ManagedProductionAction.RegisterRecipe =>
                e.Scene == ManagedProductionSceneKind.RecipeDetail &&
                e.ExactFacilityIdentity && e.CoordinatorOnsite &&
                e.LaneOwnership && e.JournalReady && e.ExactRecipeIdentity &&
                e.WithinSevenSlots,
            ManagedProductionAction.ReceiveAll =>
                e.Scene == ManagedProductionSceneKind.FacilityPanel &&
                e.ExactFacilityIdentity && e.CoordinatorOnsite &&
                e.LaneOwnership && e.JournalReady && e.AllFacilityWorksCompleted,
            ManagedProductionAction.CloseKnownPanel =>
                e.Scene is ManagedProductionSceneKind.FacilityPanel or
                    ManagedProductionSceneKind.ProcessingHub or
                    ManagedProductionSceneKind.RecipeDetail,
            ManagedProductionAction.BeginGathering =>
                e.Scene == ManagedProductionSceneKind.WorldHud && e.WeightBelowLimit,
            _ => false
        };
    }

    // Positive world evidence is mandatory: absence of a known menu is not proof.
    internal static bool ConfirmsWorldHandoff(ManagedProductionActionEvidence e)
        => e.ViewportValid && e.Scene == ManagedProductionSceneKind.WorldHud &&
           e.TwoDistinctFrames && e.LatestFrameMatches &&
           e.CliIdle && e.NoBlockingModal && e.NotCancelled &&
           e.ExclusiveUiLease;
}

// Fixture frames must come from independent captures, not repeated references to
// a single saved PNG. Stability never grants an input without final revalidation.
internal sealed class ManagedProductionSceneHistory
{
    private long _lastSequence = -1;
    private string? _lastCaptureId;
    private string? _lastFacility;
    private ManagedProductionSceneKind _lastScene;
    private int _streak;

    internal int Observe(long captureSequence, string captureId,
        ManagedProductionSceneKind scene, string? exactFacility)
    {
        if (string.IsNullOrWhiteSpace(captureId) ||
            captureSequence <= _lastSequence || captureId == _lastCaptureId)
        {
            _streak = 0;
            return 0;
        }
        _streak = _lastSequence >= 0 && _lastScene == scene &&
                  string.Equals(_lastFacility, exactFacility, StringComparison.Ordinal)
            ? Math.Min(_streak + 1, 2) : 1;
        _lastSequence = captureSequence;
        _lastCaptureId = captureId;
        _lastScene = scene;
        _lastFacility = exactFacility;
        return _streak;
    }

    internal void Invalidate()
    {
        _streak = 0;
        _lastSequence = -1;
        _lastCaptureId = null;
        _lastFacility = null;
        _lastScene = ManagedProductionSceneKind.Unknown;
    }
}
