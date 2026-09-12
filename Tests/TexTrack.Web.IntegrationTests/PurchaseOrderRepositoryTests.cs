using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class PurchaseOrderRepositoryTests
{
    private static readonly DateOnly PostingDate = new(2026, 8, 15);

    [Fact]
    public async Task Save_creates_no_stock_movement_or_financial_effect()
    {
        await using var environment = await CreateSeededAsync();

        var saved = await environment.PurchaseOrderRepository.SaveAsync(NewRequest(orderedQuantity: 100, rate: 9));

        await using var db = environment.CreateDbContext();
        Assert.False(await db.StockMovements.AnyAsync(x => x.VoucherId == saved.VoucherId));
        var line = await db.PurchaseOrderLines.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Equal(900m, line.Amount);
    }

    [Fact]
    public async Task Cancel_is_blocked_while_a_non_cancelled_purchase_links_to_it()
    {
        await using var environment = await CreateSeededAsync();
        var poSaved = await environment.PurchaseOrderRepository.SaveAsync(NewRequest(orderedQuantity: 100, rate: 9));
        var poLineId = await GetPoLineIdAsync(environment, poSaved.VoucherId);
        await environment.InventoryInwardRepository.SaveAsync(NewPurchaseRequest(poLineId, quantity: 40, rate: 9));

        var result = await environment.PurchaseOrderRepository.CancelAsync(poSaved.VoucherId, "No longer needed");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Cancel_succeeds_once_the_linked_purchase_is_itself_cancelled()
    {
        await using var environment = await CreateSeededAsync();
        var poSaved = await environment.PurchaseOrderRepository.SaveAsync(NewRequest(orderedQuantity: 100, rate: 9));
        var poLineId = await GetPoLineIdAsync(environment, poSaved.VoucherId);
        var purchaseSaved = await environment.InventoryInwardRepository.SaveAsync(
            NewPurchaseRequest(poLineId, quantity: 40, rate: 9));
        await environment.InventoryInwardRepository.CancelAsync(purchaseSaved.VoucherId, "Wrong entry");

        var result = await environment.PurchaseOrderRepository.CancelAsync(poSaved.VoucherId, "No longer needed");

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task Reducing_ordered_quantity_below_what_was_already_received_is_rejected()
    {
        await using var environment = await CreateSeededAsync();
        var poSaved = await environment.PurchaseOrderRepository.SaveAsync(NewRequest(orderedQuantity: 100, rate: 9));
        var poLineId = await GetPoLineIdAsync(environment, poSaved.VoucherId);
        await environment.InventoryInwardRepository.SaveAsync(NewPurchaseRequest(poLineId, quantity: 40, rate: 9));
        var edit = await environment.PurchaseOrderRepository.GetForEditAsync(poSaved.VoucherId);
        Assert.NotNull(edit);

        var request = NewRequest(orderedQuantity: 30, rate: 9);
        request.VoucherId = poSaved.VoucherId;
        request.ConcurrencyToken = edit.ConcurrencyToken;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.PurchaseOrderRepository.SaveAsync(request));
        Assert.Contains("already been received", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> GetPoLineIdAsync(PostgreSqlTestEnvironment environment, long poVoucherId)
    {
        await using var db = environment.CreateDbContext();
        return await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.VoucherId == poVoucherId)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static PurchaseOrderSaveRequest NewRequest(decimal orderedQuantity, decimal rate) => new()
    {
        VoucherTypeId = 4,
        VoucherDate = PostingDate,
        ReferenceNumber = "PO-TEST-CRUD",
        SupplierLedgerId = 10,
        Narration = "Purchase Order repository test",
        Lines =
        [
            new PurchaseOrderLineInput
            {
                StockItemId = 1,
                StockItemVariantId = 1,
                UqcId = 1,
                OrderedQuantity = orderedQuantity,
                Rate = rate
            }
        ]
    };

    private static InventoryInwardSaveRequest NewPurchaseRequest(long? poLineId, decimal quantity, decimal rate) => new()
    {
        Kind = InventoryInwardVoucherKind.Purchase,
        VoucherTypeId = 2,
        VoucherDate = PostingDate,
        ReferenceNumber = "SUP-INV-CRUD",
        SupplierLedgerId = 10,
        Narration = "Purchase Order repository test",
        Lines =
        [
            new InventoryInwardLineInput
            {
                StockItemId = 1,
                StockItemVariantId = 1,
                UqcId = 1,
                GodownId = 1,
                Quantity = quantity,
                Rate = rate,
                PurchaseOrderLineId = poLineId
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
        db.VoucherTypes.AddRange(
            new VoucherType
            {
                Id = 2, CompanyId = 1, Name = "Purchase", NameNormalized = "PURCHASE",
                SystemTypeCode = "PURCHASE", Nature = "Accounting + Inventory", PostingMode = "Inventory Inward",
                Abbreviation = "PUR", NumberingMode = "Auto", StartingNumber = 1, ResetPeriod = "FinancialYear",
                IsSystem = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            },
            new VoucherType
            {
                Id = 4, CompanyId = 1, Name = "Purchase Order", NameNormalized = "PURCHASE ORDER",
                SystemTypeCode = "PURCHASE_ORDER", Nature = "Planning", PostingMode = "No financial posting",
                Abbreviation = "PO", NumberingMode = "Auto", StartingNumber = 1, ResetPeriod = "FinancialYear",
                IsSystem = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('vouchers', 'id'), (SELECT MAX(id) FROM vouchers), true)");
        return environment;
    }
}
