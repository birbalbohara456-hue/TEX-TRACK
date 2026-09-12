using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class InventoryInwardRepositoryTests
{
    private static readonly DateOnly BooksBeginningDate = new(2026, 4, 1);
    private static readonly DateOnly PostingDate = new(2026, 8, 15);

    [Fact]
    public async Task Purchase_posts_accepted_cost_to_the_exact_variant_and_godown()
    {
        await using var environment = await CreateSeededAsync();

        var saved = await environment.InventoryInwardRepository.SaveAsync(NewRequest(
            InventoryInwardVoucherKind.Purchase, voucherTypeId: 2, supplierId: 10,
            quantity: 12.5m, rate: 7.3333m));

        await using var db = environment.CreateDbContext();
        var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == saved.VoucherId);
        var line = await db.InventoryInwardLines.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        var movement = await db.StockMovements.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Equal(10, voucher.PartyLedgerId);
        Assert.Equal("1", voucher.VoucherNumber);
        Assert.Equal(1, line.StockItemVariantId);
        Assert.Equal(91.6663m, line.Amount);
        Assert.Equal(StockMovementSemantics.PurchaseInward, movement.MovementKind);
        Assert.Equal(12.5m, movement.QuantityChange);
        Assert.Equal(91.6663m, movement.ValueChange);
        Assert.Equal(line.Id, movement.InventoryInwardLineId);
        Assert.Equal(1, movement.StockItemVariantId);
        Assert.Equal(1, movement.GodownId);
        Assert.Single(await db.VoucherAuditRevisions.AsNoTracking()
            .Where(x => x.VoucherId == saved.VoucherId && x.Action == VoucherAuditActions.Create)
            .ToListAsync());
    }

    [Fact]
    public async Task Opening_stock_posts_without_a_party_and_is_visible_to_stock_position()
    {
        await using var environment = await CreateSeededAsync();

        var saved = await environment.InventoryInwardRepository.SaveAsync(NewRequest(
            InventoryInwardVoucherKind.OpeningStock, voucherTypeId: 3,
            supplierId: null, quantity: 100, rate: 20));

        await using (var db = environment.CreateDbContext())
        {
            var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == saved.VoucherId);
            Assert.Null(voucher.PartyLedgerId);
            var movement = await db.StockMovements.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
            Assert.Equal(StockMovementSemantics.OpeningStockInward, movement.MovementKind);
            Assert.Equal(2_000m, movement.ValueChange);
        }

        var position = await environment.StockPositionService.GetAsync(new StockPositionRequest(
            StockItemId: 1, StockItemVariantId: 1, UqcId: 1, GodownId: 1,
            AsOnDate: PostingDate));
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(2_000m, position.Value);
    }

    [Fact]
    public async Task Alteration_replaces_old_posting_and_preserves_audited_revisions()
    {
        await using var environment = await CreateSeededAsync();
        var request = NewRequest(InventoryInwardVoucherKind.OpeningStock, 3, null, 10, 5);
        var saved = await environment.InventoryInwardRepository.SaveAsync(request);
        var edit = await environment.InventoryInwardRepository.GetForEditAsync(saved.VoucherId);
        Assert.NotNull(edit);

        request.VoucherId = saved.VoucherId;
        request.ConcurrencyToken = edit.ConcurrencyToken;
        request.Lines[0].Quantity = 8;
        request.Lines[0].Rate = 6;
        await environment.InventoryInwardRepository.SaveAsync(request);

        await using var db = environment.CreateDbContext();
        var movement = await db.StockMovements.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Equal(BooksBeginningDate, movement.MovementDate);
        Assert.Equal(8m, movement.QuantityChange);
        Assert.Equal(48m, movement.ValueChange);
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == saved.VoucherId));
    }

    [Fact]
    public async Task Cancellation_reverses_the_exact_stock_position_and_keeps_history()
    {
        await using var environment = await CreateSeededAsync();
        var saved = await environment.InventoryInwardRepository.SaveAsync(NewRequest(
            InventoryInwardVoucherKind.Purchase, 2, 10, 25, 4));

        var result = await environment.InventoryInwardRepository.CancelAsync(
            saved.VoucherId, "Supplier invoice reversed");

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == saved.VoucherId);
        var movements = await db.StockMovements.AsNoTracking()
            .Where(x => x.VoucherId == saved.VoucherId).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(VoucherLifecycleService.CancelledStatus, voucher.Status);
        Assert.Equal(2, movements.Count);
        Assert.Equal(0, movements.Sum(x => x.QuantityChange));
        Assert.Equal(0, movements.Sum(x => x.ValueChange));
        Assert.Equal(movements[0].StockItemVariantId, movements[1].StockItemVariantId);
        Assert.Equal(movements[0].InventoryInwardLineId, movements[1].InventoryInwardLineId);
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == saved.VoucherId));
    }

    [Fact]
    public async Task Delete_removes_live_stock_effect_but_retains_the_final_audit_snapshot()
    {
        await using var environment = await CreateSeededAsync();
        var saved = await environment.InventoryInwardRepository.SaveAsync(NewRequest(
            InventoryInwardVoucherKind.OpeningStock, 3, null, 25, 4));

        var result = await environment.InventoryInwardRepository.DeleteAsync(saved.VoucherId);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.Vouchers.AnyAsync(x => x.Id == saved.VoucherId));
        Assert.False(await db.InventoryInwardLines.AnyAsync(x => x.VoucherId == saved.VoucherId));
        Assert.False(await db.StockMovements.AnyAsync(x => x.VoucherId == saved.VoucherId));
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == saved.VoucherId));
        Assert.True(await db.VoucherAuditRevisions.AnyAsync(x =>
            x.VoucherId == saved.VoucherId && x.Action == VoucherAuditActions.Delete));
    }

    [Theory]
    [InlineData(2, 1, "does not belong")]
    [InlineData(1, 2, "must match")]
    public async Task Save_rejects_wrong_variant_or_uqc_identity(
        long variantId,
        long uqcId,
        string expectedMessage)
    {
        await using var environment = await CreateSeededAsync();
        var request = NewRequest(InventoryInwardVoucherKind.OpeningStock, 3, null, 10, 5);
        request.Lines[0].StockItemVariantId = variantId;
        request.Lines[0].UqcId = uqcId;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryInwardRepository.SaveAsync(request));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = environment.CreateDbContext();
        Assert.Empty(await db.InventoryInwardLines.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Shared_stock_period_gate_blocks_inward_creation_on_a_frozen_date()
    {
        await using var environment = await CreateSeededAsync();
        await using (var db = environment.CreateDbContext())
            await db.Companies.Where(x => x.Id == 1)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.StockFrozenThrough, PostingDate));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryInwardRepository.SaveAsync(NewRequest(
                InventoryInwardVoucherKind.OpeningStock, 3, null, 10, 5)));

        Assert.Contains("stock is frozen through", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Opening_stock_is_rejected_outside_the_books_beginning_date()
    {
        await using var environment = await CreateSeededAsync();
        var request = NewRequest(InventoryInwardVoucherKind.OpeningStock, 3, null, 10, 5);
        request.VoucherDate = PostingDate;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryInwardRepository.SaveAsync(request));

        Assert.Contains("books beginning date", error.Message, StringComparison.OrdinalIgnoreCase);
    }

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
        db.VoucherTypes.AddRange(
            NewType(2, "Purchase", "PURCHASE", "PUR", now),
            NewType(3, "Opening Stock", "OPENING_STOCK", "OPN", now));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('vouchers', 'id'), (SELECT MAX(id) FROM vouchers), true)");
        return environment;
    }

    private static VoucherType NewType(
        long id,
        string name,
        string systemType,
        string abbreviation,
        DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        Name = name,
        NameNormalized = name.ToUpperInvariant(),
        SystemTypeCode = systemType,
        Nature = "Inventory",
        PostingMode = "Inventory Inward",
        Abbreviation = abbreviation,
        NumberingMode = "Auto",
        StartingNumber = 1,
        ResetPeriod = "FinancialYear",
        IsSystem = true,
        IsActive = true,
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static InventoryInwardSaveRequest NewRequest(
        InventoryInwardVoucherKind kind,
        long voucherTypeId,
        long? supplierId,
        decimal quantity,
        decimal rate) => new()
    {
        Kind = kind,
        VoucherTypeId = voucherTypeId,
        VoucherDate = kind == InventoryInwardVoucherKind.OpeningStock ? BooksBeginningDate : PostingDate,
        OpeningStockItemId = kind == InventoryInwardVoucherKind.OpeningStock ? 1 : null,
        ReferenceNumber = kind == InventoryInwardVoucherKind.Purchase ? "SUP-INV-100" : "OPENING-2026",
        SupplierLedgerId = supplierId,
        Narration = "Native inward repository test",
        Lines =
        [
            new InventoryInwardLineInput
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
}
