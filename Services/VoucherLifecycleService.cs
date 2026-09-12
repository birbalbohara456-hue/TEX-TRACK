using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;

namespace TexTrack.Web.Services;

public enum VoucherLinkScope
{
    Downstream,
    Any
}

public sealed class VoucherLifecycleService
{
    public const string OpenStatus = "Open";
    public const string CancelledStatus = "Cancelled";

    public static string? ValidateCancellationReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Cancellation reason is required.";
        return reason.Trim().Length > 500
            ? "Cancellation reason cannot exceed 500 characters."
            : null;
    }

    public async Task<bool> HasActiveLinksAsync(
        TexTrackDbContext db,
        long companyId,
        long voucherId,
        VoucherLinkScope scope,
        CancellationToken cancellationToken = default)
    {
        var links = db.VoucherLinks.AsNoTracking().Where(x => x.CompanyId == companyId);

        return scope == VoucherLinkScope.Downstream
            ? await links.AnyAsync(
                x => x.SourceVoucherId == voucherId && x.TargetVoucher.Status != CancelledStatus,
                cancellationToken)
            : await links.AnyAsync(
                x => (x.SourceVoucherId == voucherId && x.TargetVoucher.Status != CancelledStatus) ||
                     (x.TargetVoucherId == voucherId && x.SourceVoucher.Status != CancelledStatus),
                cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActiveLinkDescriptionsAsync(
        TexTrackDbContext db,
        long companyId,
        long voucherId,
        VoucherLinkScope scope,
        CancellationToken cancellationToken = default)
    {
        var links = db.VoucherLinks.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (scope == VoucherLinkScope.Downstream)
            links = links.Where(x => x.SourceVoucherId == voucherId && x.TargetVoucher.Status != CancelledStatus);
        else
            links = links.Where(x =>
                (x.SourceVoucherId == voucherId && x.TargetVoucher.Status != CancelledStatus) ||
                (x.TargetVoucherId == voucherId && x.SourceVoucher.Status != CancelledStatus));

        return await links.OrderBy(x => x.Id).Select(x =>
            x.SourceVoucherId == voucherId
                ? "downstream " + (string.IsNullOrEmpty(x.TargetVoucher.VoucherType.Name) ? "voucher" : x.TargetVoucher.VoucherType.Name) + " " + x.TargetVoucher.VoucherNumber
                : "upstream " + (string.IsNullOrEmpty(x.SourceVoucher.VoucherType.Name) ? "voucher" : x.SourceVoucher.VoucherType.Name) + " " + x.SourceVoucher.VoucherNumber)
            .Distinct().ToListAsync(cancellationToken);
    }

    public static string BuildBlockedMessage(string action, string subject, IReadOnlyCollection<string> links) =>
        links.Count == 0
            ? $"{subject} cannot be {action} because it has active linked vouchers."
            : $"{subject} cannot be {action}. Linked vouchers: {string.Join(", ", links)}.";

    public void MarkCancelled(
        Voucher voucher,
        string reason,
        string actor,
        DateTimeOffset occurredAtUtc)
    {
        if (voucher.Status == CancelledStatus)
            throw new InvalidOperationException("This voucher is already cancelled.");

        var validation = ValidateCancellationReason(reason);
        if (validation is not null) throw new InvalidOperationException(validation);

        voucher.Status = CancelledStatus;
        voucher.CancellationReason = reason.Trim();
        voucher.CancelledAtUtc = occurredAtUtc;
        voucher.CancelledBy = actor;
        voucher.ModifiedAtUtc = occurredAtUtc;
        voucher.ModifiedBy = actor;
        voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public static AuditLog NewAudit(
        long companyId,
        long voucherId,
        string action,
        bool success,
        string description,
        string actor,
        DateTimeOffset occurredAtUtc) => new()
    {
        CompanyId = companyId,
        EntityType = "Voucher",
        EntityId = voucherId,
        Action = action,
        Success = success,
        Description = description,
        PerformedBy = actor,
        PerformedAtUtc = occurredAtUtc
    };
}
