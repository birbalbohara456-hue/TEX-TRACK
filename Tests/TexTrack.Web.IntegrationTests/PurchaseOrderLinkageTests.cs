using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

/// <summary>
/// Regression coverage for the PO&#8594;Purchase linkage: this is the exact area
/// where a real bug was found and fixed during live verification -
/// ValidateAndNormalizeLinesAsync was silently dropping PurchaseOrderLineId
/// when rebuilding line inputs, so no VoucherLink was ever created and the
/// pending-quantity check never actually validated anything. These tests
/// assert the fixed behavior end-to-end, not just that SaveAsync doesn't throw.
/// </summary>
public sealed class PurchaseOrderLinkageTests
{
    private static readonly DateOnly PostingDate = new(2026, 8, 15);

    [Fact]
    public async Task Purchase_against_a_po_line_persists_the_link_and_reduces_pending_quantity()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherId = await CreatePurchaseOrderAsync(environment, orderedQuantity: 100);
        var poLineId = await GetPoLineIdAsync(environment, poVoucherId);

        var saved = await environment.InventoryInwardRepository.SaveAsync(
            NewPurchaseRequest(poLineId, quantity: 40, rate: 10));

        await using var db = environment.CreateDbContext();
        var line = await db.InventoryInwardLines.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Equal(poLineId, line.PurchaseOrderLineId);
        var link = await db.VoucherLinks.AsNoTracking().SingleAsync(x => x.TargetVoucherId == saved.VoucherId);
        Assert.Equal(poVoucherId, link.SourceVoucherId);
        Assert.Equal(VoucherLinkTypes.PurchaseOrderToPurchase, link.LinkType);

        var pending = await environment.PurchaseOrderRepository.GetPendingLinesAsync();
        var pendingLine = Assert.Single(pending);
        Assert.Equal(60m, pendingLine.PendingQuantity);
    }

    [Fact]
    public async Task Purchase_exceeding_the_live_pending_quantity_is_rejected()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherId = await CreatePurchaseOrderAsync(environment, orderedQuantity: 100);
        var poLineId = await GetPoLineIdAsync(environment, poVoucherId);
        await environment.InventoryInwardRepository.SaveAsync(NewPurchaseRequest(poLineId, quantity: 40, rate: 10));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryInwardRepository.SaveAsync(NewPurchaseRequest(poLineId, quantity: 70, rate: 10)));

        Assert.Contains("exceeds the pending quantity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelling_a_purchase_frees_its_quantity_back_up_as_pending()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherId = await CreatePurchaseOrderAsync(environment, orderedQuantity: 100);
        var poLineId = await GetPoLineIdAsync(environment, poVoucherId);
        var saved = await environment.InventoryInwardRepository.SaveAsync(
            NewPurchaseRequest(poLineId, quantity: 40, rate: 10));

        var cancelResult = await environment.InventoryInwardRepository.CancelAsync(saved.VoucherId, "Wrong entry");
        Assert.True(cancelResult.Success, cancelResult.Message);

        var pending = await environment.PurchaseOrderRepository.GetPendingLinesAsync();
        var pendingLine = Assert.Single(pending);
        Assert.Equal(100m, pendingLine.PendingQuantity);

        var secondSave = await environment.InventoryInwardRepository.SaveAsync(
            NewPurchaseRequest(poLineId, quantity: 100, rate: 10));
        Assert.NotEqual(0, secondSave.VoucherId);
    }

    [Fact]
    public async Task Po_reference_on_a_purchase_can_be_changed_or_cleared_on_alteration()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherId = await CreatePurchaseOrderAsync(environment, orderedQuantity: 100);
        var poLineId = await GetPoLineIdAsync(environment, poVoucherId);
        var saved = await environment.InventoryInwardRepository.SaveAsync(
            NewPurchaseRequest(poLineId, quantity: 40, rate: 10));
        var edit = await environment.InventoryInwardRepository.GetForEditAsync(saved.VoucherId);
        Assert.NotNull(edit);

        // Clear the PO reference entirely - must succeed (open-market), not be locked.
        var clearedRequest = NewPurchaseRequest(null, quantity: 40, rate: 10);
        clearedRequest.VoucherId = saved.VoucherId;
        clearedRequest.ConcurrencyToken = edit.ConcurrencyToken;
        await environment.InventoryInwardRepository.SaveAsync(clearedRequest);

        await using var db = environment.CreateDbContext();
        var line = await db.InventoryInwardLines.AsNoTracking().SingleAsync(x => x.VoucherId == saved.VoucherId);
        Assert.Null(line.PurchaseOrderLineId);
        Assert.False(await db.VoucherLinks.AnyAsync(x => x.TargetVoucherId == saved.VoucherId));

        var pending = await environment.PurchaseOrderRepository.GetPendingLinesAsync();
        var pendingLine = Assert.Single(pending);
        Assert.Equal(100m, pendingLine.PendingQuantity);
    }

    [Fact]
    public async Task Purchase_can_split_the_same_item_variant_godown_across_two_different_po_references()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherIdA = await CreatePurchaseOrderAsync(environment, orderedQuantity: 50);
        var poLineIdA = await GetPoLineIdAsync(environment, poVoucherIdA);
        var poVoucherIdB = await CreatePurchaseOrderAsync(environment, orderedQuantity: 30);
        var poLineIdB = await GetPoLineIdAsync(environment, poVoucherIdB);

        var request = new InventoryInwardSaveRequest
        {
            Kind = InventoryInwardVoucherKind.Purchase,
            VoucherTypeId = 2,
            VoucherDate = PostingDate,
            ReferenceNumber = "SUP-INV-MULTI-PO",
            SupplierLedgerId = 10,
            Narration = "One invoice, two Purchase Orders",
            Lines =
            [
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 20, Rate = 10, PurchaseOrderLineId = poLineIdA },
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 15, Rate = 12, PurchaseOrderLineId = poLineIdB }
            ]
        };

        var saved = await environment.InventoryInwardRepository.SaveAsync(request);

        await using var db = environment.CreateDbContext();
        var lines = await db.InventoryInwardLines.AsNoTracking().Where(x => x.VoucherId == saved.VoucherId).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, x => x.PurchaseOrderLineId == poLineIdA && x.Quantity == 20m);
        Assert.Contains(lines, x => x.PurchaseOrderLineId == poLineIdB && x.Quantity == 15m);

        var links = await db.VoucherLinks.AsNoTracking().Where(x => x.TargetVoucherId == saved.VoucherId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, x => x.SourceVoucherId == poVoucherIdA);
        Assert.Contains(links, x => x.SourceVoucherId == poVoucherIdB);
    }

    [Fact]
    public async Task Purchase_still_rejects_the_same_item_variant_godown_twice_against_the_same_po_reference()
    {
        await using var environment = await CreateSeededAsync();
        var poVoucherId = await CreatePurchaseOrderAsync(environment, orderedQuantity: 100);
        var poLineId = await GetPoLineIdAsync(environment, poVoucherId);

        var request = new InventoryInwardSaveRequest
        {
            Kind = InventoryInwardVoucherKind.Purchase,
            VoucherTypeId = 2,
            VoucherDate = PostingDate,
            ReferenceNumber = "SUP-INV-DUP-SAME-PO",
            SupplierLedgerId = 10,
            Narration = "Duplicate line against the same PO reference",
            Lines =
            [
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 10, Rate = 10, PurchaseOrderLineId = poLineId },
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 5, Rate = 10, PurchaseOrderLineId = poLineId }
            ]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => environment.InventoryInwardRepository.SaveAsync(request));
        Assert.Contains("cannot appear twice", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Purchase_still_rejects_the_same_item_variant_godown_twice_when_both_are_open_market()
    {
        await using var environment = await CreateSeededAsync();

        var request = new InventoryInwardSaveRequest
        {
            Kind = InventoryInwardVoucherKind.Purchase,
            VoucherTypeId = 2,
            VoucherDate = PostingDate,
            ReferenceNumber = "SUP-INV-DUP-OPEN-MARKET",
            SupplierLedgerId = 10,
            Narration = "Duplicate open-market lines",
            Lines =
            [
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 10, Rate = 10 },
                new InventoryInwardLineInput { StockItemId = 1, StockItemVariantId = 1, UqcId = 1, GodownId = 1, Quantity = 5, Rate = 10 }
            ]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => environment.InventoryInwardRepository.SaveAsync(request));
        Assert.Contains("cannot appear twice", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> CreatePurchaseOrderAsync(PostgreSqlTestEnvironment environment, decimal orderedQuantity)
    {
        var saved = await environment.PurchaseOrderRepository.SaveAsync(new PurchaseOrderSaveRequest
        {
            VoucherTypeId = 4,
            VoucherDate = PostingDate,
            ReferenceNumber = "PO-TEST-1",
            SupplierLedgerId = 10,
            Narration = "Purchase Order linkage test",
            Lines =
            [
                new PurchaseOrderLineInput
                {
                    StockItemId = 1,
                    StockItemVariantId = 1,
                    UqcId = 1,
                    OrderedQuantity = orderedQuantity,
                    Rate = 9
                }
            ]
        });
        return saved.VoucherId;
    }

    private static async Task<long> GetPoLineIdAsync(PostgreSqlTestEnvironment environment, long poVoucherId)
    {
        await using var db = environment.CreateDbContext();
        return await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.VoucherId == poVoucherId)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static InventoryInwardSaveRequest NewPurchaseRequest(long? poLineId, decimal quantity, decimal rate) => new()
    {
        Kind = InventoryInwardVoucherKind.Purchase,
        VoucherTypeId = 2,
        VoucherDate = PostingDate,
        ReferenceNumber = "SUP-INV-1",
        SupplierLedgerId = 10,
        Narration = "Purchase Order linkage test",
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
