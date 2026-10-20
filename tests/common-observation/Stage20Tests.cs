using FishingAutomation.Telemetry;

internal static class Stage20Tests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Stage20: " + label);
            checks++;
        }

        var errorAt = new DateTimeOffset(2026, 10, 18, 14, 15, 0, TimeSpan.Zero);
        var cases = new (string Mode, CommonIncidentArea Area,
            VerifiedScreenIncidentSignal Signal, string Label)[]
        {
            ("가공", CommonIncidentArea.Altering,
                VerifiedScreenIncidentSignal.AlteringFreeActionGuardPassed,
                "altering_free_action_guard_passed"),
            ("제작", CommonIncidentArea.Crafting,
                VerifiedScreenIncidentSignal.CraftingResultPopupGuardPassed,
                "crafting_result_popup_guard_passed"),
            ("낚시", CommonIncidentArea.Fishing,
                VerifiedScreenIncidentSignal.FishingHookReadyTwoFrames,
                "fishing_hook_ready_two_frames"),
            ("던전", CommonIncidentArea.Dungeon,
                VerifiedScreenIncidentSignal.DungeonRetryGuardPassed,
                "dungeon_retry_guard_passed"),
            ("페카 심층", CommonIncidentArea.Dungeon,
                VerifiedScreenIncidentSignal.DungeonRetryGuardPassed,
                "dungeon_retry_guard_passed")
        };

        AiPassiveCliEvidence.SetEnabled(false);
        foreach (var sample in cases)
        {
            Check(VerifiedScreenIncidentDiagnostic.AreaOf(sample.Signal) ==
                sample.Area, "wrong category for " + sample.Mode);
            AiPassiveCliEvidence.ObserveVerifiedScreen(
                sample.Signal, errorAt.AddMilliseconds(-500));
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt) is null, "disabled watcher used " + sample.Mode);
        }
        AiPassiveCliEvidence.SetEnabled(true);
        foreach (var sample in cases)
        {
            AiPassiveCliEvidence.SetEnabled(true);
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt) is null, "opened independent OCR " + sample.Mode);
            AiPassiveCliEvidence.ObserveVerifiedScreen(
                sample.Signal, errorAt.AddMilliseconds(-350));
            var block = AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt);
            Check(block != null && block.Contains(
                    "area=" + sample.Area.ToString().ToLowerInvariant(),
                    StringComparison.Ordinal) &&
                block.Contains("signal=" + sample.Label, StringComparison.Ordinal) &&
                block.Contains("comparison=no_comparable_cli_state",
                    StringComparison.Ordinal) &&
                block.Contains("age_ms=350", StringComparison.Ordinal),
                "unexpected screen-only evidence " + sample.Mode);
            Check(block!.Contains(
                "verified_cause=false;gameplay_authorized=false;repair_authorized=false",
                StringComparison.Ordinal), "repair/game input authorized " + sample.Mode);

            // An unrelated live query must not be treated as corroboration.
            AiPassiveCliEvidence.Observe("get_altering_works", true, "connected",
                null, errorAt.AddMilliseconds(-300), "work_running");
            AiPassiveCliEvidence.Observe("get_activity", true, "connected",
                null, errorAt.AddMilliseconds(-200), "gathering_observed");
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt)!.Contains(
                "comparison=no_comparable_cli_state", StringComparison.Ordinal),
                "false CLI cross-confirmation " + sample.Mode);

            foreach (var other in cases.Where(x => x.Area != sample.Area))
                Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                    other.Mode, errorAt) is null,
                    "cross-mode screenshot leakage: " + sample.Mode + " -> " + other.Mode);
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt.AddSeconds(6)) is null,
                "expired screen admitted for " + sample.Mode);
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt.AddSeconds(-1)) is null,
                "post-error screen admitted for " + sample.Mode);
            Check(VerifiedScreenIncidentDiagnostic.Render(sample.Mode,
                sample.Signal, errorAt.AddSeconds(-6), errorAt, null) is null,
                "stale screen bypassed render TTL " + sample.Mode);

            AiPassiveCliEvidence.SetEnabled(false);
            Check(AiPassiveCliEvidence.SnapshotForScreenIncident(
                sample.Mode, errorAt) is null,
                "watcher did not purge mode " + sample.Mode);
            AiPassiveCliEvidence.SetEnabled(true);
        }
        Check(VerifiedScreenIncidentDiagnostic.AreaOf(
            (VerifiedScreenIncidentSignal)int.MaxValue) == CommonIncidentArea.Unknown,
            "unknown signal gained authority");
        Check(VerifiedScreenIncidentDiagnostic.Render("어비스",
            VerifiedScreenIncidentSignal.DungeonRetryGuardPassed,
            errorAt.AddMilliseconds(-100), errorAt, null) is null,
            "normal dungeon signal reclassified as abyss");
        Check(VerifiedScreenIncidentDiagnostic.Render("채집",
            VerifiedScreenIncidentSignal.CraftingResultPopupGuardPassed,
            errorAt.AddMilliseconds(-100), errorAt, null) is null,
            "crafting signal reclassified as gathering");
        Check(VerifiedScreenIncidentDiagnostic.Render("가공",
            VerifiedScreenIncidentSignal.AlteringFreeActionGuardPassed,
            errorAt.AddMilliseconds(1), errorAt, null) is null,
            "future sample included in diagnostics");
        AiPassiveCliEvidence.SetEnabled(false);
        Console.WriteLine("PASS " + checks +
            " Stage20 cross-feature passive visual guard coverage assertions");
    }
}
