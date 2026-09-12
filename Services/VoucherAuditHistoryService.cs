using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;

namespace TexTrack.Web.Services;

public static class VoucherAuditActions
{
    public const string Baseline = "Baseline";
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Cancel = "Cancel";
    public const string Delete = "Delete";
    public const string ImportCreate = "ImportCreate";
    public const string ImportUpdate = "ImportUpdate";
    public const string ImportCancel = "ImportCancel";
    public const string SystemStatusUpdate = "SystemStatusUpdate";
}

/// <summary>
/// Records full, display-stable voucher snapshots. Callers must already have an
/// open database transaction; this makes the business mutation and audit row a
/// single atomic commit. Audit rows have no navigation to the live voucher.
/// </summary>
public sealed class VoucherAuditHistoryService(IDbContextFactory<TexTrackDbContext> contextFactory)
{
    public const int CurrentSnapshotSchemaVersion = 1;
    public const string LegacyBaselineReason =
        "Current state captured when full voucher audit history was introduced. Earlier unrecorded revisions are unknown; this is not the original creation snapshot.";

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false
    };

    private static readonly SnapshotTable[] SnapshotTables =
    [
        new("voucher", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.id), '[]'::jsonb)::text
            FROM (
                SELECT v.*,
                       c.name AS company_name_at_revision,
                       fy.name AS financial_year_name_at_revision,
                       vt.name AS voucher_type_name_at_revision,
                       vt.system_type_code AS voucher_type_code_at_revision,
                       p.name AS party_ledger_name_at_revision,
                       osi.name AS opening_stock_item_name_at_revision,
                       mjo.voucher_number AS master_job_order_number_at_revision
                FROM vouchers v
                JOIN companies c ON c.id = v.company_id
                JOIN financial_years fy ON fy.id = v.financial_year_id
                JOIN voucher_types vt ON vt.id = v.voucher_type_id
                LEFT JOIN ledgers p ON p.id = v.party_ledger_id
                LEFT JOIN stock_items osi ON osi.id = v.opening_stock_item_id
                LEFT JOIN vouchers mjo ON mjo.id = v.master_job_order_id
                WHERE v.id = @voucherId
            ) q
            """),
        new("job_work_order_finished_goods", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision,
                       u.short_name AS uqc_at_revision,
                       c.name AS colour_name_at_revision,
                       fg.name AS finished_goods_godown_name_at_revision,
                       dg.name AS destination_godown_name_at_revision
                FROM job_work_order_finished_goods x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = si.uqc_id
                LEFT JOIN colours c ON c.id = x.colour_id
                LEFT JOIN godowns fg ON fg.id = x.finished_goods_godown_id
                LEFT JOIN godowns dg ON dg.id = x.destination_godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("job_work_order_size_allocations", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.finished_good_id, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, v.variant_key AS variant_key_at_revision,
                       s.name AS size_name_at_revision, c.name AS colour_name_at_revision
                FROM job_work_order_size_allocations x
                JOIN job_work_order_finished_goods jwo ON jwo.id = x.finished_good_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                LEFT JOIN sizes s ON s.id = x.size_id
                LEFT JOIN colours c ON c.id = v.colour_id
                WHERE jwo.voucher_id = @voucherId
            ) q
            """),
        new("job_work_order_components", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.finished_good_id, q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision,
                       u.short_name AS uqc_at_revision,
                       g.name AS component_at_name_at_revision,
                       v.variant_key AS variant_key_at_revision,
                       bs.stage_name AS bom_stage_name_at_revision,
                       cs.stage_name AS child_bom_stage_name_at_revision
                FROM job_work_order_components x
                JOIN job_work_order_finished_goods jwo ON jwo.id = x.finished_good_id
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = x.uqc_id
                LEFT JOIN godowns g ON g.id = x.component_godown_id
                LEFT JOIN stock_item_variants v ON v.id = x.component_variant_id
                LEFT JOIN job_work_order_bom_stages bs ON bs.id = x.bom_stage_id
                LEFT JOIN job_work_order_bom_stages cs ON cs.id = x.child_bom_stage_id
                WHERE jwo.voucher_id = @voucherId
            ) q
            """),
        new("job_work_order_processes", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.finished_good_id, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, p.name AS process_name_at_revision
                FROM job_work_order_processes x
                JOIN job_work_order_finished_goods jwo ON jwo.id = x.finished_good_id
                JOIN processes p ON p.id = x.process_id
                WHERE jwo.voucher_id = @voucherId
            ) q
            """),
        new("job_work_order_bom_stages", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.stage_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, b.name AS source_bom_name_at_revision,
                       br.revision_number AS source_bom_revision_number_at_revision,
                       si.name AS output_stock_item_name_at_revision,
                       v.variant_key AS output_variant_key_at_revision,
                       u.short_name AS output_uqc_at_revision,
                       p.name AS process_name_at_revision,
                       l.name AS assigned_job_worker_name_at_revision,
                       g.name AS output_godown_name_at_revision
                FROM job_work_order_bom_stages x
                LEFT JOIN bill_of_materials b ON b.id = x.source_bom_id
                LEFT JOIN bill_of_material_revisions br ON br.id = x.source_bom_revision_id
                JOIN stock_items si ON si.id = x.output_stock_item_id
                LEFT JOIN stock_item_variants v ON v.id = x.output_variant_id
                JOIN uqcs u ON u.id = x.output_uqc_id
                LEFT JOIN processes p ON p.id = x.process_id
                LEFT JOIN ledgers l ON l.id = x.assigned_job_worker_id
                LEFT JOIN godowns g ON g.id = x.output_godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("job_work_order_stage_assignments", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.bom_stage_id, q.assignment_version, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, l.name AS job_worker_name_at_revision, s.stage_name AS stage_name_at_revision
                FROM job_work_order_stage_assignments x
                JOIN job_work_order_bom_stages s ON s.id = x.bom_stage_id
                LEFT JOIN ledgers l ON l.id = x.job_worker_id
                WHERE s.voucher_id = @voucherId
            ) q
            """),
        new("master_job_order_finished_goods", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision, u.short_name AS uqc_at_revision
                FROM master_job_order_finished_goods x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = si.uqc_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("master_job_order_allocations", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.finished_good_id, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, v.variant_key AS variant_key_at_revision,
                       c.name AS colour_name_at_revision, s.name AS size_name_at_revision
                FROM master_job_order_allocations x
                JOIN master_job_order_finished_goods mjo ON mjo.id = x.finished_good_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                LEFT JOIN colours c ON c.id = x.colour_id
                LEFT JOIN sizes s ON s.id = x.size_id
                WHERE mjo.voucher_id = @voucherId
            ) q
            """),
        new("material_out_details", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.voucher_id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, g.name AS destination_godown_name_at_revision
                FROM material_out_details x
                JOIN godowns g ON g.id = x.destination_godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("material_out_lines", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision, u.short_name AS uqc_at_revision,
                       sg.name AS source_godown_name_at_revision,
                       dg.name AS destination_godown_name_at_revision,
                       bs.stage_name AS bom_stage_name_at_revision,
                       jw.name AS assigned_job_worker_name_at_revision
                FROM material_out_lines x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns sg ON sg.id = x.source_godown_id
                JOIN godowns dg ON dg.id = x.destination_godown_id
                LEFT JOIN job_work_order_bom_stages bs ON bs.id = x.bom_stage_id
                LEFT JOIN job_work_order_stage_assignments a ON a.id = x.stage_assignment_id
                LEFT JOIN ledgers jw ON jw.id = a.job_worker_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("material_in_details", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.voucher_id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, cg.name AS consumption_godown_name_at_revision,
                       rg.name AS receiving_godown_name_at_revision
                FROM material_in_details x
                JOIN godowns cg ON cg.id = x.consumption_godown_id
                JOIN godowns rg ON rg.id = x.receiving_godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("material_in_finished_goods", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision, u.short_name AS uqc_at_revision,
                       g.name AS receiving_godown_name_at_revision,
                       bs.stage_name AS bom_stage_name_at_revision,
                       jw.name AS assigned_job_worker_name_at_revision
                FROM material_in_finished_goods x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns g ON g.id = x.receiving_godown_id
                LEFT JOIN job_work_order_bom_stages bs ON bs.id = x.bom_stage_id
                LEFT JOIN job_work_order_stage_assignments a ON a.id = x.stage_assignment_id
                LEFT JOIN ledgers jw ON jw.id = a.job_worker_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("material_in_fg_allocations", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.finished_good_line_id, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, v.variant_key AS variant_key_at_revision,
                       c.name AS colour_name_at_revision, s.name AS size_name_at_revision
                FROM material_in_fg_allocations x
                JOIN material_in_finished_goods mi ON mi.id = x.finished_good_line_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                LEFT JOIN colours c ON c.id = v.colour_id
                LEFT JOIN sizes s ON s.id = v.size_id
                WHERE mi.voucher_id = @voucherId
            ) q
            """),
        new("material_in_consumptions", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision, u.short_name AS uqc_at_revision,
                       g.name AS consumption_godown_name_at_revision,
                       bs.stage_name AS bom_stage_name_at_revision,
                       jw.name AS assigned_job_worker_name_at_revision
                FROM material_in_consumptions x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns g ON g.id = x.consumption_godown_id
                LEFT JOIN job_work_order_bom_stages bs ON bs.id = x.bom_stage_id
                LEFT JOIN job_work_order_stage_assignments a ON a.id = x.stage_assignment_id
                LEFT JOIN ledgers jw ON jw.id = a.job_worker_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("material_in_mo_allocations", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.consumption_line_id, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, mov.voucher_id AS material_out_voucher_id_at_revision,
                       v.voucher_number AS material_out_voucher_number_at_revision
                FROM material_in_mo_allocations x
                JOIN material_in_consumptions mic ON mic.id = x.consumption_line_id
                JOIN material_out_lines mov ON mov.id = x.material_out_line_id
                JOIN vouchers v ON v.id = mov.voucher_id
                WHERE mic.voucher_id = @voucherId
            ) q
            """),
        new("inventory_inward_lines", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision,
                       v.variant_key AS variant_key_at_revision,
                       u.short_name AS uqc_at_revision,
                       g.name AS godown_name_at_revision
                FROM inventory_inward_lines x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns g ON g.id = x.godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("stock_movements", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision, u.short_name AS uqc_at_revision,
                       g.name AS godown_name_at_revision
                FROM stock_movements x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns g ON g.id = x.godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("purchase_order_lines", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision,
                       v.variant_key AS variant_key_at_revision,
                       u.short_name AS uqc_at_revision,
                       g.name AS godown_name_at_revision
                FROM purchase_order_lines x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                JOIN uqcs u ON u.id = x.uqc_id
                LEFT JOIN godowns g ON g.id = x.godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("purchase_return_lines", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.line_number, q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, si.name AS stock_item_name_at_revision,
                       v.variant_key AS variant_key_at_revision,
                       u.short_name AS uqc_at_revision,
                       g.name AS godown_name_at_revision
                FROM purchase_return_lines x
                JOIN stock_items si ON si.id = x.stock_item_id
                JOIN stock_item_variants v ON v.id = x.stock_item_variant_id
                JOIN uqcs u ON u.id = x.uqc_id
                JOIN godowns g ON g.id = x.godown_id
                WHERE x.voucher_id = @voucherId
            ) q
            """),
        new("voucher_links", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*,
                       sv.voucher_number AS source_voucher_number_at_revision,
                       svt.name AS source_voucher_type_at_revision,
                       tv.voucher_number AS target_voucher_number_at_revision,
                       tvt.name AS target_voucher_type_at_revision
                FROM voucher_links x
                JOIN vouchers sv ON sv.id = x.source_voucher_id
                JOIN voucher_types svt ON svt.id = sv.voucher_type_id
                JOIN vouchers tv ON tv.id = x.target_voucher_id
                JOIN voucher_types tvt ON tvt.id = tv.voucher_type_id
                WHERE x.source_voucher_id = @voucherId OR x.target_voucher_id = @voucherId
            ) q
            """),
        new("tally_sync_records", """
            SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q.id), '[]'::jsonb)::text
            FROM (
                SELECT x.*, c.tally_company_name AS tally_company_name_at_revision
                FROM tally_sync_records x
                JOIN tally_company_links c ON c.id = x.tally_company_link_id
                WHERE x.voucher_id = @voucherId
            ) q
            """)
    ];

    public async Task<VoucherAuditRevision> RecordAsync(
        TexTrackDbContext db,
        long voucherId,
        string action,
        string reason,
        string actor,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Voucher audit history must be written inside the voucher transaction.");

        var auditTime = NormalizeAuditTime(recordedAtUtc);

        var voucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.FinancialYear)
            .Include(x => x.VoucherType)
            .SingleOrDefaultAsync(x => x.Id == voucherId, cancellationToken)
            ?? throw new InvalidOperationException("Cannot record history because the live voucher was not found.");

        await AcquireVoucherAuditLockAsync(db, voucher.AuditIdentity, cancellationToken);

        var previous = await db.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.VoucherAuditIdentity == voucher.AuditIdentity)
            .OrderByDescending(x => x.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var revisionNumber = (previous?.RevisionNumber ?? 0) + 1;
        var snapshot = await BuildSnapshotAsync(db, voucherId, auditTime, cancellationToken);
        var snapshotJson = snapshot.ToJsonString(CompactJson);
        var contentHash = Hash(snapshotJson);
        var changesJson = BuildChanges(previous?.SnapshotJson, snapshot, action).ToJsonString(CompactJson);
        var previousChainHash = previous?.ChainHash ?? string.Empty;
        var chainHash = ComputeChainHash(previousChainHash, voucher.AuditIdentity,
            revisionNumber, action.Trim(), contentHash, auditTime, actor.Trim());

        var revision = new VoucherAuditRevision
        {
            VoucherAuditIdentity = voucher.AuditIdentity,
            VoucherId = voucher.Id,
            CompanyId = voucher.CompanyId,
            CompanyName = voucher.Company.Name,
            FinancialYearId = voucher.FinancialYearId,
            FinancialYearName = voucher.FinancialYear.Name,
            VoucherTypeId = voucher.VoucherTypeId,
            VoucherTypeName = voucher.VoucherType.Name,
            VoucherTypeCode = voucher.VoucherType.SystemTypeCode,
            VoucherNumber = voucher.VoucherNumber,
            RevisionNumber = revisionNumber,
            SnapshotSchemaVersion = CurrentSnapshotSchemaVersion,
            Action = action.Trim(),
            Reason = (reason ?? string.Empty).Trim(),
            SnapshotJson = snapshotJson,
            ChangesJson = changesJson,
            ContentHash = contentHash,
            PreviousChainHash = previousChainHash,
            ChainHash = chainHash,
            RecordedAtUtc = auditTime,
            RecordedBy = actor.Trim()
        };
        db.VoucherAuditRevisions.Add(revision);
        await db.SaveChangesAsync(cancellationToken);
        return revision;
    }

    public static bool VerifyRevision(
        VoucherAuditRevision revision,
        int expectedRevisionNumber,
        string expectedPreviousChainHash)
    {
        if (revision.RevisionNumber != expectedRevisionNumber ||
            !string.Equals(revision.PreviousChainHash, expectedPreviousChainHash, StringComparison.Ordinal))
            return false;
        var contentHash = Hash(revision.SnapshotJson);
        if (!string.Equals(revision.ContentHash, contentHash, StringComparison.Ordinal)) return false;
        var chainHash = ComputeChainHash(revision.PreviousChainHash,
            revision.VoucherAuditIdentity, revision.RevisionNumber, revision.Action,
            revision.ContentHash, NormalizeAuditTime(revision.RecordedAtUtc), revision.RecordedBy);
        return string.Equals(revision.ChainHash, chainHash, StringComparison.Ordinal);
    }

    public async Task EnsureLegacyBaselinesAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var ids = await db.Vouchers.AsNoTracking()
                .Where(v => !db.VoucherAuditRevisions.Any(r => r.VoucherAuditIdentity == v.AuditIdentity))
                .OrderBy(v => v.Id)
                .Select(v => v.Id)
                .Take(25)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) return;

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            foreach (var id in ids)
                await RecordAsync(db, id, VoucherAuditActions.Baseline, LegacyBaselineReason,
                    "system:TexTrack audit-baseline", DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task<JsonObject> BuildSnapshotAsync(
        TexTrackDbContext db,
        long voucherId,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var tables = new JsonObject();
        foreach (var table in SnapshotTables)
            tables[table.Name] = await QueryRowsAsync(db, table.Sql, voucherId, cancellationToken);

        return new JsonObject
        {
            ["schemaVersion"] = CurrentSnapshotSchemaVersion,
            ["capturedAtUtc"] = capturedAtUtc.ToUniversalTime().ToString("O"),
            ["tables"] = tables
        };
    }

    private static async Task<JsonArray> QueryRowsAsync(
        TexTrackDbContext db,
        string sql,
        long voucherId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "voucherId";
        parameter.Value = voucherId;
        command.Parameters.Add(parameter);
        var json = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? "[]";
        return JsonNode.Parse(json)?.AsArray() ?? new JsonArray();
    }

    private static async Task AcquireVoucherAuditLockAsync(
        TexTrackDbContext db,
        Guid auditIdentity,
        CancellationToken cancellationToken)
    {
        var bytes = SHA256.HashData(auditIdentity.ToByteArray());
        var key = BitConverter.ToInt64(bytes, 0);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonObject BuildChanges(string? previousSnapshotJson, JsonObject current, string action)
    {
        if (string.IsNullOrWhiteSpace(previousSnapshotJson))
            return new JsonObject
            {
                ["kind"] = action == VoucherAuditActions.Baseline ? "legacy-baseline" : "initial",
                ["tables"] = new JsonObject()
            };

        var previous = JsonNode.Parse(previousSnapshotJson)?.AsObject();
        var previousTables = previous?["tables"]?.AsObject();
        var currentTables = current["tables"]!.AsObject();
        var tableChanges = new JsonObject();
        var addedCount = 0;
        var removedCount = 0;
        var changedCount = 0;

        foreach (var (tableName, currentNode) in currentTables)
        {
            var beforeRows = IndexRows(previousTables?[tableName] as JsonArray);
            var afterRows = IndexRows(currentNode as JsonArray);
            var added = new JsonArray();
            var removed = new JsonArray();
            var changed = new JsonArray();

            foreach (var (key, row) in afterRows)
            {
                if (!beforeRows.TryGetValue(key, out var oldRow))
                {
                    added.Add(row.DeepClone());
                    addedCount++;
                    continue;
                }
                if (JsonNode.DeepEquals(oldRow, row)) continue;
                var fields = CompareFields(oldRow, row);
                changed.Add(new JsonObject { ["rowKey"] = key, ["fields"] = fields });
                changedCount++;
            }
            foreach (var (key, row) in beforeRows.Where(x => !afterRows.ContainsKey(x.Key)))
            {
                removed.Add(row.DeepClone());
                removedCount++;
            }

            if (added.Count > 0 || removed.Count > 0 || changed.Count > 0)
                tableChanges[tableName] = new JsonObject
                {
                    ["added"] = added,
                    ["removed"] = removed,
                    ["changed"] = changed
                };
        }

        return new JsonObject
        {
            ["kind"] = "comparison",
            ["summary"] = new JsonObject
            {
                ["addedRows"] = addedCount,
                ["removedRows"] = removedCount,
                ["changedRows"] = changedCount,
                ["lifecycleAction"] = action
            },
            ["tables"] = tableChanges
        };
    }

    private static Dictionary<string, JsonObject> IndexRows(JsonArray? rows)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (rows is null) return result;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is not JsonObject row) continue;
            var key = RowKey(row, index);
            while (!result.TryAdd(key, row)) key += "#";
        }
        return result;
    }

    private static string RowKey(JsonObject row, int index)
    {
        foreach (var name in new[] { "id", "voucher_id", "stable_key" })
            if (row[name] is JsonNode value) return $"{name}:{value.ToJsonString()}";
        return $"position:{index}";
    }

    private static JsonObject CompareFields(JsonObject before, JsonObject after)
    {
        var result = new JsonObject();
        foreach (var name in before.Select(x => x.Key).Union(after.Select(x => x.Key), StringComparer.Ordinal).Order())
        {
            before.TryGetPropertyValue(name, out var oldValue);
            after.TryGetPropertyValue(name, out var newValue);
            if (JsonNode.DeepEquals(oldValue, newValue)) continue;
            result[name] = new JsonObject
            {
                ["before"] = oldValue?.DeepClone(),
                ["after"] = newValue?.DeepClone()
            };
        }
        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ComputeChainHash(
        string previousChainHash,
        Guid auditIdentity,
        int revisionNumber,
        string action,
        string contentHash,
        DateTimeOffset recordedAtUtc,
        string actor) => Hash(string.Join("|",
            previousChainHash,
            auditIdentity.ToString("N"),
            revisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            action,
            contentHash,
            recordedAtUtc.ToUniversalTime().ToString("O"),
            actor));

    private static DateTimeOffset NormalizeAuditTime(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    private sealed record SnapshotTable(string Name, string Sql);
}
