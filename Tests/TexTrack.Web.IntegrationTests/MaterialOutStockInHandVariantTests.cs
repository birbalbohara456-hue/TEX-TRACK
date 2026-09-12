using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class MaterialOutStockInHandVariantTests
{
    // Regression for the "Stock in Hand" bug: a JWO component tied to a specific
    // colour/size variant must carry that variant id all the way into the
    // Material Out pending-component list, because StockPositionService matches
    // the variant exactly (including null) - reading with variant=null against a
    // component whose real stock sits under a colour/size variant always finds
    // nothing, even though the component genuinely has stock.
    [Fact]
    public async Task Pending_component_carries_its_component_variant_so_stock_position_is_not_read_against_the_base_variant()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        const long jobWorkerLedgerId = 50;
        long variantId;

        await using (var db = environment.CreateDbContext())
        {
            var jwoType = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
            jwoType.SystemTypeCode = "JOB_WORK_OUT_ORDER";

            db.LedgerGroups.Add(new LedgerGroup
            {
                Id = 50, CompanyId = 1, Name = "Job Workers", NameNormalized = "JOB WORKERS",
                RootClassification = "SundryCreditors", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            db.Ledgers.Add(new Ledger
            {
                Id = jobWorkerLedgerId, CompanyId = 1, LedgerGroupId = 50, Name = "Variant Worker",
                NameNormalized = "VARIANT WORKER", IsJobWorker = true, IsActive = true,
                CreatedAtUtc = now, ModifiedAtUtc = now
            });
            await db.SaveChangesAsync();

            var jwo = await db.Vouchers.SingleAsync(x => x.Id == 1);
            jwo.PartyLedgerId = jobWorkerLedgerId;

            var variant = new StockItemVariant
            {
                CompanyId = 1, StockItemId = 3, ColourId = 1, VariantKey = "C:1|S:-",
                IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            };
            db.StockItemVariants.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;

            // Stock exists only under the colour variant, never under the base
            // (null) variant - exactly the shape the bug got wrong.
            db.StockMovements.Add(new StockMovement
            {
                CompanyId = 1, FinancialYearId = 1, VoucherId = 1,
                MovementDate = new DateOnly(2026, 4, 1), StockItemId = 3, StockItemVariantId = variantId,
                UqcId = 1, GodownId = 1, QuantityChange = 40, Rate = 5, ValueChange = 200,
                MovementKind = "MaterialInFinishedGoods", CreatedAtUtc = now, CreatedBy = "Test"
            });

            var finishedGood = new JobWorkOrderFinishedGood
            {
                VoucherId = 1, LineNumber = 1, StockItemId = 2, OrderedQuantity = 10,
                FinishedGoodsGodownId = 1, DestinationGodownId = 1
            };
            db.JobWorkOrderFinishedGoods.Add(finishedGood);
            await db.SaveChangesAsync();

            db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
            {
                FinishedGoodId = finishedGood.Id, LineNumber = 1, StockItemId = 3,
                ComponentVariantId = variantId, UqcId = 1, RequiredQuantity = 10, ComponentGodownId = 1
            });
            await db.SaveChangesAsync();
        }

        var pending = Assert.Single(await environment.MaterialOutRepository.GetPendingOrdersAsync(jobWorkerLedgerId));
        var component = Assert.Single(Assert.Single(pending.FinishedGoods).Components);
        Assert.Equal(variantId, component.StockItemVariantId);

        var correctPosition = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(component.StockItemId, component.StockItemVariantId, component.UqcId, 1, new DateOnly(2026, 4, 30)),
            CancellationToken.None);
        var basePosition = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(component.StockItemId, null, component.UqcId, 1, new DateOnly(2026, 4, 30)),
            CancellationToken.None);

        Assert.Equal(40m, correctPosition.Quantity);
        Assert.Equal(0m, basePosition.Quantity);
    }
}
