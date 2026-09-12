using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class VoucherAuditTrailRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<IReadOnlyList<VoucherAuditTrailRow>> GetListAsync(
        string? search = null,
        bool includeAllFinancialYears = false,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewAuditTrail, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId);
        if (!includeAllFinancialYears)
            query = query.Where(x => x.FinancialYearId == companyContext.FinancialYearId);

        var term = (search ?? string.Empty).Trim();
        if (term.Length > 0)
        {
            var matchingIdentities = query
                .Where(x => EF.Functions.ILike(x.VoucherNumber, $"%{term}%") ||
                            EF.Functions.ILike(x.VoucherTypeName, $"%{term}%") ||
                            EF.Functions.ILike(x.RecordedBy, $"%{term}%") ||
                            EF.Functions.ILike(x.Action, $"%{term}%") ||
                            EF.Functions.ILike(x.Reason, $"%{term}%"))
                .Select(x => x.VoucherAuditIdentity)
                .Distinct();
            query = query.Where(x => matchingIdentities.Contains(x.VoucherAuditIdentity));
        }

        var rows = await query
            .OrderByDescending(x => x.RecordedAtUtc)
            .Select(x => new
            {
                x.VoucherAuditIdentity, x.VoucherId, x.VoucherTypeName, x.VoucherTypeCode,
                x.VoucherNumber, x.FinancialYearName, x.RevisionNumber, x.Action,
                x.RecordedAtUtc, x.RecordedBy
            })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(x => x.VoucherAuditIdentity)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.RevisionNumber).ToList();
                var first = ordered[0];
                var last = ordered[^1];
                return new VoucherAuditTrailRow(
                    first.VoucherAuditIdentity, first.VoucherId, first.VoucherTypeName,
                    first.VoucherTypeCode, first.VoucherNumber, first.FinancialYearName,
                    ordered.Count, first.Action, first.RecordedAtUtc, first.RecordedBy,
                    last.Action, last.RecordedAtUtc, last.RecordedBy,
                    last.Action == VoucherAuditActions.Delete);
            })
            .OrderByDescending(x => x.LastRecordedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<VoucherAuditRevisionRow>> GetHistoryAsync(
        Guid voucherAuditIdentity,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewAuditTrail, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.VoucherAuditIdentity == voucherAuditIdentity)
            .OrderBy(x => x.RevisionNumber)
            .ToListAsync(cancellationToken);

        var rows = new List<VoucherAuditRevisionRow>(stored.Count);
        var expectedRevision = 1;
        var previousChainHash = string.Empty;
        var chainValid = true;
        foreach (var revision in stored)
        {
            chainValid = chainValid && VoucherAuditHistoryService.VerifyRevision(
                revision, expectedRevision, previousChainHash);
            rows.Add(new VoucherAuditRevisionRow(
                revision.Id, revision.VoucherAuditIdentity, revision.VoucherId,
                revision.RevisionNumber, revision.SnapshotSchemaVersion,
                revision.Action, revision.Reason, revision.RecordedAtUtc,
                revision.RecordedBy, revision.SnapshotJson, revision.ChangesJson,
                revision.ContentHash, revision.PreviousChainHash, revision.ChainHash,
                chainValid));
            expectedRevision++;
            previousChainHash = revision.ChainHash;
        }
        rows.Reverse();
        return rows;
    }

    public async Task<VoucherAuditStampModel?> GetStampAsync(
        long voucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var auditIdentity = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        x.Id == voucherId)
            .Select(x => x.AuditIdentity)
            .SingleOrDefaultAsync(cancellationToken);
        if (auditIdentity == Guid.Empty) return null;

        var revisions = await db.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.VoucherAuditIdentity == auditIdentity)
            .OrderBy(x => x.RevisionNumber)
            .Select(x => new { x.VoucherAuditIdentity, x.RecordedBy, x.RecordedAtUtc, x.Action })
            .ToListAsync(cancellationToken);
        if (revisions.Count == 0) return null;
        var first = revisions[0];
        var last = revisions[^1];
        return new VoucherAuditStampModel(first.VoucherAuditIdentity,
            first.RecordedBy, first.RecordedAtUtc, last.RecordedBy,
            last.RecordedAtUtc, last.Action, first.Action == VoucherAuditActions.Baseline);
    }
}
