using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;

namespace TexTrack.Web.Services;

/// <summary>
/// Keeps the JWO lifecycle status derived from persisted production activity.
/// Dispatching every component is not completion; only final-output receipts can
/// complete the order.
/// </summary>
public static class JobWorkOrderStatusUpdater
{
    public static async Task<bool> UpdateAsync(
        TexTrackDbContext db,
        long jwoVoucherId,
        DateTimeOffset occurredAtUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var ordered = await db.JobWorkOrderFinishedGoods.AsNoTracking()
            .Where(x => x.VoucherId == jwoVoucherId)
            .SumAsync(x => (decimal?)x.OrderedQuantity, cancellationToken) ?? 0;

        var finalReceived = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => x.JwoFinishedGood.VoucherId == jwoVoucherId &&
                        x.Voucher.Status != "Cancelled" &&
                        (x.BomStageId == null || x.BomStage!.IsFinalStage))
            .SumAsync(x => (decimal?)x.ReceivedQuantity, cancellationToken) ?? 0;

        var hasDispatch = await db.MaterialOutLines.AsNoTracking()
            .AnyAsync(x => x.JwoVoucherId == jwoVoucherId &&
                           x.Voucher.Status != "Cancelled" &&
                           x.IssuedQuantity > 0,
                cancellationToken);

        var jwo = await db.Vouchers.SingleAsync(x => x.Id == jwoVoucherId, cancellationToken);
        var status = ordered > 0 && finalReceived >= ordered
            ? "Completed"
            : finalReceived > 0 || hasDispatch
                ? "PartiallyProcessed"
                : "Open";
        if (string.Equals(jwo.Status, status, StringComparison.Ordinal)) return false;
        jwo.Status = status;
        jwo.ModifiedAtUtc = occurredAtUtc;
        jwo.ModifiedBy = actor;
        jwo.ConcurrencyToken = Guid.NewGuid().ToString("N");
        return true;
    }
}
