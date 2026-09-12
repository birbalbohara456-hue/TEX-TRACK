using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class StockItemOpeningInventoryTests
{
    private static readonly DateOnly BooksBeginningDate = new(2026, 4, 1);

    [Fact]
    public async Task New_item_and_variant_opening_allocations_commit_as_one_posting()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await environment.AddOpeningStockVoucherTypeAsync();
        var model = new StockItemEditModel
        {
            Name = "New Variant Item",
            StockGroupId = 1,
            UqcId = 1,
            TaxMode = "NotApplicable",
            ColourIds = [1],
            SizeIds = [1],
            OpeningInventory =
            [
                new StockItemOpeningAllocationEditModel
                {
                    VariantKey = "C:1|S:1",
                    GodownId = 2,
                    Quantity = 6,
                    Rate = 25,
                    Value = 150,
                    CalculationBasis = "Rate"
                }
            ]
        };

        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var item = await db.StockItems.AsNoTracking().SingleAsync(x => x.Id == result.EntityId);
        var variant = await db.StockItemVariants.AsNoTracking().SingleAsync(x => x.StockItemId == item.Id);
        var voucher = await db.Vouchers.AsNoTracking().SingleAsync(x => x.OpeningStockItemId == item.Id);
        var line = await db.InventoryInwardLines.AsNoTracking().SingleAsync(x => x.VoucherId == voucher.Id);
        Assert.Equal("C:1|S:1", variant.VariantKey);
        Assert.Equal(variant.Id, line.StockItemVariantId);
        Assert.Equal(2, line.GodownId);
        Assert.Equal(6m, line.Quantity);
        Assert.Equal(150m, line.Amount);
    }

    [Fact]
    public async Task Item_master_value_entry_posts_exact_opening_value_at_books_beginning()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddOpeningStockVoucherTypeAsync();
        var model = await environment.GetEditModelAsync(seed.StockItemId);
        model.OpeningInventory.Add(NewAllocation(quantity: 3, rate: 0, value: 100, basis: "Value"));

        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var voucher = await db.Vouchers.AsNoTracking()
            .SingleAsync(x => x.OpeningStockItemId == seed.StockItemId);
        var line = await db.InventoryInwardLines.AsNoTracking()
            .SingleAsync(x => x.VoucherId == voucher.Id);
        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(x => x.VoucherId == voucher.Id);
        Assert.Equal(BooksBeginningDate, voucher.VoucherDate);
        Assert.Null(voucher.PartyLedgerId);
        Assert.Equal(seed.StockItemId, line.StockItemId);
        Assert.Equal(seed.StockItemVariantId, line.StockItemVariantId);
        Assert.Equal(1, line.GodownId);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(33.3333m, line.Rate);
        Assert.Equal(100m, line.Amount);
        Assert.Equal(StockMovementSemantics.OpeningStockInward, movement.MovementKind);
        Assert.Equal(3m, movement.QuantityChange);
        Assert.Equal(100m, movement.ValueChange);
        Assert.Equal(1, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == voucher.Id));
    }

    [Fact]
    public async Task Editing_an_item_replaces_its_single_opening_posting_without_duplicate_history()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddOpeningStockVoucherTypeAsync();
        var model = await environment.GetEditModelAsync(seed.StockItemId);
        model.OpeningInventory.Add(NewAllocation(quantity: 5, rate: 12, value: 60, basis: "Rate"));
        Assert.True((await environment.Repository.SaveAsync(model)).Success);

        model = await environment.GetEditModelAsync(seed.StockItemId);
        var voucherId = Assert.IsType<long>(model.OpeningStockVoucherId);
        model.OpeningInventory[0].Quantity = 8;
        model.OpeningInventory[0].Value = 120;
        model.OpeningInventory[0].CalculationBasis = "Value";
        Assert.True((await environment.Repository.SaveAsync(model)).Success);

        model = await environment.GetEditModelAsync(seed.StockItemId);
        model.Name = "Candidate Item Renamed";
        Assert.True((await environment.Repository.SaveAsync(model)).Success);

        await using var db = environment.CreateDbContext();
        Assert.Equal(1, await db.Vouchers.CountAsync(x => x.OpeningStockItemId == seed.StockItemId));
        var line = await db.InventoryInwardLines.AsNoTracking().SingleAsync(x => x.VoucherId == voucherId);
        var movement = await db.StockMovements.AsNoTracking().SingleAsync(x => x.VoucherId == voucherId);
        Assert.Equal(8m, line.Quantity);
        Assert.Equal(15m, line.Rate);
        Assert.Equal(120m, line.Amount);
        Assert.Equal(8m, movement.QuantityChange);
        Assert.Equal(120m, movement.ValueChange);
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == voucherId));
    }

    [Fact]
    public async Task Clearing_item_master_opening_inventory_removes_stock_effect_and_retains_audit()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddOpeningStockVoucherTypeAsync();
        var model = await environment.GetEditModelAsync(seed.StockItemId);
        model.OpeningInventory.Add(NewAllocation(quantity: 10, rate: 4, value: 40, basis: "Rate"));
        Assert.True((await environment.Repository.SaveAsync(model)).Success);

        model = await environment.GetEditModelAsync(seed.StockItemId);
        var voucherId = Assert.IsType<long>(model.OpeningStockVoucherId);
        model.OpeningInventory.Clear();
        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.Vouchers.AnyAsync(x => x.Id == voucherId));
        Assert.False(await db.InventoryInwardLines.AnyAsync(x => x.VoucherId == voucherId));
        Assert.False(await db.StockMovements.AnyAsync(x => x.VoucherId == voucherId));
        Assert.Equal(2, await db.VoucherAuditRevisions.CountAsync(x => x.VoucherId == voucherId));
        Assert.True(await db.VoucherAuditRevisions.AnyAsync(x =>
            x.VoucherId == voucherId && x.Action == VoucherAuditActions.Delete));
    }

    [Fact]
    public async Task Duplicate_item_variant_godown_allocations_are_rejected_atomically()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        var model = await environment.GetEditModelAsync(seed.StockItemId);
        model.Name = "Must Not Persist";
        model.OpeningInventory.Add(NewAllocation(5, 10, 50, "Rate"));
        model.OpeningInventory.Add(NewAllocation(7, 10, 70, "Rate"));

        var result = await environment.Repository.SaveAsync(model);

        Assert.False(result.Success);
        Assert.Contains("cannot appear twice", result.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = environment.CreateDbContext();
        Assert.Equal("Candidate Item", await db.StockItems.Where(x => x.Id == seed.StockItemId).Select(x => x.Name).SingleAsync());
        Assert.False(await db.Vouchers.AnyAsync(x => x.OpeningStockItemId == seed.StockItemId));
        Assert.False(await db.StockMovements.AnyAsync(x => x.StockItemId == seed.StockItemId));
    }

    private static StockItemOpeningAllocationEditModel NewAllocation(
        decimal quantity,
        decimal rate,
        decimal value,
        string basis) => new()
    {
        VariantKey = "BASE",
        GodownId = 1,
        Quantity = quantity,
        Rate = rate,
        Value = value,
        CalculationBasis = basis
    };
}
