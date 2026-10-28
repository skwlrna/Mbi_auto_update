using FishingAutomation;

internal static class SceneArbiterRegression
{
    // 35 named L1/L2 assertions against the compiled production policy.
    // Real-private screenshots and actual Interception/Windows input are separate L4/L5 gates.
    internal static int Run(Action<bool, string> check)
    {
        int checks = 0;
        void T(string id, bool assertion)
        {
            check(assertion, "SceneArbiter " + id);
            checks++;
        }

        var world = ManagedProductionSceneArbiter.Classify(
            new ManagedProductionSceneSignals(WorldHud: true));
        var hub = ManagedProductionSceneArbiter.Classify(
            new ManagedProductionSceneSignals(ProcessingHub: true));
        var facility = ManagedProductionSceneArbiter.Classify(
            new ManagedProductionSceneSignals(FacilityPanel: true));
        var armor = ManagedProductionSceneArbiter.Classify(
            new ManagedProductionSceneSignals(ArmorMenu: true));
        var food = ManagedProductionSceneArbiter.Classify(
            new ManagedProductionSceneSignals(FoodMenu: true));
        var permit = new ManagedProductionActionEvidence(
            ManagedProductionSceneKind.WorldHud, TwoDistinctFrames: true,
            LatestFrameMatches: true, CliIdle: true, ExclusiveUiLease: true,
            NotCancelled: true, NoBlockingModal: true,
            ExactFacilityIdentity: true, CoordinatorOnsite: true,
            LaneOwnership: true, JournalReady: true,
            ExactRecipeIdentity: true, WithinSevenSlots: true,
            AllFacilityWorksCompleted: true, CorrectTabVerified: true,
            WeightBelowLimit: true);
        static bool Allow(ManagedProductionAction action, ManagedProductionActionEvidence evidence)
            => ManagedProductionSceneArbiter.Allows(action, evidence);

        T("R01 armour-vs-hub", armor == ManagedProductionSceneKind.ArmorMenu &&
            !Allow(ManagedProductionAction.SelectFacilityCard, permit with { Scene = armor }));
        T("R02 repeated-armour-still-not-hub",
            ManagedProductionSceneArbiter.Classify(new ManagedProductionSceneSignals(ArmorMenu:true))
            == armor && !Allow(ManagedProductionAction.EnterKMenu, permit with { Scene=armor }));
        T("R03 muted-metal-is-panel-not-world", facility == ManagedProductionSceneKind.FacilityPanel &&
            !Allow(ManagedProductionAction.EnterKMenu, permit with { Scene=facility }));
        T("R04 weight-limit-stops-gather", world == ManagedProductionSceneKind.WorldHud &&
            !Allow(ManagedProductionAction.BeginGathering, permit with { WeightBelowLimit=false }));
        T("R05 blue-cave-world-not-color-gated", world == ManagedProductionSceneKind.WorldHud &&
            Allow(ManagedProductionAction.EnterKMenu, permit));
        T("R06 night-cave-requires-two-captures", !Allow(ManagedProductionAction.EnterKMenu,
            permit with { TwoDistinctFrames=false }));
        T("R07 four-fields-need-final-viewport", !Allow(ManagedProductionAction.EnterKMenu,
            permit with { LatestFrameMatches=false }));
        T("R08 food-tab-only-no-K", food == ManagedProductionSceneKind.FoodMenu &&
            Allow(ManagedProductionAction.SelectProcessingTab, permit with { Scene=food }) &&
            !Allow(ManagedProductionAction.EnterKMenu, permit with { Scene=food }));
        T("R09 exact-processing-hub-allows-card", hub == ManagedProductionSceneKind.ProcessingHub &&
            Allow(ManagedProductionAction.SelectFacilityCard, permit with { Scene=hub }));
        T("R10 overlapping-armor-and-hub-is-ambiguous",
            ManagedProductionSceneArbiter.Classify(
                new ManagedProductionSceneSignals(ProcessingHub:true,ArmorMenu:true))
            == ManagedProductionSceneKind.Ambiguous);
        T("R11 modal-over-hub-blocks-card",
            !Allow(ManagedProductionAction.SelectFacilityCard, permit with {
                Scene=ManagedProductionSceneArbiter.Classify(
                    new ManagedProductionSceneSignals(ProcessingHub:true,OtherModal:true)) }));
        T("R12 teal-remote-is-not-onsite", !Allow(ManagedProductionAction.SelectRecipe,
            permit with { Scene=facility, CoordinatorOnsite=false }));
        T("R13 muted-remote-is-not-onsite", !Allow(ManagedProductionAction.ReceiveAll,
            permit with { Scene=facility, CoordinatorOnsite=false }));
        T("R14 other-facility-identity-denies-move", !Allow(ManagedProductionAction.ClickFacilityMove,
            permit with { Scene=facility, ExactFacilityIdentity=false }));
        T("R15 partial-render-loading-denies-input",
            ManagedProductionSceneArbiter.Classify(new ManagedProductionSceneSignals(
                FacilityPanel:true,Loading:true))==ManagedProductionSceneKind.LoadingTransition &&
            !Allow(ManagedProductionAction.ClickFacilityMove,
                permit with { Scene=ManagedProductionSceneKind.LoadingTransition }));
        var history = new ManagedProductionSceneHistory();
        T("R16 delayed-panel-needs-second-capture",
            history.Observe(1,"f1",facility,"metal")==1 &&
            history.Observe(2,"f2",facility,"metal")==2);
        history.Invalidate();
        T("R17 retry-not-authorized-by-stale-hub", history.Observe(7,"h1",hub,null)==1 &&
            history.Observe(7,"h1",hub,null)==0);
        T("R18 popup-over-hub-blocks-all-input",
            ManagedProductionSceneArbiter.Classify(new ManagedProductionSceneSignals(
                ProcessingHub:true,MoveDialog:true))==ManagedProductionSceneKind.MoveDialog &&
            !Allow(ManagedProductionAction.SelectFacilityCard,
                permit with { Scene=ManagedProductionSceneKind.MoveDialog }));
        T("R19 field-to-menu-race-denies-K", !Allow(ManagedProductionAction.EnterKMenu,
            permit with { Scene=food, LatestFrameMatches=false }));
        T("R20 facility-to-world-race-denies-card", !Allow(ManagedProductionAction.SelectFacilityCard,
            permit with { Scene=world, LatestFrameMatches=false }));
        T("R21 F10-denies-every-DOWN", !Allow(ManagedProductionAction.EnterKMenu,
            permit with { NotCancelled=false }) &&
            !Allow(ManagedProductionAction.RegisterRecipe,
                permit with { Scene=ManagedProductionSceneKind.RecipeDetail,NotCancelled=false }));
        T("R22 arrival-requires-onsite-affirmative", Allow(ManagedProductionAction.SelectRecipe,
            permit with { Scene=facility, CoordinatorOnsite=true }));
        T("R23 click-without-travel-is-not-onsite", !Allow(ManagedProductionAction.SelectRecipe,
            permit with { Scene=facility, CoordinatorOnsite=false }));
        T("R24 instant-arrival-cannot-ignore-identity", !Allow(
            ManagedProductionAction.SelectRecipe, permit with {
                Scene=facility, ExactFacilityIdentity=false, CoordinatorOnsite=true }));
        T("R25 intermediate-lane-lease-denies-registration", !Allow(
            ManagedProductionAction.RegisterRecipe, permit with {
                Scene=ManagedProductionSceneKind.RecipeDetail,LaneOwnership=false }));
        T("R26 partial-7-slot-receipt-denied", !Allow(ManagedProductionAction.ReceiveAll,
            permit with { Scene=facility,AllFacilityWorksCompleted=false }));
        T("R27 full-completed-7-slot-receipt-allowed", Allow(ManagedProductionAction.ReceiveAll,
            permit with { Scene=facility,AllFacilityWorksCompleted=true }));
        T("R28 uncertain-journal-denies-replay", !Allow(ManagedProductionAction.RegisterRecipe,
            permit with { Scene=ManagedProductionSceneKind.RecipeDetail,JournalReady=false }));
        T("R29 disappearance-of-UI-not-world-handoff", !ManagedProductionSceneArbiter.ConfirmsWorldHandoff(
            permit with { Scene=ManagedProductionSceneKind.Unknown }));
        T("R30 positive-world-and-idle-can-handoff", ManagedProductionSceneArbiter.ConfirmsWorldHandoff(permit));
        T("R31 cold-start-is-not-onsite", !Allow(ManagedProductionAction.ReceiveAll,
            permit with { Scene=facility,CoordinatorOnsite=false }));
        var duplicate = new ManagedProductionSceneHistory();
        T("R32 single-PNG-twice-is-not-two-frames",
            duplicate.Observe(1,"same-png",world,null)==1 &&
            duplicate.Observe(2,"same-png",world,null)==0);
        T("R33 wrong-viewport-denies-all-fixed-input",
            ManagedProductionSceneArbiter.Classify(new ManagedProductionSceneSignals(
                ProcessingHub:true,ViewportValid:false))==ManagedProductionSceneKind.Unknown &&
            !Allow(ManagedProductionAction.SelectFacilityCard,
                permit with { Scene=hub,ViewportValid=false }));
        T("R34 abyss-exclusive-lease-denies-production", !Allow(
            ManagedProductionAction.ClickFacilityMove,
            permit with { Scene=facility,ExclusiveUiLease=false }));
        T("R35 automatic-unmodified-and-safety-invariants",
            typeof(FacilityLaneState).GetMethod("AssertAccess",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic) is not null &&
            typeof(MultiAlteringCoordinator) is not null &&
            !Allow(ManagedProductionAction.RegisterRecipe,
                permit with { Scene=ManagedProductionSceneKind.RecipeDetail,WithinSevenSlots=false }) &&
            !Allow(ManagedProductionAction.EnterKMenu,permit with { CliIdle=false }));
        return checks;
    }
}
