using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

/// <summary>
/// Records the valuation behaviour that exists before a cost-layer/FIFO redesign.
/// These tests are intentionally descriptive: changing an expectation requires an
/// approved valuation, backdating, cancellation, or financial-year policy change.
/// </summary>
public sealed class InventoryValuationCharacterizationTests
{
    [Fact]
    public async Task Native_material_out_and_material_in_consumption_preserve_component_variant_identity()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var chain = await SeedManualProductionChainAsync(environment);
        var materialOutRequest = NewMaterialOutRequest(chain, new DateOnly(2026, 8, 2));

        var materialOut = await environment.MaterialOutRepository.SaveAsync(materialOutRequest);
        await using (var persisted = environment.CreateDbContext())
        {
            materialOutRequest.VoucherId = materialOut.VoucherId;
            materialOutRequest.ConcurrencyToken = await persisted.Vouchers
                .Where(x => x.Id == materialOut.VoucherId)
                .Select(x => x.ConcurrencyToken)
                .SingleAsync();
        }
        await environment.MaterialOutRepository.UpdateAsync(materialOutRequest);

        var receipt = await SaveReceiptAsync(
            environment, chain, new DateOnly(2026, 8, 3),
            receivedQuantity: 10, consumedQuantity: 10, totalProcessCharge: 25);

        await using var db = environment.CreateDbContext();
        var materialOutMovements = await db.StockMovements.AsNoTracking()
            .Where(x => x.VoucherId == materialOut.VoucherId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, materialOutMovements.Count);
        Assert.All(materialOutMovements, movement => Assert.Equal(1, movement.StockItemVariantId));

        var consumptionMovement = await db.StockMovements.AsNoTracking()
            .SingleAsync(x => x.VoucherId == receipt.VoucherId && x.MovementKind == "MaterialInConsumption");
        Assert.Equal(1, consumptionMovement.StockItemVariantId);
    }

    [Fact]
    public async Task Material_in_allocates_oldest_mo_rates_and_capitalizes_material_plus_process_charge()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var chain = await SeedManualProductionChainAsync(environment);
        var firstMoLineId = await AddMaterialOutLineAsync(
            environment, chain, sequence: 1,
            date: new DateOnly(2026, 8, 2), quantity: 40, rate: 10);
        var secondMoLineId = await AddMaterialOutLineAsync(
            environment, chain, sequence: 2,
            date: new DateOnly(2026, 8, 3), quantity: 60, rate: 20);

        var receipt = await SaveReceiptAsync(
            environment, chain, new DateOnly(2026, 8, 4),
            receivedQuantity: 50, consumedQuantity: 50, totalProcessCharge: 100);

        await using var db = environment.CreateDbContext();
        var allocations = await db.MaterialInMaterialOutAllocations.AsNoTracking()
            .Where(x => x.ConsumptionLine.VoucherId == receipt.VoucherId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Collection(allocations,
            first =>
            {
                Assert.Equal(firstMoLineId, first.MaterialOutLineId);
                Assert.Equal(40m, first.AllocatedQuantity);
                Assert.Equal(10m, first.RateSnapshot);
                Assert.Equal(400m, first.ValueSnapshot);
            },
            second =>
            {
                Assert.Equal(secondMoLineId, second.MaterialOutLineId);
                Assert.Equal(10m, second.AllocatedQuantity);
                Assert.Equal(20m, second.RateSnapshot);
                Assert.Equal(200m, second.ValueSnapshot);
            });

        var consumption = await db.MaterialInConsumptions.AsNoTracking()
            .SingleAsync(x => x.VoucherId == receipt.VoucherId);
        Assert.Equal(50m, consumption.ConsumedQuantity);
        Assert.Equal(600m, consumption.Value);
        Assert.Equal(12m, consumption.Rate);

        var output = await db.MaterialInFinishedGoods.AsNoTracking()
            .SingleAsync(x => x.VoucherId == receipt.VoucherId);
        Assert.Equal(600m, output.MaterialValue);
        Assert.Equal(100m, output.ProcessCharge);
        Assert.Equal(700m, output.FinishedGoodsValue);
        Assert.Equal(14m, output.Rate);

        var movements = await db.StockMovements.AsNoTracking()
            .Where(x => x.VoucherId == receipt.VoucherId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Contains(movements, x =>
            x.MovementKind == "MaterialInConsumption" &&
            x.QuantityChange == -50m && x.ValueChange == -600m);
        Assert.Contains(movements, x =>
            x.MovementKind == "MaterialInFinishedGoods" &&
            x.QuantityChange == 50m && x.ValueChange == 700m);
    }

    [Fact]
    public async Task Backdated_mo_does_not_rebalance_an_existing_mi_allocation()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var chain = await SeedManualProductionChainAsync(environment);
        var originalMoLineId = await AddMaterialOutLineAsync(
            environment, chain, sequence: 1,
            date: new DateOnly(2026, 8, 10), quantity: 100, rate: 20);

        var firstReceipt = await SaveReceiptAsync(
            environment, chain, new DateOnly(2026, 8, 11),
            receivedQuantity: 50, consumedQuantity: 50, totalProcessCharge: 50);

        var backdatedMoLineId = await AddMaterialOutLineAsync(
            environment, chain, sequence: 2,
            date: new DateOnly(2026, 8, 5), quantity: 10, rate: 10);

        var secondReceipt = await SaveReceiptAsync(
            environment, chain, new DateOnly(2026, 8, 12),
            receivedQuantity: 10, consumedQuantity: 10, totalProcessCharge: 10);

        await using var db = environment.CreateDbContext();
        var firstAllocation = await db.MaterialInMaterialOutAllocations.AsNoTracking()
            .SingleAsync(x => x.ConsumptionLine.VoucherId == firstReceipt.VoucherId);
        Assert.Equal(originalMoLineId, firstAllocation.MaterialOutLineId);
        Assert.Equal(50m, firstAllocation.AllocatedQuantity);
        Assert.Equal(20m, firstAllocation.RateSnapshot);
        Assert.Equal(1_000m, firstAllocation.ValueSnapshot);

        var secondAllocation = await db.MaterialInMaterialOutAllocations.AsNoTracking()
            .SingleAsync(x => x.ConsumptionLine.VoucherId == secondReceipt.VoucherId);
        Assert.Equal(backdatedMoLineId, secondAllocation.MaterialOutLineId);
        Assert.Equal(10m, secondAllocation.AllocatedQuantity);
        Assert.Equal(10m, secondAllocation.RateSnapshot);
        Assert.Equal(100m, secondAllocation.ValueSnapshot);

        Assert.Equal(50m, await db.MaterialInMaterialOutAllocations
            .Where(x => x.MaterialOutLineId == originalMoLineId)
            .SumAsync(x => x.AllocatedQuantity));
    }

    [Fact]
    public async Task Cancelled_voucher_is_absent_from_an_as_on_report_before_its_cancellation_time()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        await using (var db = environment.CreateDbContext())
        {
            var voucher = await db.Vouchers.SingleAsync(x => x.Id == 1);
            voucher.VoucherDate = new DateOnly(2026, 8, 1);
            voucher.Status = VoucherLifecycleService.CancelledStatus;
            voucher.CancellationReason = "Characterize historical report policy";
            voucher.CancelledAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
            voucher.CancelledBy = "Test";
            db.StockMovements.Add(NewMovement(
                voucherId: 1, financialYearId: 1,
                date: new DateOnly(2026, 8, 1), quantity: 10, value: 100));
            await db.SaveChangesAsync();
        }

        var rows = await environment.OperationalReportingService.GetClosingStockGroupsAsync(
            new InventoryReportFilter { DateTo = new DateOnly(2026, 8, 15) });

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Closing_stock_aggregates_movements_across_financial_year_ids()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = environment.CreateDbContext())
        {
            var firstVoucher = await db.Vouchers.SingleAsync(x => x.Id == 1);
            firstVoucher.VoucherDate = new DateOnly(2027, 3, 31);
            db.FinancialYears.Add(new FinancialYear
            {
                Id = 2, CompanyId = 1, Name = "2027-28",
                StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2028, 3, 31),
                IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            db.Vouchers.Add(new Voucher
            {
                Id = 2, CompanyId = 1, FinancialYearId = 2, VoucherTypeId = 1,
                SequenceNumber = 1, VoucherNumber = "FY2-1", VoucherNumberNormalized = "FY2-1",
                VoucherDate = new DateOnly(2027, 4, 2), Status = "Open",
                CreatedAtUtc = now, ModifiedAtUtc = now
            });
            db.StockMovements.AddRange(
                NewMovement(1, 1, new DateOnly(2027, 3, 31), 10, 100),
                NewMovement(2, 2, new DateOnly(2027, 4, 2), 5, 60));
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await environment.OperationalReportingService.GetClosingStockGroupsAsync(
            new InventoryReportFilter { DateTo = new DateOnly(2027, 4, 5) }));
        Assert.Equal(15m, row.Quantity);
        Assert.Equal(160m, row.Value);
        Assert.Equal(160m / 15m, row.AverageRate);
    }

    [Fact]
    public async Task Stock_posting_accepts_an_outward_movement_without_prior_stock()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var posting = new StockPostingService();

        await using (var db = environment.CreateDbContext())
        {
            posting.Post(db, new StockMovementDraft(
                CompanyId: 1, FinancialYearId: 1, VoucherId: 1,
                MovementDate: new DateOnly(2026, 8, 1),
                StockItemId: 1, UqcId: 1, GodownId: 1,
                QuantityChange: -25, Rate: 10, ValueChange: -250,
                MovementKind: "MaterialOutSource"),
                "Characterization Test", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await using var verify = environment.CreateDbContext();
        var movement = await verify.StockMovements.AsNoTracking().SingleAsync();
        Assert.Equal(-25m, movement.QuantityChange);
        Assert.Equal(-250m, movement.ValueChange);
    }

    private static async Task<ManualProductionChain> SeedManualProductionChainAsync(
        PostgreSqlTestEnvironment environment)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = environment.CreateDbContext();
        var jwo = await db.Vouchers.SingleAsync(x => x.Id == 1);
        jwo.VoucherDate = new DateOnly(2026, 8, 1);
        jwo.Batch = "VALUATION-CHAIN";
        jwo.PartyLedgerId = 10;
        var jwoType = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
        jwoType.SystemTypeCode = "JOB_WORK_OUT_ORDER";

        db.LedgerGroups.Add(new LedgerGroup
        {
            Id = 10, CompanyId = 1, Name = "Job Workers", NameNormalized = "JOB WORKERS",
            RootClassification = "SundryCreditors", IsActive = true,
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.Ledgers.Add(new Ledger
        {
            Id = 10, CompanyId = 1, LedgerGroupId = 10,
            Name = "Valuation Worker", NameNormalized = "VALUATION WORKER",
            IsJobWorker = true, IsActive = true,
            DefaultMaterialOutDestinationGodownId = 2,
            DefaultMaterialInConsumptionGodownId = 2,
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.VoucherTypes.AddRange(
            NewVoucherType(2, "Material Out", "MATERIAL_OUT", "MO", now),
            NewVoucherType(3, "Material In", "MATERIAL_IN", "MI", now));

        var finishedGood = new JobWorkOrderFinishedGood
        {
            VoucherId = 1, LineNumber = 1, StockItemId = 2,
            OrderedQuantity = 100, FinishedGoodsGodownId = 1, DestinationGodownId = 2
        };
        db.JobWorkOrderFinishedGoods.Add(finishedGood);
        await db.SaveChangesAsync();
        db.JobWorkOrderSizeAllocations.Add(new JobWorkOrderSizeAllocation
        {
            FinishedGoodId = finishedGood.Id, StockItemVariantId = 2, Quantity = 100
        });
        var component = new JobWorkOrderComponent
        {
            FinishedGoodId = finishedGood.Id, LineNumber = 1,
            StockItemId = 1, UqcId = 1, RequiredQuantity = 100,
            ComponentGodownId = 1, ComponentVariantId = 1, XmlRate = 10
        };
        db.JobWorkOrderComponents.Add(component);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('vouchers', 'id'), (SELECT MAX(id) FROM vouchers), true)");
        return new ManualProductionChain(finishedGood.Id, component.Id);
    }

    private static MaterialOutSaveRequest NewMaterialOutRequest(
        ManualProductionChain chain,
        DateOnly date) => new()
    {
        VoucherTypeId = 2,
        VoucherDate = date,
        JobWorkerLedgerId = 10,
        JwoVoucherId = 1,
        DisplayedOrderNumber = "JWO-1",
        DestinationGodownId = 2,
        Batch = "VALUATION-CHAIN",
        Lines =
        [
            new MaterialOutSaveLine
            {
                JwoFinishedGoodId = chain.FinishedGoodId,
                JwoComponentId = chain.ComponentId,
                StockItemId = 1,
                UqcId = 1,
                SourceGodownId = 1,
                IssuedQuantity = 10,
                Rate = 10
            }
        ]
    };

    private static async Task<long> AddMaterialOutLineAsync(
        PostgreSqlTestEnvironment environment,
        ManualProductionChain chain,
        int sequence,
        DateOnly date,
        decimal quantity,
        decimal rate)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = environment.CreateDbContext();
        var voucher = new Voucher
        {
            CompanyId = 1, FinancialYearId = 1, VoucherTypeId = 2,
            SequenceNumber = sequence, VoucherNumber = $"MO-{sequence}", VoucherNumberNormalized = $"MO-{sequence}",
            VoucherDate = date, PartyLedgerId = 10, Batch = "VALUATION-CHAIN", Status = "Open",
            CreatedAtUtc = now, ModifiedAtUtc = now
        };
        db.Vouchers.Add(voucher);
        var line = new MaterialOutLine
        {
            Voucher = voucher, JwoVoucherId = 1,
            JwoFinishedGoodId = chain.FinishedGoodId, JwoComponentId = chain.ComponentId,
            LineNumber = 1, StockItemId = 1, UqcId = 1,
            SourceGodownId = 1, DestinationGodownId = 2,
            RequiredQuantity = 100, IssuedQuantity = quantity,
            Rate = rate, Amount = quantity * rate
        };
        db.MaterialOutLines.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    private static Task<MaterialInSaveResult> SaveReceiptAsync(
        PostgreSqlTestEnvironment environment,
        ManualProductionChain chain,
        DateOnly date,
        decimal receivedQuantity,
        decimal consumedQuantity,
        decimal totalProcessCharge) =>
        environment.MaterialInRepository.SaveAsync(new MaterialInSaveRequest
        {
            VoucherTypeId = 3,
            VoucherDate = date,
            JobWorkerLedgerId = 10,
            JwoVoucherId = 1,
            ConsumptionGodownId = 2,
            ReceivingGodownId = 1,
            Batch = "VALUATION-CHAIN",
            FinishedGoods =
            [
                new MaterialInFinishedGoodInput
                {
                    ChargeMode = ProcessChargeMode.Total,
                    JwoFinishedGoodId = chain.FinishedGoodId,
                    ReceivedQuantity = receivedQuantity,
                    TotalCharge = totalProcessCharge,
                    Variants =
                    [
                        new MaterialInVariantInput
                        {
                            StockItemVariantId = 2,
                            Quantity = receivedQuantity
                        }
                    ]
                }
            ],
            Consumptions =
            [
                new MaterialInConsumptionInput
                {
                    JwoComponentId = chain.ComponentId,
                    ConsumedQuantity = consumedQuantity
                }
            ]
        });

    private static VoucherType NewVoucherType(
        long id, string name, string systemTypeCode, string abbreviation, DateTimeOffset now) => new()
    {
        Id = id, CompanyId = 1, Name = name, NameNormalized = name.ToUpperInvariant(),
        SystemTypeCode = systemTypeCode, Nature = "Inventory", PostingMode = "Actual",
        Abbreviation = abbreviation, NumberingMode = "Auto", ResetPeriod = "FinancialYear",
        TallyVoucherTypeName = name, IsActive = true,
        CreatedAtUtc = now, ModifiedAtUtc = now
    };

    private static StockMovement NewMovement(
        long voucherId,
        long financialYearId,
        DateOnly date,
        decimal quantity,
        decimal value) => new()
    {
        CompanyId = 1, FinancialYearId = financialYearId, VoucherId = voucherId,
        MovementDate = date, StockItemId = 1, UqcId = 1, GodownId = 1,
        QuantityChange = quantity,
        Rate = quantity == 0 ? 0 : decimal.Round(value / quantity, 4),
        ValueChange = value, MovementKind = "MaterialInFinishedGoods",
        CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Characterization Test"
    };

    private sealed record ManualProductionChain(long FinishedGoodId, long ComponentId);
}
