namespace FishingAutomation.Telemetry;

// Only outcomes of existing, completed visual safety checks may enter this
// passive diagnostic. The enum is deliberately NOT a generic OCR label.
internal enum VerifiedScreenIncidentSignal
{
    GatheringStopControlDoubleChecked = 1,
    AbyssClearPairedFrames = 2,
    AbyssResultConfirmed = 3,
    AlteringFreeActionGuardPassed = 4,
    CraftingResultPopupGuardPassed = 5,
    FishingHookReadyTwoFrames = 6,
    DungeonRetryGuardPassed = 7
}

/// <summary>
/// A fixed-vocabulary, source-limited incident annotation. It can never
/// authorize a game action or AI repair. Lack of a comparable CLI activity
/// sample is reported as unknown, not disagreement or an offline game.
/// </summary>
internal static class VerifiedScreenIncidentDiagnostic
{
    internal static CommonIncidentArea AreaOf(VerifiedScreenIncidentSignal signal) =>
        signal switch
        {
            VerifiedScreenIncidentSignal.GatheringStopControlDoubleChecked =>
                CommonIncidentArea.Gathering,
            VerifiedScreenIncidentSignal.AbyssClearPairedFrames or
                VerifiedScreenIncidentSignal.AbyssResultConfirmed =>
                CommonIncidentArea.Abyss,
            VerifiedScreenIncidentSignal.AlteringFreeActionGuardPassed =>
                CommonIncidentArea.Altering,
            VerifiedScreenIncidentSignal.CraftingResultPopupGuardPassed =>
                CommonIncidentArea.Crafting,
            VerifiedScreenIncidentSignal.FishingHookReadyTwoFrames =>
                CommonIncidentArea.Fishing,
            VerifiedScreenIncidentSignal.DungeonRetryGuardPassed =>
                CommonIncidentArea.Dungeon,
            _ => CommonIncidentArea.Unknown
        };

    internal static string? Render(
        string? mode, VerifiedScreenIncidentSignal signal,
        DateTimeOffset observedAtUtc, DateTimeOffset incidentAtUtc,
        string? cliActivityOutcome)
    {
        var area = AreaOf(signal);
        if (area == CommonIncidentArea.Unknown ||
            CommonIncidentTriage.MapMode(mode) != area)
            return null;

        var age = incidentAtUtc.ToUniversalTime() -
            observedAtUtc.ToUniversalTime();
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(5))
            return null;

        string fixedSignal = signal switch
        {
            VerifiedScreenIncidentSignal.GatheringStopControlDoubleChecked =>
                "gathering_stop_control_double_checked",
            VerifiedScreenIncidentSignal.AbyssClearPairedFrames =>
                "abyss_clear_pair_guard_passed",
            VerifiedScreenIncidentSignal.AbyssResultConfirmed =>
                "abyss_result_guard_passed",
            VerifiedScreenIncidentSignal.AlteringFreeActionGuardPassed =>
                "altering_free_action_guard_passed",
            VerifiedScreenIncidentSignal.CraftingResultPopupGuardPassed =>
                "crafting_result_popup_guard_passed",
            VerifiedScreenIncidentSignal.FishingHookReadyTwoFrames =>
                "fishing_hook_ready_two_frames",
            VerifiedScreenIncidentSignal.DungeonRetryGuardPassed =>
                "dungeon_retry_guard_passed",
            _ => "unknown"
        };
        // The activity CLI can only describe gathering. The Abyss CLI
        // currently has no independently validated room/clear semantics.
        string comparison = area == CommonIncidentArea.Gathering &&
            cliActivityOutcome == "cli_gathering_in_progress"
            ? "compatible_gathering_hints_only"
            : "no_comparable_cli_state";

        long ageMs = Math.Clamp((long)age.TotalMilliseconds, 0, 5000);
        return "BEGIN VERIFIED SCREEN INCIDENT V1\n" +
            "area=" + area.ToString().ToLowerInvariant() + "\n" +
            "signal=" + fixedSignal + "\n" +
            "comparison=" + comparison + "\n" +
            "age_ms=" + ageMs + "\n" +
            "verified_cause=false;gameplay_authorized=false;repair_authorized=false\n" +
            "END VERIFIED SCREEN INCIDENT V1";
    }
}
