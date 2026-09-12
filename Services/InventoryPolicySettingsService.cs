using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed record InventoryPolicyView(
    bool AllowNegativeStock,
    IReadOnlyList<NegativeStockPosition> ExistingNegativePositions);

public sealed class InventoryPolicySettingsService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    StockPostingService stockPosting)
{
    public async Task<InventoryPolicyView> GetAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.AdminSettings, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var allow = await db.Companies.AsNoTracking()
            .Where(x => x.Id == companyContext.CompanyId && x.IsActive)
            .Select(x => (bool?)x.AllowNegativeStock)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The active company is unavailable.");
        var exceptions = await stockPosting.GetNegativePositionsAsync(
            db, companyContext.CompanyId, 20, cancellationToken);
        return new InventoryPolicyView(allow, exceptions);
    }

    public async Task<OperationResult> UpdateAsync(
        bool allowNegativeStock,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.AdminSettings, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var actor = companyContext.Actor;

        try
        {
            // This one-time administrative operation must not race a stock
            // voucher that is being committed while reconciliation runs.
            await db.Database.ExecuteSqlRawAsync(
                "LOCK TABLE stock_movements IN SHARE MODE", cancellationToken);

            var company = await db.Companies.SingleOrDefaultAsync(
                x => x.Id == companyContext.CompanyId && x.IsActive,
                cancellationToken);
            if (company is null)
                return OperationResult.Fail("The active company is unavailable.");
            if (company.AllowNegativeStock == allowNegativeStock)
                return OperationResult.Ok("The inventory policy is already set to the selected mode.", company.Id);

            if (!allowNegativeStock)
            {
                var exceptions = await stockPosting.GetNegativePositionsAsync(
                    db, company.Id, int.MaxValue, cancellationToken);
                if (exceptions.Count > 0)
                {
                    var first = exceptions[0];
                    db.AuditLogs.Add(new AuditLog
                    {
                        CompanyId = company.Id,
                        EntityType = "CompanyInventoryPolicy",
                        EntityId = company.Id,
                        Action = "BlockRejected",
                        Success = false,
                        Description = $"Block Negative Stock was not enabled because reconciliation found {exceptions.Count} negative position(s). First exception: {first.StockItemName} / {first.GodownName}, short {first.ShortageQuantity:0.####} {first.UqcName} on {first.FirstNegativeDate:dd-MMM-yyyy}.",
                        PerformedBy = actor,
                        PerformedAtUtc = now
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return OperationResult.Fail(
                        $"Blocking was not enabled. Resolve the {exceptions.Count} historical negative stock position(s) shown below, then try again.");
                }
            }

            var previous = company.AllowNegativeStock ? "Allow Negative Stock" : "Block Negative Stock";
            var next = allowNegativeStock ? "Allow Negative Stock" : "Block Negative Stock";
            company.AllowNegativeStock = allowNegativeStock;
            company.ModifiedAtUtc = now;
            company.ModifiedBy = actor;
            company.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = company.Id,
                EntityType = "CompanyInventoryPolicy",
                EntityId = company.Id,
                Action = "Update",
                Success = true,
                Description = $"Inventory policy changed from {previous} to {next}.",
                PerformedBy = actor,
                PerformedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{next} is now active for {company.Name}.", company.Id);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
