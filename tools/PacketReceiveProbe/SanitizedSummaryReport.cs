using System.Globalization;
using System.Text;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Explicit opt-in export of ONLY allowlisted counters and fixed result codes.
/// No arbitrary text, identifiers, IPs, process names, packet bytes or CLI
/// response bodies may enter the exported report. It is never auto-saved.
/// </summary>
internal static class SanitizedSummaryReport
{
    internal const string SaveConsent = "SAVE";
    internal const string FileName = "PacketDiagnostic_Summary.txt";

    private static readonly HashSet<string> AllowedFlowStatus = new(StringComparer.Ordinal)
    {
        "os_flow_table_unavailable", "ipv4_tcp_4tuple_matched",
        "no_ipv4_tcp_4tuple_match"
    };
    private static readonly HashSet<string> AllowedEvidence = new(StringComparer.Ordinal)
    {
        "independent_sources_agree", "cli_only", "network_candidate_only",
        "conflicting_signals", "no_observation", "unattributed_traffic",
        "unavailable", "incomplete_evidence", "invalid_evidence"
    };
    private static readonly HashSet<string> AllowedCliStatus = new(StringComparer.Ordinal)
    {
        "cli_connected", "cli_readonly_object", "cli_disconnected",
        "cli_game_or_option_off", "cli_rejected", "cli_nonzero_exit",
        "cli_unclassified_status", "cli_unexpected_response",
        "cli_empty", "cli_oversize", "cli_invalid_json", "cli_invalid_schema",
        "cli_command_blocked", "cli_not_installed", "cli_not_started",
        "cli_timeout", "cli_cancelled", "cli_unavailable",
        "cli_json_without_status", "cli_accepted", "cli_success", "cli_other_status"
    };

    internal sealed record Stage(
        string StageName,
        long Frames,
        long FrameBytes,
        long FlowMatched,
        long FlowUnmatched,
        long NamedCandidateFrames,
        string FlowStatus,
        string CliStatus,
        string CliActivity,
        string Evidence);

    internal static bool TryCreate(IReadOnlyList<Stage>? stages, out string report)
    {
        report = string.Empty;
        if (stages is null || stages.Count != GuidedDiagnosticPolicy.MaxStages)
            return false;

        var builder = new StringBuilder();
        builder.AppendLine("MabiAuto read-only diagnostic summary v1");
        builder.AppendLine("Independent evidence only; NOT a decoded game event or automation authorization.");
        builder.AppendLine("No packet payload, IP/port endpoint, PID, process name, character ID, CLI JSON or token.");
        for (int i = 0; i < stages.Count; i++)
        {
            Stage row = stages[i];
            if (row.StageName != GuidedDiagnosticPolicy.StageNames[i] ||
                row.Frames < 0 || row.FrameBytes < 0 ||
                row.FlowMatched < 0 || row.FlowUnmatched < 0 ||
                row.NamedCandidateFrames < 0 ||
                row.FlowMatched > row.Frames ||
                row.FlowUnmatched > row.Frames ||
                row.NamedCandidateFrames > row.FlowMatched ||
                row.FlowMatched + row.FlowUnmatched > row.Frames ||
                !AllowedFlowStatus.Contains(row.FlowStatus) ||
                !AllowedCliStatus.Contains(row.CliStatus) ||
                !AllowedCliStatus.Contains(row.CliActivity) ||
                !AllowedEvidence.Contains(row.Evidence))
                return false;

            builder.Append("compare stage=").Append(row.StageName)
                .Append(" frames=").Append(row.Frames.ToString(CultureInfo.InvariantCulture))
                .Append(" frame_bytes=").Append(row.FrameBytes.ToString(CultureInfo.InvariantCulture))
                .Append(" flow_matched=").Append(row.FlowMatched.ToString(CultureInfo.InvariantCulture))
                .Append(" flow_unmatched=").Append(row.FlowUnmatched.ToString(CultureInfo.InvariantCulture))
                .Append(" named_candidate_frames=").Append(row.NamedCandidateFrames.ToString(CultureInfo.InvariantCulture))
                .Append(" flow_status=").Append(row.FlowStatus)
                .Append(" cli_status=").Append(row.CliStatus)
                .Append(" cli_activity=").Append(row.CliActivity)
                .Append(" evidence=").Append(row.Evidence)
                .AppendLine();
        }

        report = builder.ToString();
        return true;
    }

    /// <summary>
    /// User must type literal SAVE after all stages complete. The target file
    /// is an invariant local basename in the extracted diagnostic directory.
    /// Never overwrites a previous report; no paths or exception text printed.
    /// </summary>
    internal static bool TrySave(
        string? consent, IReadOnlyList<Stage>? stages,
        string directory, out string savedFileName)
    {
        savedFileName = string.Empty;
        if (consent != SaveConsent || !TryCreate(stages, out string content))
            return false;

        try
        {
            for (int index = 0; index < 100; index++)
            {
                string fileName = index == 0 ? FileName :
                    $"PacketDiagnostic_Summary_{index:000}.txt";
                string path = Path.Combine(directory, fileName);
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.Write(content);
                    savedFileName = fileName;
                    return true;
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Never overwrite existing user test results.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            // No raw exception message or absolute user path may reach console.
        }
        return false;
    }
}
