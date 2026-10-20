namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Assesses agreement of three INDEPENDENT diagnostic signals. It never
/// decodes or verifies a game event, and never grants gameplay authority.
/// Negative CLI outcomes are not automatically proof that the game is off.
/// </summary>
internal static class TransportEvidenceAssessment
{
    internal sealed record Observation(
        string CliStatus, long TotalFrames, long MatchingNamedCandidateFrames,
        long UnparsedFrames, long CapacityDrops,
        bool AtLeastOneOsSocketSampleAvailable);

    internal sealed record Result(
        string Evidence, string Explanation, bool PacketMeaningVerified,
        bool AuthorizesGameplay)
    {
        internal static Result Make(string evidence, string explanation) =>
            new(evidence, explanation, false, false);
    }

    internal static Result Assess(Observation observation)
    {
        if (observation.TotalFrames < 0 ||
            observation.MatchingNamedCandidateFrames < 0 ||
            observation.UnparsedFrames < 0 || observation.CapacityDrops < 0 ||
            observation.MatchingNamedCandidateFrames > observation.TotalFrames)
            return Result.Make("invalid_evidence", "bad_diagnostic_counts");

        // Incomplete capture / OS evidence fails closed instead of inferring
        // that silence, capture mismatch, or unknown NIC implies disconnection.
        if (observation.CapacityDrops > 0)
            return Result.Make("incomplete_evidence", "flow_capacity_exceeded");
        if (!observation.AtLeastOneOsSocketSampleAvailable)
            return Result.Make("unavailable", "os_socket_table_unavailable");

        if (observation.CliStatus == "cli_connected")
        {
            return observation.MatchingNamedCandidateFrames > 0
                ? Result.Make("independent_sources_agree", "cli_connected_and_tcp_flow_candidate")
                : Result.Make("cli_only", "no_matching_named_tcp_candidate");
        }

        if (observation.MatchingNamedCandidateFrames > 0)
        {
            return observation.CliStatus is "cli_disconnected" or "cli_game_or_option_off"
                ? Result.Make("conflicting_signals", "tcp_candidate_seen_but_cli_negative")
                : Result.Make("network_candidate_only", "cli_not_confirmed_connected");
        }

        // The absence of packets is never proof of game_off.
        if (observation.TotalFrames == 0)
            return Result.Make("no_observation", "silence_does_not_prove_offline");

        return Result.Make("unattributed_traffic", "tcp_frames_not_matched_to_named_candidate");
    }
}
