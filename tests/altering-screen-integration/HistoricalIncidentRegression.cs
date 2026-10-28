using System.Drawing;
using FishingAutomation;

// Historical error inputs are taken from the runtime-errors summary.json corpus
// in skwlrna/Mbi_auto_log. This suite proves the compiled F9 safety policy
// against each incident family; it does not claim to reproduce live CLI/input
// timing or to replay private complete screenshots.
internal static class HistoricalIncidentRegression
{
    internal static int Run(Action<bool, string> check, Func<string, Bitmap, bool> pixel)
    {
        int cases = 0;
        void Case(string id, bool safe, string behavior)
        {
            check(safe, "historical " + id + " · " + behavior);
            cases++;
        }
        var all = new ManagedProductionActionEvidence(
            ManagedProductionSceneKind.WorldHud,
            TwoDistinctFrames: true, LatestFrameMatches: true,
            CliIdle: true, ExclusiveUiLease: true, NotCancelled: true,
            NoBlockingModal: true, ExactFacilityIdentity: true,
            CoordinatorOnsite: true, LaneOwnership: true,
            JournalReady: true, ExactRecipeIdentity: true,
            WithinSevenSlots: true, AllFacilityWorksCompleted: true,
            CorrectTabVerified: true, WeightBelowLimit: true);
        static bool Allows(ManagedProductionAction action, ManagedProductionActionEvidence evidence)
            => ManagedProductionSceneArbiter.Allows(action, evidence);
        var world = ManagedProductionSceneKind.WorldHud;
        var hub = ManagedProductionSceneKind.ProcessingHub;
        var facility = ManagedProductionSceneKind.FacilityPanel;
        var detail = ManagedProductionSceneKind.RecipeDetail;
        var food = ManagedProductionSceneKind.FoodMenu;
        var armor = ManagedProductionSceneKind.ArmorMenu;
        var dialog = ManagedProductionSceneKind.MoveDialog;

        var fixture = Path.Combine("tests", "altering-screen-integration");
        using var remoteWood = new Bitmap(Path.Combine(fixture,
            "facility-move-fixtures", "remote-wood-20261007-172137.png"));
        using var onsiteMetal = new Bitmap(Path.Combine(fixture,
            "facility-move-fixtures", "onsite-metal-20261023-153131.png"));
        using var undergroundField = new Bitmap(Path.Combine(fixture,
            "facility-move-fixtures", "field-metal-20261024-033339.png"));
        using var hubFrame = new Bitmap(Path.Combine(AppContext.BaseDirectory,
            "facility-hub-fixtures", "hub-wood-20261026-161326.png"));
        using var grass = new Bitmap(Path.Combine(AppContext.BaseDirectory,
            "managed-field-fixtures", "field-grass-20261013-141648.png"));
        using var town = new Bitmap(Path.Combine(AppContext.BaseDirectory,
            "managed-field-fixtures", "field-town-20261020-234501.png"));
        using var caveDay = new Bitmap(Path.Combine(AppContext.BaseDirectory,
            "managed-field-fixtures", "cave-20261026-174515.png"));
        using var caveNight = new Bitmap(Path.Combine(AppContext.BaseDirectory,
            "managed-field-fixtures", "cave-20261026-190933.png"));

        Case("20261007_172137 remote-wood",
            pixel("HasFacilityPanelStructureVisual", remoteWood) &&
            pixel("HasFacilityMoveButtonVisual", remoteWood) &&
            !Allows(ManagedProductionAction.SelectRecipe,
                all with { Scene = facility, CoordinatorOnsite = false }),
            "remote panel cannot grant onsite recipe authority");
        Case("20261007_202053 move-popup",
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = dialog }) &&
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = facility, CliIdle = false }),
            "move dialog and unknown activity never authorize repeat move");
        Case("20261013_141648 weak-HUD",
            pixel("HasManagedKEntryFieldHudVisual", grass) &&
            Allows(ManagedProductionAction.EnterKMenu, all with { Scene = world }) &&
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, TwoDistinctFrames = false }),
            "field visual accepted only with two stable frames and safe CLI");
        Case("20261013_212456 abyss-exit",
            !ManagedProductionSceneArbiter.ConfirmsWorldHandoff(
                all with { Scene = ManagedProductionSceneKind.Unknown }),
            "missing verified outside field cannot hand control to processing");
        Case("20261020_234501 K-layout",
            pixel("HasManagedKEntryFieldHudVisual", town) &&
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, LatestFrameMatches = false }),
            "town field screenshot cannot replace final input-frame evidence");
        Case("20261023_060800 HUD-food",
            !Allows(ManagedProductionAction.EnterKMenu, all with { Scene = food }) &&
            Allows(ManagedProductionAction.SelectProcessingTab,
                all with { Scene = food }),
            "K forbidden over identified food menu, processing tab separately gated");
        Case("20261023_150720 onsite-timeout",
            !Allows(ManagedProductionAction.SelectRecipe,
                all with { Scene = facility, CoordinatorOnsite = false }),
            "timeout does not establish onsite facility lease");
        Case("20261023_153131 late-arrival",
            pixel("HasFacilityPanelStructureVisual", onsiteMetal) &&
            !Allows(ManagedProductionAction.ReceiveAll,
                all with { Scene = facility, CoordinatorOnsite = false }),
            "onsite-looking pixels alone do not prove manager arrival");
        Case("20261023_175806 intermediate-owner",
            !Allows(ManagedProductionAction.RegisterRecipe,
                all with { Scene = detail, LaneOwnership = false }) &&
            !Allows(ManagedProductionAction.ReceiveAll,
                all with { Scene = facility, LaneOwnership = false }),
            "intermediate lease prevents main registration and receipt");
        Case("20261024_033339 field-false-facility",
            pixel("HasWorldHudForFacilityVeto", undergroundField) &&
            !pixel("HasVerifiedFacilityMoveClickScreen", undergroundField),
            "underground world field cannot authorize fixed facility move");
        Case("20261024_153253 remote-facility",
            pixel("HasFacilityPanelStructureVisual", remoteWood) &&
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = facility, ExactFacilityIdentity = false }),
            "generic facility structure cannot authorize another facility");
        Case("20261024_181341 HUD-drift",
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, LatestFrameMatches = false }),
            "changed final HUD screenshot vetoes K");
        Case("20261024_183512 unidentified-field",
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = ManagedProductionSceneKind.Unknown }),
            "HUD miss never grants blind K");
        Case("20261024_233624 moving-CLI",
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = hub, CliIdle = false }),
            "reported movement vetoes facility card input");
        Case("20261025_071501 dialogue",
            ManagedProductionSceneArbiter.Classify(new(
                FacilityPanel: true, MoveDialog: true)) == dialog &&
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = dialog }),
            "travel popup overrides underlying facility");
        Case("20261025_071617 moving-CLI",
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, CliIdle = false }),
            "moving never permits a new K action");
        Case("20261025_071735 facility-card-retry",
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = hub, LatestFrameMatches = false }),
            "failed card navigation cannot authorize a stale second click");
        Case("20261026_115417 gathering-stall",
            !Allows(ManagedProductionAction.BeginGathering,
                all with { Scene = world, CliIdle = false }),
            "unknown movement or gathering activity prevents new input");
        Case("20261026_115823 material-modal",
            !Allows(ManagedProductionAction.RegisterRecipe,
                all with { Scene = ManagedProductionSceneKind.OtherModal }) &&
            !Allows(ManagedProductionAction.RegisterRecipe,
                all with { Scene = detail, NoBlockingModal = false }),
            "unknown transfer confirmation is not recipe registration");
        Case("20261026_161326 hub-card",
            pixel("HasFixedProcessingHubVisual", hubFrame) &&
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = armor }),
            "real six-card hub positive while armor menu is a veto");
        Case("20261026_174515 cave-day",
            pixel("HasManagedKEntryFieldHudVisual", caveDay) &&
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, TwoDistinctFrames = false }),
            "day cave field requires two distinct captures");
        Case("20261026_190933 cave-night",
            pixel("HasManagedKEntryFieldHudVisual", caveNight) &&
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = world, ExclusiveUiLease = false }),
            "night cave field never overrides other-module ownership");
        Case("20261027_015827 facility-identity",
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = facility }) &&
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = facility, ExactFacilityIdentity = false }),
            "failed facility card cannot become confirmed target");
        Case("20261028_104145 armor-false-hub",
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = armor }) &&
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = armor }),
            "armor menu cannot impersonate hub or field");
        Case("20261028_110114 armor-hub-overlap",
            ManagedProductionSceneArbiter.Classify(new(
                ProcessingHub: true, ArmorMenu: true)) ==
                ManagedProductionSceneKind.Ambiguous &&
            !Allows(ManagedProductionAction.SelectFacilityCard,
                all with { Scene = ManagedProductionSceneKind.Ambiguous }),
            "simultaneous armor/hub evidence blocks input");
        Case("20261028_133046 92pct-bag",
            !Allows(ManagedProductionAction.BeginGathering,
                all with { Scene = world, WeightBelowLimit = false }),
            "weight threshold veto remains independent of HUD");
        Case("20261028_143857 gray-metal",
            !Allows(ManagedProductionAction.EnterKMenu,
                all with { Scene = facility }) &&
            !Allows(ManagedProductionAction.ClickFacilityMove,
                all with { Scene = facility, ExactFacilityIdentity = false }),
            "muted facility panel must not be routed into blind K/move");
        return cases;
    }
}
