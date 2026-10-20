using FishingAutomation.Telemetry;

internal static class Stage18Tests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Stage18: " + description);
            checks++;
        }
        var validModes = new (string Mode, CommonIncidentArea Area)[]
        {
            ("채집", CommonIncidentArea.Gathering),
            ("가공", CommonIncidentArea.Altering),
            ("제작", CommonIncidentArea.Crafting),
            ("낚시", CommonIncidentArea.Fishing),
            ("던전", CommonIncidentArea.Dungeon),
            ("페카 심층", CommonIncidentArea.Dungeon),
            ("어비스", CommonIncidentArea.Abyss),
            ("스케줄러", CommonIncidentArea.Scheduler)
        };
        foreach (var pair in validModes)
            Check(CommonIncidentTriage.MapMode(pair.Mode) == pair.Area,
                "real UI mode not mapped: " + pair.Mode);
        foreach (string invalid in new[] { "", "가공 무조건 성공", "밥", "채집!", "user-name", "UNKNOWN" })
            Check(CommonIncidentTriage.MapMode(invalid) == CommonIncidentArea.Unknown,
                "unknown/user-supplied mode was trusted");
        Check(CommonIncidentTriage.MapMode(null) == CommonIncidentArea.Unknown,
            "missing mode promoted to known category");

        var facts = new[] {
            new CommonIncidentFact("status", "cli_timeout_state_unknown"),
            new CommonIncidentFact("get_activity", "cli_gathering_in_progress"),
            new CommonIncidentFact("get_altering_works",
                "cli_altering_possible_removed_not_completed"),
            new CommonIncidentFact("get_items", "cli_inventory_shape_only_no_delta")
        };
        var gathering = CommonIncidentTriage.Evaluate("채집", facts);
        Check(gathering is { Area: CommonIncidentArea.Gathering,
            Coverage: "domain_cli_hint_only",
            Hint: "gathering_reported_in_progress", RelevantFacts: 1,
            Connectivity: "cli_timeout_not_disconnect", ConfirmedCause: false,
            AuthorizesGameplay: false, AuthorizesRepair: false },
            "gathering and connectivity evidence wrongly mixed into confirmed cause");
        var altering = CommonIncidentTriage.Evaluate("가공", facts);
        Check(altering is { Area: CommonIncidentArea.Altering,
            Hint: "possible_work_removal_not_completion", RelevantFacts: 1,
            ConfirmedCause: false, AuthorizesRepair: false },
            "Stage17 work-removal hint lost in common incident view");
        foreach (var pair in validModes.Where(x =>
            x.Area is not CommonIncidentArea.Gathering and not CommonIncidentArea.Altering))
        {
            var assessed = CommonIncidentTriage.Evaluate(pair.Mode, facts);
            Check(assessed.RelevantFacts == 0 &&
                assessed.Hint == "no_domain_specific_evidence" &&
                !assessed.ConfirmedCause && !assessed.AuthorizesGameplay &&
                !assessed.AuthorizesRepair,
                "unrelated CLI evidence masqueraded as " + pair.Mode + " state");
        }
        Check(CommonIncidentTriage.Evaluate("제작", facts)
            .Coverage == "no_verified_crafting_state",
            "recipe catalog/inventory shape presented as confirmed crafting");
        Check(CommonIncidentTriage.Evaluate("낚시", facts)
            .Coverage == "no_verified_fishing_state",
            "no fishing state reported as verified fishing");
        Check(CommonIncidentTriage.Evaluate("어비스", facts)
            .Coverage == "no_verified_dungeon_state",
            "no abyss state reported as verified clear");
        Check(CommonIncidentTriage.Evaluate("스케줄러", facts)
            .Coverage == "no_verified_scheduler_run_at_error",
            "a passive CLI success asserted scheduler completion");
        var unknown = CommonIncidentTriage.Evaluate("PRIVATE_PLAYER", facts);
        Check(unknown.Coverage == "unknown_mode_no_verified_state",
            "arbitrary personal mode leaked as a state");

        var spoofing = new[] {
            new CommonIncidentFact("get_altering_works", "cancelled_confirmed"),
            new CommonIncidentFact("get_items", "cli_gathering_in_progress"),
            new CommonIncidentFact("execute_altering", "cli_altering_work_running")
        };
        Check(CommonIncidentTriage.Evaluate("가공", spoofing)
            is { Coverage: "domain_data_missing", RelevantFacts: 0,
                Hint: "no_domain_specific_evidence" },
            "fake evidence was elevated into action authority");

        var noData = CommonIncidentTriage.Evaluate("채집", Array.Empty<CommonIncidentFact>());
        Check(noData is { Coverage: "domain_data_missing",
            RelevantFacts: 0, Connectivity: "connectivity_unverified" },
            "no CLI facts were treated as verified");

        string evidence = altering.ToEvidenceBlock();
        Check(evidence.StartsWith("BEGIN COMMON INCIDENT TRIAGE V1\n",
            StringComparison.Ordinal), "missing versioned marker");
        Check(evidence.EndsWith("END COMMON INCIDENT TRIAGE V1",
            StringComparison.Ordinal), "missing triage footer");
        Check(evidence.Contains("verified_cause=false;gameplay_authorized=false;repair_authorized=false",
            StringComparison.Ordinal), "triage gave any automatic action authority");
        foreach (string secret in new[] { "PRIVATE_PLAYER", "execute_altering", "밥",
            "token", "password", "get_items", "game.exe" })
            Check(!evidence.Contains(secret, StringComparison.OrdinalIgnoreCase),
                "triage block exposed raw content or private text");

        var time = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        AiPassiveCliEvidence.SetEnabled(false);
        Check(AiPassiveCliEvidence.SnapshotForIncident("가공", time) is null,
            "AI is disabled but incident context was emitted");
        AiPassiveCliEvidence.SetEnabled(true);
        string? idle = AiPassiveCliEvidence.SnapshotForIncident("어비스", time);
        Check(idle is not null && idle.Contains("no_verified_dungeon_state",
            StringComparison.Ordinal), "unknown abyss coverage hidden");
        AiPassiveCliEvidence.Observe("get_altering_works", true, "connected",
            null, time.AddMilliseconds(-100), "work_running");
        AiPassiveCliEvidence.Observe("get_activity", true, "connected",
            null, time.AddMilliseconds(-70), "gathering_observed");
        AiPassiveCliEvidence.Observe("status", false, "disconnected", "timeout",
            time.AddMilliseconds(-40));
        var applied = AiPassiveCliEvidence.SnapshotForIncident("가공", time);
        Check(applied is not null &&
            applied.Contains("hint=work_reported_in_progress", StringComparison.Ordinal) &&
            applied.Contains("connectivity=cli_timeout_not_disconnect", StringComparison.Ordinal) &&
            applied.Contains("relevant_facts=1", StringComparison.Ordinal),
            "previous validated CLI facts did not reach error-time incident context");
        Check(AiPassiveCliEvidence.SnapshotForIncident("낚시", time)!
            .Contains("hint=no_domain_specific_evidence", StringComparison.Ordinal),
            "gathering facts were misinterpreted as fishing game state");
        Check(AiPassiveCliEvidence.SnapshotForIncident("가공",
            time.AddSeconds(6))!.Contains("coverage=domain_data_missing",
                StringComparison.Ordinal), "expired hints promoted to fresh state");
        AiPassiveCliEvidence.Observe("get_activity", true, "connected", null,
            time.AddMilliseconds(100), "gathering_observed");
        Check(AiPassiveCliEvidence.SnapshotForIncident("채집", time)!
            .Contains("relevant_facts=1", StringComparison.Ordinal),
            "post-error activity overwrote historical pre-error evidence");
        AiPassiveCliEvidence.SetEnabled(false);
        Check(AiPassiveCliEvidence.SnapshotForIncident("가공", time) is null,
            "watcher stop failed to purge all common incident context");
        Console.WriteLine("PASS " + checks +
            " Stage18 synthetic cross-mode safe incident triage assertions");
    }
}
