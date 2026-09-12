using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>
/// Returns one exact persisted stock position. It never opens or scrapes a
/// report and never mixes item, variant, UQC, godown or company dimensions.
/// </summary>
public sealed class StockPositionService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<StockPositionSnapshot> GetAsync(
        StockPositionRequest request,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        ValidateRequest(request);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = companyContext.CompanyId;

        var itemExists = await db.StockItems.AsNoTracking()
            .AnyAsync(x => x.Id == request.StockItemId && x.CompanyId == companyId, cancellationToken);
        var uqcExists = await db.Uqcs.AsNoTracking()
            .AnyAsync(x => x.Id == request.UqcId && x.CompanyId == companyId, cancellationToken);
        var godownExists = await db.Godowns.AsNoTracking()
            .AnyAsync(x => x.Id == request.GodownId && x.CompanyId == companyId, cancellationToken);
        var variantExists = request.StockItemVariantId is null || await db.StockItemVariants.AsNoTracking()
            .AnyAsync(x => x.Id == request.StockItemVariantId.Value &&
                           x.CompanyId == companyId &&
                           x.StockItemId == request.StockItemId,
                cancellationToken);

        if (!itemExists || !uqcExists || !godownExists || !variantExists)
            throw new InvalidOperationException("The requested stock position contains an unavailable or mismatched company master.");

        var totals = await db.StockMovements.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.StockItemId == request.StockItemId &&
                        x.StockItemVariantId == request.StockItemVariantId &&
                        x.UqcId == request.UqcId &&
                        x.GodownId == request.GodownId &&
                        x.MovementDate <= request.AsOnDate &&
                        x.Voucher.Status != "Cancelled")
            .GroupBy(_ => 1)
            .Select(x => new { Quantity = x.Sum(y => y.QuantityChange), Value = x.Sum(y => y.ValueChange) })
            .SingleOrDefaultAsync(cancellationToken);

        return new StockPositionSnapshot(
            request.StockItemId,
            request.StockItemVariantId,
            request.UqcId,
            request.GodownId,
            request.AsOnDate,
            totals?.Quantity ?? 0,
            totals?.Value ?? 0);
    }

    private static void ValidateRequest(StockPositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StockItemId <= 0 || request.UqcId <= 0 || request.GodownId <= 0 || request.StockItemVariantId <= 0)
            throw new ArgumentException("Stock item, UQC and godown are required; a supplied variant must be valid.", nameof(request));
    }
}
