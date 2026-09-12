using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;

namespace TexTrack.Web.Services;

public sealed record StockMovementDraft(
    long CompanyId,
    long FinancialYearId,
    long VoucherId,
    DateOnly MovementDate,
    long StockItemId,
    long UqcId,
    long GodownId,
    decimal QuantityChange,
    decimal Rate,
    decimal ValueChange,
    string MovementKind,
    long? MaterialOutLineId = null,
    long? MaterialInFinishedGoodId = null,
    long? MaterialInConsumptionId = null,
    long? StockItemVariantId = null,
    long? InventoryInwardLineId = null,
    long? PurchaseReturnLineId = null);

public sealed record NegativeStockPosition(
    long StockItemId,
    long? StockItemVariantId,
    long UqcId,
    long GodownId,
    string StockItemName,
    string VariantName,
    string UqcName,
    string GodownName,
    DateOnly FirstNegativeDate,
    decimal ShortageQuantity);

public sealed class StockPostingService
{
    private readonly object touchedPositionsLock = new();
    private readonly Dictionary<TexTrackDbContext, HashSet<StockPositionKey>> touchedPositions =
        new(ReferenceEqualityComparer.Instance);

    public StockMovement Post(
        TexTrackDbContext db,
        StockMovementDraft draft,
        string actor,
        DateTimeOffset occurredAtUtc)
    {
        Validate(draft);
        Track(db, StockPositionKey.From(draft));

        var movement = new StockMovement
        {
            CompanyId = draft.CompanyId,
            FinancialYearId = draft.FinancialYearId,
            VoucherId = draft.VoucherId,
            MaterialOutLineId = draft.MaterialOutLineId,
            MaterialInFinishedGoodId = draft.MaterialInFinishedGoodId,
            MaterialInConsumptionId = draft.MaterialInConsumptionId,
            InventoryInwardLineId = draft.InventoryInwardLineId,
            PurchaseReturnLineId = draft.PurchaseReturnLineId,
            StockItemVariantId = draft.StockItemVariantId,
            MovementDate = draft.MovementDate,
            StockItemId = draft.StockItemId,
            UqcId = draft.UqcId,
            GodownId = draft.GodownId,
            QuantityChange = draft.QuantityChange,
            Rate = draft.Rate,
            ValueChange = draft.ValueChange,
            MovementKind = draft.MovementKind.Trim(),
            CreatedAtUtc = occurredAtUtc,
            CreatedBy = actor
        };

        db.StockMovements.Add(movement);
        return movement;
    }

    public void RemoveVoucherPostings(TexTrackDbContext db, long voucherId)
    {
        var movements = db.StockMovements.Where(x => x.VoucherId == voucherId).ToList();
        foreach (var movement in movements)
            Track(db, StockPositionKey.From(movement));
        db.StockMovements.RemoveRange(movements);
    }

    public async Task<int> ReverseVoucherPostingsAsync(
        TexTrackDbContext db,
        long voucherId,
        IReadOnlyDictionary<string, string>? reversalKinds,
        string defaultReversalKind,
        string actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var originals = await db.StockMovements.AsNoTracking()
            .Where(x => x.VoucherId == voucherId)
            .ToListAsync(cancellationToken);

        if (reversalKinds is not null)
            originals = originals.Where(x => reversalKinds.ContainsKey(x.MovementKind)).ToList();

        foreach (var original in originals)
        {
            var reversalKind = reversalKinds is not null
                ? reversalKinds[original.MovementKind]
                : defaultReversalKind;

            Post(db, new StockMovementDraft(
                    original.CompanyId,
                    original.FinancialYearId,
                    original.VoucherId,
                    original.MovementDate,
                    original.StockItemId,
                    original.UqcId,
                    original.GodownId,
                    -original.QuantityChange,
                    original.Rate,
                    -original.ValueChange,
                    reversalKind,
                    original.MaterialOutLineId,
                    original.MaterialInFinishedGoodId,
                    original.MaterialInConsumptionId,
                    original.StockItemVariantId,
                    original.InventoryInwardLineId,
                    original.PurchaseReturnLineId),
                actor,
                occurredAtUtc);
        }

        return originals.Count;
    }

    /// <summary>
    /// Flushes the final movement state inside the caller's transaction and
    /// verifies that every touched exact stock position remains non-negative
    /// on every effective date. The advisory locks serialize competing checks
    /// for the same positions; SERIALIZABLE callers retain PostgreSQL's
    /// additional write-skew protection.
    /// </summary>
    public async Task EnsureNegativeStockPolicyAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken = default)
    {
        var positions = TakeTouchedPositions(db);
        if (positions.Count == 0)
            return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock changes must be validated inside a database transaction.");

        // Save first so the validation sees the transaction's complete final
        // state, including replacements, cancellations and removals.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var position in positions.OrderBy(x => x.LockIdentity, StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({position.LockIdentity}, 0))",
                cancellationToken);

        var allowNegativeStock = await db.Companies.AsNoTracking()
            .Where(x => x.Id == companyId && x.IsActive)
            .Select(x => (bool?)x.AllowNegativeStock)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The active company context is unavailable; the negative-stock policy could not be verified.");

        if (allowNegativeStock)
            return;

        foreach (var position in positions)
        {
            if (position.CompanyId != companyId)
                throw new InvalidOperationException("A stock mutation attempted to cross the active company boundary.");

            var exception = await FindFirstNegativeAsync(db, position, cancellationToken);
            if (exception is not null)
                throw new InvalidOperationException(BuildBlockedMessage(exception));
        }
    }

    public async Task<IReadOnlyList<NegativeStockPosition>> GetNegativePositionsAsync(
        TexTrackDbContext db,
        long companyId,
        int maximumResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0) throw new ArgumentOutOfRangeException(nameof(companyId));
        if (maximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        return await QueryNegativePositionsAsync(
            db, companyId, position: null, maximumResults, cancellationToken);
    }

    private static async Task<NegativeStockPosition?> FindFirstNegativeAsync(
        TexTrackDbContext db,
        StockPositionKey position,
        CancellationToken cancellationToken)
    {
        return (await QueryNegativePositionsAsync(
            db, position.CompanyId, position, 1, cancellationToken)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<NegativeStockPosition>> QueryNegativePositionsAsync(
        TexTrackDbContext db,
        long companyId,
        StockPositionKey? position,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH daily AS
            (
                SELECT
                    sm.company_id,
                    sm.stock_item_id,
                    sm.stock_item_variant_id,
                    sm.uqc_id,
                    sm.godown_id,
                    sm.movement_date,
                    SUM(sm.quantity_change) AS daily_change
                FROM stock_movements AS sm
                INNER JOIN vouchers AS v ON v.id = sm.voucher_id
                WHERE sm.company_id = @company_id
                  AND v.status <> @cancelled_status
                  AND
                  (
                      @filter_position = FALSE
                      OR
                      (
                          sm.stock_item_id = @stock_item_id
                          AND sm.stock_item_variant_id IS NOT DISTINCT FROM @stock_item_variant_id
                          AND sm.uqc_id = @uqc_id
                          AND sm.godown_id = @godown_id
                      )
                  )
                GROUP BY
                    sm.company_id,
                    sm.stock_item_id,
                    sm.stock_item_variant_id,
                    sm.uqc_id,
                    sm.godown_id,
                    sm.movement_date
            ),
            running AS
            (
                SELECT
                    daily.*,
                    SUM(daily_change) OVER
                    (
                        PARTITION BY company_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id
                        ORDER BY movement_date
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                    ) AS running_quantity
                FROM daily
            ),
            first_negative AS
            (
                SELECT DISTINCT ON
                    (company_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id)
                    company_id,
                    stock_item_id,
                    stock_item_variant_id,
                    uqc_id,
                    godown_id,
                    movement_date,
                    -running_quantity AS shortage_quantity
                FROM running
                WHERE running_quantity < 0
                ORDER BY
                    company_id,
                    stock_item_id,
                    stock_item_variant_id,
                    uqc_id,
                    godown_id,
                    movement_date
            )
            SELECT
                negative.stock_item_id,
                negative.stock_item_variant_id,
                negative.uqc_id,
                negative.godown_id,
                item.name AS stock_item_name,
                COALESCE(variant.variant_key, 'Base / unspecified') AS variant_name,
                uqc.short_name AS uqc_name,
                godown.name AS godown_name,
                negative.movement_date AS first_negative_date,
                negative.shortage_quantity
            FROM first_negative AS negative
            INNER JOIN stock_items AS item ON item.id = negative.stock_item_id
            LEFT JOIN stock_item_variants AS variant ON variant.id = negative.stock_item_variant_id
            INNER JOIN uqcs AS uqc ON uqc.id = negative.uqc_id
            INNER JOIN godowns AS godown ON godown.id = negative.godown_id
            ORDER BY
                negative.movement_date,
                LOWER(item.name),
                LOWER(godown.name),
                negative.stock_item_id,
                negative.stock_item_variant_id NULLS FIRST,
                negative.uqc_id,
                negative.godown_id
            LIMIT @maximum_results
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            AddParameter(command, "company_id", DbType.Int64, companyId);
            AddParameter(command, "cancelled_status", DbType.String, VoucherLifecycleService.CancelledStatus);
            AddParameter(command, "filter_position", DbType.Boolean, position is not null);
            AddParameter(command, "stock_item_id", DbType.Int64, position?.StockItemId ?? 0);
            AddParameter(command, "stock_item_variant_id", DbType.Int64,
                position?.StockItemVariantId is long variantId ? variantId : DBNull.Value);
            AddParameter(command, "uqc_id", DbType.Int64, position?.UqcId ?? 0);
            AddParameter(command, "godown_id", DbType.Int64, position?.GodownId ?? 0);
            AddParameter(command, "maximum_results", DbType.Int32, maximumResults);

            var results = new List<NegativeStockPosition>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new NegativeStockPosition(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetFieldValue<DateOnly>(8),
                    reader.GetDecimal(9)));
            }
            return results;
        }
        finally
        {
            if (openedHere)
                await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string BuildBlockedMessage(NegativeStockPosition position) =>
        $"Negative stock is blocked. {position.StockItemName} ({position.VariantName}) would be short by " +
        $"{position.ShortageQuantity:0.####} {position.UqcName} in {position.GodownName} on " +
        $"{position.FirstNegativeDate:dd-MMM-yyyy}. Post or correct inward stock at that exact item, variant, UQC and godown before continuing.";

    private void Track(TexTrackDbContext db, StockPositionKey position)
    {
        lock (touchedPositionsLock)
        {
            if (!touchedPositions.TryGetValue(db, out var positions))
            {
                positions = [];
                touchedPositions.Add(db, positions);
            }
            positions.Add(position);
        }
    }

    private HashSet<StockPositionKey> TakeTouchedPositions(TexTrackDbContext db)
    {
        lock (touchedPositionsLock)
        {
            if (!touchedPositions.Remove(db, out var positions))
                return [];
            return positions;
        }
    }

    private static void Validate(StockMovementDraft draft)
    {
        if (draft.CompanyId <= 0 || draft.FinancialYearId <= 0 || draft.VoucherId <= 0)
            throw new InvalidOperationException("Stock posting requires a valid company, financial year and voucher.");
        if (draft.StockItemId <= 0 || draft.UqcId <= 0 || draft.GodownId <= 0)
            throw new InvalidOperationException("Stock posting requires a valid item, UQC and godown.");
        if (draft.QuantityChange == 0)
            throw new InvalidOperationException("Stock movement quantity must be non-zero. Value-only adjustments require a dedicated valuation transaction.");
        if (draft.Rate < 0)
            throw new InvalidOperationException("Stock movement rate cannot be negative.");
        if (string.IsNullOrWhiteSpace(draft.MovementKind) || draft.MovementKind.Trim().Length > 50)
            throw new InvalidOperationException("Stock movement kind is required and cannot exceed 50 characters.");
    }

    private sealed record StockPositionKey(
        long CompanyId,
        long StockItemId,
        long? StockItemVariantId,
        long UqcId,
        long GodownId)
    {
        public string LockIdentity =>
            $"negative-stock:{CompanyId}:{StockItemId}:{StockItemVariantId?.ToString() ?? "null"}:{UqcId}:{GodownId}";

        public static StockPositionKey From(StockMovementDraft draft) =>
            new(draft.CompanyId, draft.StockItemId, draft.StockItemVariantId, draft.UqcId, draft.GodownId);

        public static StockPositionKey From(StockMovement movement) =>
            new(movement.CompanyId, movement.StockItemId, movement.StockItemVariantId, movement.UqcId, movement.GodownId);
    }

}
