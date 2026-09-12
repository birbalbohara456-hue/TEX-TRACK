using System.Data;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class VoucherAuditHistoryTests
{
    [Fact]
    public async Task Full_history_records_diffs_and_survives_live_voucher_deletion()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var service = new VoucherAuditHistoryService(environment.CreateFactory());
        long voucherId;
        Guid auditIdentity;

        await using (var db = environment.CreateDb())
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var voucher = await NewVoucherAsync(db, "Before edit");
            voucherId = voucher.Id;
            auditIdentity = voucher.AuditIdentity;
            await service.RecordAsync(db, voucher.Id, VoucherAuditActions.Create,
                "Initial save", "user:1:Audit Test", DateTimeOffset.UtcNow);
            await transaction.CommitAsync();
        }

        await using (var db = environment.CreateDb())
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var voucher = await db.Vouchers.SingleAsync(x => x.Id == voucherId);
            voucher.Narration = "After edit";
            voucher.ModifiedAtUtc = DateTimeOffset.UtcNow;
            voucher.ModifiedBy = "user:2:Editor";
            voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await service.RecordAsync(db, voucher.Id, VoucherAuditActions.Update,
                "Narration changed", voucher.ModifiedBy, voucher.ModifiedAtUtc);
            await transaction.CommitAsync();
        }

        await using (var db = environment.CreateDb())
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var voucher = await db.Vouchers.SingleAsync(x => x.Id == voucherId);
            await service.RecordAsync(db, voucher.Id, VoucherAuditActions.Delete,
                "Test deletion", "user:3:Deleter", DateTimeOffset.UtcNow);
            db.Vouchers.Remove(voucher);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = environment.CreateDb();
        Assert.False(await verify.Vouchers.AnyAsync(x => x.Id == voucherId));
        var revisions = await verify.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.VoucherAuditIdentity == auditIdentity)
            .OrderBy(x => x.RevisionNumber)
            .ToListAsync();
        Assert.Equal(3, revisions.Count);
        Assert.Equal([1, 2, 3], revisions.Select(x => x.RevisionNumber));
        Assert.Equal(VoucherAuditActions.Delete, revisions[^1].Action);
        Assert.Equal(revisions[0].ChainHash, revisions[1].PreviousChainHash);
        Assert.Equal(revisions[1].ChainHash, revisions[2].PreviousChainHash);
        var previousHash = string.Empty;
        for (var index = 0; index < revisions.Count; index++)
        {
            Assert.True(VoucherAuditHistoryService.VerifyRevision(revisions[index], index + 1, previousHash));
            previousHash = revisions[index].ChainHash;
        }
        revisions[1].SnapshotJson = revisions[1].SnapshotJson + " ";
        Assert.False(VoucherAuditHistoryService.VerifyRevision(
            revisions[1], 2, revisions[0].ChainHash));

        var changes = JsonNode.Parse(revisions[1].ChangesJson)!.AsObject();
        var fields = changes["tables"]!["voucher"]!["changed"]![0]!["fields"]!.AsObject();
        Assert.Equal("\"Before edit\"", fields["narration"]!["before"]!.ToJsonString());
        Assert.Equal("\"After edit\"", fields["narration"]!["after"]!.ToJsonString());
        Assert.Contains("company_name_at_revision", revisions[0].SnapshotJson, StringComparison.Ordinal);
        Assert.Contains("voucher_type_name_at_revision", revisions[0].SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_voucher_gets_honest_baseline_not_fabricated_creation_history()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        await using (var db = environment.CreateDb())
            _ = await NewVoucherAsync(db, "Legacy current state");

        var service = new VoucherAuditHistoryService(environment.CreateFactory());
        await service.EnsureLegacyBaselinesAsync();

        await using var verify = environment.CreateDb();
        var baseline = await verify.VoucherAuditRevisions.AsNoTracking().SingleAsync();
        Assert.Equal(VoucherAuditActions.Baseline, baseline.Action);
        Assert.Equal(VoucherAuditHistoryService.LegacyBaselineReason, baseline.Reason);
        Assert.Contains("legacy-baseline", baseline.ChangesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_write_requires_the_business_transaction()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        await using var db = environment.CreateDb();
        var voucher = await NewVoucherAsync(db, "No transaction");
        var service = new VoucherAuditHistoryService(environment.CreateFactory());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(
            db, voucher.Id, VoucherAuditActions.Update, "Invalid call", "user:1:Test", DateTimeOffset.UtcNow));
        Assert.Contains("inside the voucher transaction", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.VoucherAuditRevisions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Full_trail_requires_privileged_role_and_never_crosses_company_boundary()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        Guid companyOneIdentity;
        Guid companyTwoIdentity;
        await using (var db = environment.CreateDb())
        {
            var year = await db.FinancialYears.FirstAsync();
            var type = await db.VoucherTypes.FirstAsync();
            companyOneIdentity = Guid.NewGuid();
            companyTwoIdentity = Guid.NewGuid();
            db.VoucherAuditRevisions.AddRange(
                Revision(companyOneIdentity, 101, year.CompanyId, year.Id, type.Id, "Company 1"),
                Revision(companyTwoIdentity, 202, 2, year.Id, type.Id, "Company 2"));
            await db.SaveChangesAsync();
        }

        var manager = new VoucherAuditTrailRepository(
            environment.CreateFactory(),
            environment.CreateCompanyContext(SecurityRoleCodes.Manager, 1));
        var visible = await manager.GetListAsync(includeAllFinancialYears: true);
        Assert.Single(visible);
        Assert.Equal(companyOneIdentity, visible[0].VoucherAuditIdentity);
        Assert.Empty(await manager.GetHistoryAsync(companyTwoIdentity));

        var unprivilegedOperator = new VoucherAuditTrailRepository(
            environment.CreateFactory(),
            environment.CreateCompanyContext(SecurityRoleCodes.Operator, 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            unprivilegedOperator.GetListAsync(includeAllFinancialYears: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            unprivilegedOperator.GetHistoryAsync(companyOneIdentity));
    }

    [Fact]
    public async Task Live_stamp_uses_immutable_identity_when_numeric_id_has_old_history()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        long voucherId;
        Guid liveIdentity;
        await using (var db = environment.CreateDb())
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var voucher = await NewVoucherAsync(db, "Current voucher");
            voucherId = voucher.Id;
            liveIdentity = voucher.AuditIdentity;
            var service = new VoucherAuditHistoryService(environment.CreateFactory());
            await service.RecordAsync(db, voucher.Id, VoucherAuditActions.Create,
                "Current save", "user:2:Current Actor", DateTimeOffset.UtcNow);
            await transaction.CommitAsync();
        }

        await using (var db = environment.CreateDb())
        {
            var year = await db.FinancialYears.FirstAsync();
            var type = await db.VoucherTypes.FirstAsync();
            db.VoucherAuditRevisions.Add(Revision(
                Guid.NewGuid(), voucherId, year.CompanyId, year.Id, type.Id, "Old deleted actor"));
            await db.SaveChangesAsync();
        }

        var repository = new VoucherAuditTrailRepository(
            environment.CreateFactory(),
            environment.CreateCompanyContext(SecurityRoleCodes.Manager, 1));
        var stamp = await repository.GetStampAsync(voucherId);
        Assert.NotNull(stamp);
        Assert.Equal(liveIdentity, stamp.VoucherAuditIdentity);
        Assert.Equal("user:2:Current Actor", stamp.CreatedBy);
    }

    private static VoucherAuditRevision Revision(
        Guid identity,
        long voucherId,
        long companyId,
        long financialYearId,
        long voucherTypeId,
        string actor)
    {
        var hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        return new VoucherAuditRevision
        {
            VoucherAuditIdentity = identity,
            VoucherId = voucherId,
            CompanyId = companyId,
            CompanyName = $"Company {companyId}",
            FinancialYearId = financialYearId,
            FinancialYearName = "Test FY",
            VoucherTypeId = voucherTypeId,
            VoucherTypeName = "Test Voucher",
            VoucherTypeCode = "TEST",
            VoucherNumber = voucherId.ToString(),
            RevisionNumber = 1,
            SnapshotSchemaVersion = 1,
            Action = VoucherAuditActions.Create,
            Reason = "Fixture",
            SnapshotJson = "{}",
            ChangesJson = "{}",
            ContentHash = hash,
            PreviousChainHash = string.Empty,
            ChainHash = hash,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            RecordedBy = actor
        };
    }

    private static async Task<Voucher> NewVoucherAsync(TexTrack.Web.Data.TexTrackDbContext db, string narration)
    {
        var type = await db.VoucherTypes.FirstAsync();
        var year = await db.FinancialYears.FirstAsync();
        var next = (await db.Vouchers.MaxAsync(x => (int?)x.SequenceNumber) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var voucher = new Voucher
        {
            CompanyId = year.CompanyId,
            FinancialYearId = year.Id,
            VoucherTypeId = type.Id,
            SequenceNumber = next,
            VoucherNumber = $"AUDIT-{next}",
            VoucherNumberNormalized = $"AUDIT-{next}",
            VoucherDate = year.StartDate,
            Narration = narration,
            Status = "Open",
            CreatedAtUtc = now,
            ModifiedAtUtc = now,
            CreatedBy = "user:1:Audit Test",
            ModifiedBy = "user:1:Audit Test"
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();
        return voucher;
    }
}
