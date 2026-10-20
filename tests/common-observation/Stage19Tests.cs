using FishingAutomation.Telemetry;

internal static class Stage19Tests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Stage19: " + name);
            checks++;
        }

        var t = new DateTimeOffset(2026, 10, 18, 13, 30, 0, TimeSpan.Zero);
        var stop = VerifiedScreenIncidentSignal.GatheringStopControlDoubleChecked;
        var clear = VerifiedScreenIncidentSignal.AbyssClearPairedFrames;
        var result = VerifiedScreenIncidentSignal.AbyssResultConfirmed;
        AiPassiveCliEvidence.SetEnabled(false);
        AiPassiveCliEvidence.ObserveVerifiedScreen(stop, t);
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("채집", t) is null,
            "disabled watcher gathered visual evidence");
        AiPassiveCliEvidence.SetEnabled(true);
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("채집", t) is null,
            "opened a capture without existing screen decision");

        AiPassiveCliEvidence.ObserveVerifiedScreen(stop, t.AddSeconds(-2));
        string gather = AiPassiveCliEvidence.SnapshotForScreenIncident("채집", t)!;
        Check(gather.Contains("signal=gathering_stop_control_double_checked",
            StringComparison.Ordinal) &&
            gather.Contains("comparison=no_comparable_cli_state", StringComparison.Ordinal) &&
            gather.Contains("age_ms=2000", StringComparison.Ordinal),
            "screen-only hint was incorrectly correlated with CLI");
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("가공", t) is null &&
              AiPassiveCliEvidence.SnapshotForScreenIncident("어비스", t) is null,
            "screen hint was attributed to an unrelated macro mode");

        AiPassiveCliEvidence.Observe("get_activity", true, "connected", null,
            t.AddSeconds(-1), "gathering_observed");
        gather = AiPassiveCliEvidence.SnapshotForScreenIncident("채집", t)!;
        Check(gather.Contains("comparison=compatible_gathering_hints_only",
            StringComparison.Ordinal), "pre-error CLI gathering hint not correlated");
        Check(!gather.Contains("PRIVATE_CHARACTER", StringComparison.Ordinal) &&
              !gather.Contains("server_clear_confirmed", StringComparison.Ordinal) &&
              gather.Contains("verified_cause=false;gameplay_authorized=false;repair_authorized=false",
                  StringComparison.Ordinal),
            "private values or authority leaked into screen report");

        AiPassiveCliEvidence.ObserveVerifiedScreen(clear, t.AddMilliseconds(-500));
        var abyss = AiPassiveCliEvidence.SnapshotForScreenIncident("어비스", t);
        Check(abyss is not null &&
            abyss.Contains("signal=abyss_clear_pair_guard_passed", StringComparison.Ordinal) &&
            abyss.Contains("comparison=no_comparable_cli_state", StringComparison.Ordinal),
            "Abyss guard claimed a decoded packet or CLI semantic cross-check");
        AiPassiveCliEvidence.ObserveVerifiedScreen(result, t.AddMilliseconds(-300));
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("어비스", t)!
            .Contains("signal=abyss_result_guard_passed", StringComparison.Ordinal),
            "latest separate Abyss result guard not selected");
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("던전", t) is null,
            "Abyss evidence treated as general dungeon screen");

        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("어비스",
            t.AddSeconds(6)) is null, "expired screen evidence escaped 5s TTL");
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("어비스",
            t.AddMilliseconds(-700)) is null, "post-error screen was used");
        AiPassiveCliEvidence.ObserveVerifiedScreen((VerifiedScreenIncidentSignal)999, t);
        Check(VerifiedScreenIncidentDiagnostic.AreaOf((VerifiedScreenIncidentSignal)999) ==
            CommonIncidentArea.Unknown, "unknown visual enum gained semantics");
        AiPassiveCliEvidence.ObserveVerifiedScreen(result, t.AddSeconds(-2));
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("어비스", t)!
            .Contains("signal=abyss_result_guard_passed", StringComparison.Ordinal),
            "out-of-order callback replaced latest screen decision");

        AiPassiveCliEvidence.SetEnabled(false);
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("채집", t) is null,
            "watcher stop retained screen evidence");
        AiPassiveCliEvidence.SetEnabled(true);
        Check(AiPassiveCliEvidence.SnapshotForScreenIncident("어비스", t) is null,
            "screen evidence leaked into a new watcher session");
        Check(VerifiedScreenIncidentDiagnostic.Render("어비스", clear,
            t.AddSeconds(1), t, null) is null,
            "future-only observation admitted");
        Check(VerifiedScreenIncidentDiagnostic.Render("어비스", clear,
            t.AddSeconds(-6), t, null) is null,
            "stale direct visual diagnostic admitted");
        AiPassiveCliEvidence.SetEnabled(false);

        Console.WriteLine("PASS " + checks +
            " Stage19 existing-screen-only, opt-in, strict timing and privacy checks");
    }
}
