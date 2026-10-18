namespace FishingAutomation.ExportDiagnostics;

/// <summary>
/// Audited read-only coverage map of *existing* MabiAuto queries. These are
/// integration requirements, not discovered third-party packet fields.
/// Data meaning is never inferred from file extension, syntax, traffic size
/// or a TCP process-name match.
/// </summary>
internal static class IntegrationCoveragePolicy
{
    internal sealed record Coverage(
        string Domain, string ExistingAuthority, string ExistingQueries,
        string ThirdPartyExport, bool ProductionPacketEnabled);

    private static readonly Coverage[] Known =
    {
        new("items_inventory", "cli_readonly", "get_items,get_inventory",
            "not_required", false),
        new("activity", "cli_readonly", "get_activity",
            "not_required", false),
        new("altering", "cli_readonly",
            "get_alterable_items,get_altering_works",
            "not_required", false),
        new("gathering", "cli_readonly", "get_gatherable_items",
            "not_required", false),
        new("recipes", "cli_readonly", "get_craftable_items",
            "not_required", false),
        new("environment", "cli_readonly", "get_current_environment",
            "not_required", false),
        new("identity", "cli_readonly", "get_my_info",
            "not_required", false),
        new("currencies", "cli_readonly", "get_currencies",
            "not_required", false),
        new("battle_snapshot", "unverified_offline_export",
            "none", "requires_real_decoded_sample", false),
        new("boss_emblem", "screen_recognition",
            "none", "no_verified_packet_schema", false),
        new("abyss_room_position", "screen_recognition",
            "none", "no_verified_packet_schema", false),
        new("abyss_clear", "screen_two_consecutive_evidence",
            "none", "no_verified_packet_schema", false)
    };

    internal static IReadOnlyList<Coverage> Entries => Array.AsReadOnly(Known);

    internal sealed record Decision(
        string Domain, string Status, bool CanReplaceExistingAuthority,
        bool GameplayAuthorized);

    /// <summary>
    /// Shape validation tells us ONLY whether offline syntax is well formed.
    /// The policy intentionally has no "trust third-party packet data" path.
    /// A future change will require concrete schema and provenance review.
    /// </summary>
    internal static Decision Evaluate(
        string domain, bool offlineExportStructureValid)
    {
        var entry = Known.FirstOrDefault(x =>
            string.Equals(x.Domain, domain, StringComparison.Ordinal));
        if (entry is null)
            return new(domain, "unknown_domain", false, false);
        if (entry.ThirdPartyExport == "not_required")
            return new(entry.Domain, "existing_cli_authoritative", false, false);
        if (entry.ThirdPartyExport == "requires_real_decoded_sample")
            return new(entry.Domain,
                offlineExportStructureValid
                    ? "structure_only_needs_semantics_and_provenance"
                    : "missing_verified_export", false, false);
        return new(entry.Domain, "screen_authority_no_verified_packet_event",
            false, false);
    }
}
