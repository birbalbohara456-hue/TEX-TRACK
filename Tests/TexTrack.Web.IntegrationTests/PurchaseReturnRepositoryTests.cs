using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class PurchaseReturnRepositoryTests
{
    private static readonly DateOnly PostingDate = new(2026, 8, 15);

    [Fact]
    public async Task Save_posts_a_negative_outward_movement_for_the_exact_variant_and_godown()
    {
        await using var environment = await CreateSeededAsync();

        var saved = await environment.PurchaseReturnRepository.SaveAsync(NewRequest(quantity: 12.5m, rate: 7.3333m));

        await using var db = environment.CreateDbContext();
        var line = await db.PurchaseReturnLines.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        var movement = await db.StockMovements.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Equal(91.6663m, line.Amount);
        Assert.Equal(StockMovementSemantics.PurchaseReturnOutward, movement.MovementKind);
        Assert.Equal(-12.5m, movement.QuantityChange);
        Assert.Equal(-91.6663m, movement.ValueChange);
        Assert.Equal(line.Id, movement.PurchaseReturnLineId);
    }

    [Fact]
    public async Task Save_succeeds_without_any_supplier_or_bill_reference()
    {
        await using var environment = await CreateSeededAsync();
        var request = NewRequest(quantity: 5, rate: 10);
        request.SupplierLedgerId = null;
        request.ReferenceNumber = string.Empty;

        var saved = await environment.PurchaseReturnRepository.SaveAsync(request);

        await using var db = environment.CreateDbContext();
        var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == saved.VoucherId);
        Assert.Null(voucher.PartyLedgerId);
    }

    [Fact]
    public async Task Save_is_not_capped_by_any_prior_purchase_history_for_the_same_item()
    {
        // Standalone by design: a Purchase Return can exceed what was ever actually
        // purchased for this item - no linkage, no cap, per the explicit product decision.
        await using var environment = await CreateSeededAsync();

        var saved = await environment.PurchaseReturnRepository.SaveAsync(NewRequest(quantity: 999_999m, rate: 1));

        Assert.NotEqual(0, saved.VoucherId);
    }

    [Fact]
    public async Task Save_is_rolled_back_when_company_policy_blocks_the_resulting_negative_position()
    {
        await using var environment = await CreateSeededAsync();
        await using (var policyDb = environment.CreateDbContext())
            await policyDb.Companies.Where(x => x.Id == 1)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.AllowNegativeStock, false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.PurchaseReturnRepository.SaveAsync(NewRequest(quantity: 1, rate: 1)));

        Assert.Contains("Negative stock is blocked", error.Message, StringComparison.Ordinal);
        await using var verify = environment.CreateDbContext();
        Assert.False(await verify.PurchaseReturnLines.AnyAsync());
        Assert.False(await verify.StockMovements.AnyAsync());
    }

    [Fact]
    public async Task Cancellation_reverses_the_exact_movement_and_keeps_audit_history()
    {
        await using var environment = await CreateSeededAsync();
        var saved = await environment.PurchaseReturnRepository.SaveAsync(NewRequest(quantity: 25, rate: 4));

        var result = await environment.PurchaseReturnRepository.CancelAsync(saved.VoucherId, "Returned by mistake");

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == saved.VoucherId);
        var movements = await db.StockMovements.AsNoTracking()
            .Where(x => x.VoucherId == saved.VoucherId).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(VoucherLifecycleService.CancelledStatus, voucher.Status);
        Assert.Equal(2, movements.Count);
        Assert.Equal(0, movements.Sum(x => x.QuantityChange));
        Assert.Equal(0, movements.Sum(x => x.ValueChange));
        Assert.Equal(StockMovementSemantics.PurchaseReturnOutwardCancellation, movements[1].MovementKind);
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == saved.VoucherId));
    }

    [Fact]
    public async Task Delete_removes_live_stock_effect_but_retains_the_final_audit_snapshot()
    {
        await using var environment = await CreateSeededAsync();
        var saved = await environment.PurchaseReturnRepository.SaveAsync(NewRequest(quantity: 8, rate: 3));

        var result = await environment.PurchaseReturnRepository.DeleteAsync(saved.VoucherId);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.Vouchers.AnyAsync(x => x.Id == saved.VoucherId));
        Assert.False(await db.PurchaseReturnLines.AnyAsync(x => x.VoucherId == saved.VoucherId));
        Assert.False(await db.StockMovements.AnyAsync(x => x.VoucherId == saved.VoucherId));
        Assert.True(await db.VoucherAuditRevisions.AnyAsync(x =>
            x.VoucherId == saved.VoucherId && x.Action == VoucherAuditActions.Delete));
    }

    private static PurchaseReturnSaveRequest NewRequest(decimal quantity, decimal rate) => new()
    {
        VoucherTypeId = 5,
        VoucherDate = PostingDate,
        ReferenceNumber = "BILL-REF-1",
        SupplierLedgerId = 10,
        Narration = "Purchase Return repository test",
        Lines =
        [
            new PurchaseReturnLineInput
            {
                StockItemId = 1,
                StockItemVariantId = 1,
                UqcId = 1,
                GodownId = 1,
                Quantity = quantity,
                Rate = rate
            }
        ]
    };

    private static async Task<PostgreSqlTestEnvironment> CreateSeededAsync()
    {
        var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        await using var db = environment.CreateDbContext();
        db.LedgerGroups.Add(new LedgerGroup
        {
            Id = 10, CompanyId = 1, Name = "Sundry Creditors", NameNormalized = "SUNDRY CREDITORS",
            RootClassification = "SundryCreditors", IsActive = true,
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.Ledgers.Add(new Ledger
        {
            Id = 10, CompanyId = 1, LedgerGroupId = 10,
            Name = "Test Supplier", NameNormalized = "TEST SUPPLIER", IsActive = true,
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.VoucherTypes.Add(new VoucherType
        {
            Id = 5, CompanyId = 1, Name = "Purchase Return", NameNormalized = "PURCHASE RETURN",
            SystemTypeCode = "PURCHASE_RETURN", Nature = "Accounting + Inventory", PostingMode = "Inventory Outward",
            Abbreviation = "PR", NumberingMode = "Auto", StartingNumber = 1, ResetPeriod = "FinancialYear",
            IsSystem = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('vouchers', 'id'), (SELECT MAX(id) FROM vouchers), true)");
        return environment;
    }
}
