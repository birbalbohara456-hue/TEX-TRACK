using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class VoucherLifecycleAndStockPostingTests
{
    [Fact]
    public async Task Cancellation_reverses_each_posting_and_records_one_audit_atomically()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var posting = new StockPostingService();
        var lifecycle = new VoucherLifecycleService();
        var postedAt = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

        await using (var db = environment.CreateDbContext())
        {
            posting.Post(db, Draft(-10, -500, "MaterialOutSource", 1), "Test", postedAt);
            posting.Post(db, Draft(10, 500, "MaterialOutDestination", 2), "Test", postedAt);
            await db.SaveChangesAsync();
        }

        await using (var db = environment.CreateDbContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var voucher = await db.Vouchers.SingleAsync(x => x.Id == 1);
            var cancelledAt = postedAt.AddMinutes(5);
            var reversed = await posting.ReverseVoucherPostingsAsync(
                db,
                voucher.Id,
                new Dictionary<string, string>
                {
                    ["MaterialOutSource"] = "MaterialOutCancellationSource",
                    ["MaterialOutDestination"] = "MaterialOutCancellationDestination"
                },
                "Unused",
                "Test",
                cancelledAt);

            lifecycle.MarkCancelled(voucher, "Test cancellation", "Test", cancelledAt);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                voucher.CompanyId, voucher.Id, "Cancel", true, "Test cancellation", "Test", cancelledAt));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            Assert.Equal(2, reversed);
        }

        await using (var db = environment.CreateDbContext())
        {
            var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == 1);
            var movements = await db.StockMovements.AsNoTracking().Where(x => x.VoucherId == 1).ToListAsync();

            Assert.Equal(VoucherLifecycleService.CancelledStatus, voucher.Status);
            Assert.Equal("Test cancellation", voucher.CancellationReason);
            Assert.Equal("Test", voucher.CancelledBy);
            Assert.Equal(4, movements.Count);
            Assert.Equal(0, movements.Sum(x => x.QuantityChange));
            Assert.Equal(0, movements.Sum(x => x.ValueChange));
            Assert.Contains(movements, x => x.MovementKind == "MaterialOutCancellationSource" && x.QuantityChange == 10);
            Assert.Contains(movements, x => x.MovementKind == "MaterialOutCancellationDestination" && x.QuantityChange == -10);
            Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityType == "Voucher" && x.EntityId == 1 && x.Action == "Cancel"));
        }
    }

    [Fact]
    public void Lifecycle_rejects_blank_reason_without_mutating_voucher()
    {
        var voucher = new Voucher { Status = VoucherLifecycleService.OpenStatus };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new VoucherLifecycleService().MarkCancelled(voucher, " ", "Test", DateTimeOffset.UtcNow));

        Assert.Equal("Cancellation reason is required.", error.Message);
        Assert.Equal(VoucherLifecycleService.OpenStatus, voucher.Status);
        Assert.Null(voucher.CancelledAtUtc);
    }

    [Fact]
    public void Stock_posting_rejects_invalid_draft_before_tracking_anything()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new StockPostingService().Post(null!, Draft(1, 1, "MaterialOutSource", 1) with { Rate = -1 }, "Test", DateTimeOffset.UtcNow));

        Assert.Equal("Stock movement rate cannot be negative.", error.Message);
    }

    private static StockMovementDraft Draft(decimal quantity, decimal value, string kind, long godownId) => new(
        CompanyId: 1,
        FinancialYearId: 1,
        VoucherId: 1,
        MovementDate: new DateOnly(2026, 7, 23),
        StockItemId: 1,
        UqcId: 1,
        GodownId: godownId,
        QuantityChange: quantity,
        Rate: 50,
        ValueChange: value,
        MovementKind: kind);
}
