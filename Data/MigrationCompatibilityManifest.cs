namespace TexTrack.Web.Data;

/// <summary>
/// Explicitly reviewed legacy migration identities whose resulting schema is reconciled by
/// later forward migrations. Historical schema_versions rows must never be rewritten.
/// </summary>
internal static class MigrationCompatibilityManifest
{
    private static readonly ApprovedMigrationIdentity[] Approved =
    [
        new(11,
            "011_jwo_component_rate_material_out_cancel_fix.sql",
            "1B0FF0E7AC0160DF8C533EDD2F5E1C7FE2CA36C14836DB39CEB85A63469A25CF",
            "011_stock_movement_kind_length.sql",
            "9A8D89C8074E5964AEAE78E8363DFE34FCBCE7AD15C6D1894B6B58551E56A063"),
        new(14,
            "014_nature_of_process.sql",
            "368BEADDA484C4C4558F977D7535B992F22DCD5BE01DA5F772B517EF5E63702C",
            "014_material_in_stock_movement_kinds.sql",
            "844F42E413C9B6F6D80252BDB17E000C4A33A3200605E8FF9D9DB3EE085B5AB3")
    ];

    internal static bool IsApprovedEquivalent(
        int version,
        string appliedFileName,
        string appliedChecksum,
        string currentFileName,
        string currentChecksum) =>
        Approved.Any(x =>
            x.Version == version &&
            string.Equals(x.AppliedFileName, appliedFileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.AppliedChecksum, appliedChecksum, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CurrentFileName, currentFileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CurrentChecksum, currentChecksum, StringComparison.OrdinalIgnoreCase));

    private sealed record ApprovedMigrationIdentity(
        int Version,
        string AppliedFileName,
        string AppliedChecksum,
        string CurrentFileName,
        string CurrentChecksum);
}
