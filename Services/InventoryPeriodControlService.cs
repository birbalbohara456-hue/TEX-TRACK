using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;

namespace TexTrack.Web.Services;

/// <summary>
/// Enforces the company-owned, inclusive stock freeze boundary inside the same
/// database transaction as the protected voucher mutation.
/// </summary>
public sealed class InventoryPeriodControlService
{
    public async Task EnsurePostingDateIsOpenAsync(
        TexTrackDbContext db,
        long companyId,
        DateOnly postingDate,
        string operationDescription,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.AsNoTracking()
            .Where(x => x.Id == companyId && x.IsActive)
            .Select(x => new { x.StockFrozenThrough })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The active company context is unavailable; the stock period could not be verified.");
        var frozenThrough = company.StockFrozenThrough;

        if (frozenThrough is null || postingDate > frozenThrough.Value)
            return;

        var nextOpenDate = frozenThrough.Value.AddDays(1);
        throw new InvalidOperationException(
            $"{operationDescription} is dated {postingDate:dd-MMM-yyyy}, but stock is frozen through " +
            $"{frozenThrough.Value:dd-MMM-yyyy}. Use {nextOpenDate:dd-MMM-yyyy} or a later date, or have an authorised administrator reopen the stock period.");
    }

    public async Task EnsureVoucherMutationIsOpenAsync(
        TexTrackDbContext db,
        long companyId,
        DateOnly originalPostingDate,
        DateOnly proposedPostingDate,
        string voucherDescription,
        CancellationToken cancellationToken = default)
    {
        await EnsurePostingDateIsOpenAsync(
            db, companyId, originalPostingDate, $"The original {voucherDescription}", cancellationToken);

        if (proposedPostingDate != originalPostingDate)
            await EnsurePostingDateIsOpenAsync(
                db, companyId, proposedPostingDate, $"The amended {voucherDescription}", cancellationToken);
    }
}
