namespace FishingAutomation.Telemetry;

/// <summary>
/// Single cross-feature, fixed-vocabulary diagnostic taxonomy for existing
/// runtime-error events. Modes are existing UI-owned exact labels; neither
/// free-form title/detail nor packet bytes are inputs. All labels are hints,
/// never confirmed causes or gameplay/repair authorizations.
/// </summary>
internal enum CommonIncidentArea
{
    Unknown = 0,
    Gathering = 1,
    Altering = 2,
    Crafting = 3,
    Fishing = 4,
    Dungeon = 5,
    Abyss = 6,
    Scheduler = 7
}

internal sealed record CommonIncidentFact(string Query, string Outcome);

internal sealed record CommonIncidentAssessment(
    CommonIncidentArea Area, string Coverage, string Hint, string Connectivity,
    int RelevantFacts, bool ConfirmedCause = false,
    bool AuthorizesGameplay = false, bool AuthorizesRepair = false)
{
    // An independent stage-18 block is attached to the existing local
    // error.txt for the already opted-in AI watcher. No caller-supplied
    // string, title/detail, identifier or raw CLI output is interpolated.
    internal string ToEvidenceBlock() =>
        "BEGIN COMMON INCIDENT TRIAGE V1\n" +
        "area=" + Area.ToString().ToLowerInvariant() + "\n" +
        "coverage=" + Coverage + "\n" +
        "hint=" + Hint + "\n" +
        "connectivity=" + Connectivity + "\n" +
        "relevant_facts=" + Math.Clamp(RelevantFacts, 0, 5) + "\n" +
        "verified_cause=false;gameplay_authorized=false;repair_authorized=false\n" +
        "END COMMON INCIDENT TRIAGE V1";
}

internal static class CommonIncidentTriage
{
    internal static CommonIncidentArea MapMode(string? mode) => mode switch
    {
        "채집" => CommonIncidentArea.Gathering,
        "가공" => CommonIncidentArea.Altering,
        "제작" => CommonIncidentArea.Crafting,
        "낚시" => CommonIncidentArea.Fishing,
        "던전" or "페카 심층" => CommonIncidentArea.Dungeon,
        "어비스" => CommonIncidentArea.Abyss,
        "스케줄러" => CommonIncidentArea.Scheduler,
        _ => CommonIncidentArea.Unknown
    };

    /// <summary>
    /// Facts MUST originate from the allowlisted previous CLI query cache.
    /// Evaluates no external data and has no side effects.
    /// </summary>
    internal static CommonIncidentAssessment Evaluate(
        string? mode, IReadOnlyCollection<CommonIncidentFact>? facts)
    {
        var area = MapMode(mode);
        string hint = "no_domain_specific_evidence";
        string network = "connectivity_unverified";
        int relevant = 0;
        // A limited preview of the already bounded source: never turn an
        // unrelated CLI success into evidence of a dungeon/fishing clear.
        foreach (var fact in (facts ?? Array.Empty<CommonIncidentFact>()).Take(5))
        {
            if (fact.Query == "status")
            {
                network = fact.Outcome switch
                {
                    "cli_connected" => "read_only_cli_connected_only",
                    "cli_explicit_disconnect" => "cli_disconnect_reported",
                    "cli_timeout_state_unknown" => "cli_timeout_not_disconnect",
                    "cli_unavailable_state_unknown" => "cli_unavailable_not_disconnect",
                    "cli_reports_game_off" => "cli_game_off_reported",
                    _ => network
                };
                continue;
            }

            // Positive evidence is only mode-specific. The same source
            // cannot corroborate itself, and generic query success is not
            // a gameplay event. A detailed hint is a hypothesis only.
            string? next = area switch
            {
                CommonIncidentArea.Gathering when
                    fact.Query == "get_activity" &&
                    fact.Outcome == "cli_gathering_in_progress" =>
                    "gathering_reported_in_progress",
                CommonIncidentArea.Altering when
                    fact.Query == "get_altering_works" =>
                    fact.Outcome switch
                    {
                        "cli_altering_possible_removed_not_completed" =>
                            "possible_work_removal_not_completion",
                        "cli_altering_possible_finished_receipt_unknown" =>
                            "work_completed_rows_receipt_unknown",
                        "cli_work_rows_completed_receipt_unknown" =>
                            "work_completed_rows_receipt_unknown",
                        "cli_altering_work_running" => "work_reported_in_progress",
                        "cli_no_work_rows_completion_unknown" =>
                            "empty_work_list_not_completion",
                        _ => null
                    },
                _ => null
            };

            if (next is null) continue;
            relevant++;
            hint = next;
        }
        var coverage = area switch
        {
            CommonIncidentArea.Unknown => "unknown_mode_no_verified_state",
            CommonIncidentArea.Gathering or CommonIncidentArea.Altering
                when relevant > 0 => "domain_cli_hint_only",
            CommonIncidentArea.Gathering or CommonIncidentArea.Altering =>
                "domain_data_missing",
            CommonIncidentArea.Crafting => "no_verified_crafting_state",
            CommonIncidentArea.Fishing => "no_verified_fishing_state",
            CommonIncidentArea.Dungeon or CommonIncidentArea.Abyss =>
                "no_verified_dungeon_state",
            CommonIncidentArea.Scheduler => "no_verified_scheduler_run_at_error",
            _ => "unknown_mode_no_verified_state"
        };
        return new(area, coverage, hint, network, relevant);
    }
}
