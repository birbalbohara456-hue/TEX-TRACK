using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Domain;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class ComponentVariantMovementIntegrityTests
{
    [Fact]
    public async Task Migration_backfills_only_deterministic_component_and_movement_variants()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SeedHistoricalMovementLinksAsync(environment, includeAmbiguousComponent: true);

        await using (var db = environment.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync(await ReadMigrationAsync());

        await using var verify = environment.CreateDbContext();
        Assert.Equal(3, await verify.JobWorkOrderComponents
            .Where(x => x.Id == 20)
            .Select(x => x.ComponentVariantId)
            .SingleAsync());
        Assert.Null(await verify.JobWorkOrderComponents
            .Where(x => x.Id == 21)
            .Select(x => x.ComponentVariantId)
            .SingleAsync());

        Assert.All(
            await verify.StockMovements
                .Where(x => x.MaterialOutLineId == 30 || x.MaterialInConsumptionId == 40)
                .ToListAsync(),
            movement => Assert.Equal(3, movement.StockItemVariantId));
        Assert.Null(await verify.StockMovements
            .Where(x => x.MaterialOutLineId == 31)
            .Select(x => x.StockItemVariantId)
            .SingleAsync());
    }

    [Fact]
    public async Task Migration_stops_instead_of_overwriting_an_inconsistent_existing_variant()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SeedHistoricalMovementLinksAsync(environment, includeAmbiguousComponent: false);
        var migrationSql = await ReadMigrationAsync();

        await using (var db = environment.CreateDbContext())
        {
            var movement = await db.StockMovements.FirstAsync(x => x.MaterialOutLineId == 30);
            movement.StockItemVariantId = 1;
            await db.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync(migrationSql));
            Assert.Contains("inconsistent component/variant identity", error.MessageText, StringComparison.Ordinal);
        }
    }

    private static async Task SeedHistoricalMovementLinksAsync(
        PostgreSqlTestEnvironment environment,
        bool includeAmbiguousComponent)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = environment.CreateDbContext();
        var finishedGood = new JobWorkOrderFinishedGood
        {
            Id = 10,
            VoucherId = 1,
            LineNumber = 1,
            StockItemId = 2,
            OrderedQuantity = 10,
            FinishedGoodsGodownId = 1,
            DestinationGodownId = 2
        };
        var deterministicComponent = new JobWorkOrderComponent
        {
            Id = 20,
            FinishedGoodId = 10,
            LineNumber = 1,
            StockItemId = 3,
            UqcId = 1,
            RequiredQuantity = 10,
            ComponentGodownId = 1
        };
        db.JobWorkOrderFinishedGoods.Add(finishedGood);
        db.JobWorkOrderComponents.Add(deterministicComponent);
        if (includeAmbiguousComponent)
        {
            db.StockItemVariants.Add(new StockItemVariant
            {
                Id = 4,
                CompanyId = 1,
                StockItemId = 1,
                VariantKey = "SECOND",
                IsActive = true,
                CreatedAtUtc = now,
                ModifiedAtUtc = now
            });
            db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
            {
                Id = 21,
                FinishedGoodId = 10,
                LineNumber = 2,
                StockItemId = 1,
                UqcId = 1,
                RequiredQuantity = 5,
                ComponentGodownId = 1
            });
        }

        db.Vouchers.AddRange(
            NewVoucher(2, "MO-HIST", 2, new DateOnly(2026, 8, 2), now),
            NewVoucher(3, "MI-HIST", 3, new DateOnly(2026, 8, 3), now));
        db.MaterialOutLines.Add(new MaterialOutLine
        {
            Id = 30,
            VoucherId = 2,
            JwoVoucherId = 1,
            JwoFinishedGoodId = 10,
            JwoComponentId = 20,
            LineNumber = 1,
            StockItemId = 3,
            UqcId = 1,
            SourceGodownId = 1,
            DestinationGodownId = 2,
            RequiredQuantity = 10,
            IssuedQuantity = 10,
            Rate = 5,
            Amount = 50
        });
        if (includeAmbiguousComponent)
        {
            db.MaterialOutLines.Add(new MaterialOutLine
            {
                Id = 31,
                VoucherId = 2,
                JwoVoucherId = 1,
                JwoFinishedGoodId = 10,
                JwoComponentId = 21,
                LineNumber = 2,
                StockItemId = 1,
                UqcId = 1,
                SourceGodownId = 1,
                DestinationGodownId = 2,
                RequiredQuantity = 5,
                IssuedQuantity = 5,
                Rate = 2,
                Amount = 10
            });
        }
        db.MaterialInConsumptions.Add(new MaterialInConsumption
        {
            Id = 40,
            VoucherId = 3,
            JwoComponentId = 20,
            LineNumber = 1,
            StockItemId = 3,
            UqcId = 1,
            ConsumptionGodownId = 2,
            AvailableQuantity = 10,
            ConsumedQuantity = 5,
            Rate = 5,
            Value = 25
        });
        await db.SaveChangesAsync();

        db.StockMovements.AddRange(
            NewMovement(2, 3, 1, -10, -50, "MaterialOutSource", materialOutLineId: 30),
            NewMovement(2, 3, 2, 10, 50, "MaterialOutDestination", materialOutLineId: 30),
            NewMovement(3, 3, 2, -5, -25, "MaterialInConsumption", materialInConsumptionId: 40));
        if (includeAmbiguousComponent)
            db.StockMovements.Add(NewMovement(
                2, 1, 1, -5, -10, "MaterialOutSource", materialOutLineId: 31));
        await db.SaveChangesAsync();
    }

    private static Voucher NewVoucher(
        long id,
        string number,
        int sequence,
        DateOnly date,
        DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        FinancialYearId = 1,
        VoucherTypeId = 1,
        SequenceNumber = sequence,
        VoucherNumber = number,
        VoucherNumberNormalized = number,
        VoucherDate = date,
        Status = "Open",
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static StockMovement NewMovement(
        long voucherId,
        long stockItemId,
        long godownId,
        decimal quantity,
        decimal value,
        string kind,
        long? materialOutLineId = null,
        long? materialInConsumptionId = null) => new()
    {
        CompanyId = 1,
        FinancialYearId = 1,
        VoucherId = voucherId,
        MaterialOutLineId = materialOutLineId,
        MaterialInConsumptionId = materialInConsumptionId,
        MovementDate = new DateOnly(2026, 8, checked((int)voucherId)),
        StockItemId = stockItemId,
        UqcId = 1,
        GodownId = godownId,
        QuantityChange = quantity,
        Rate = Math.Abs(value / quantity),
        ValueChange = value,
        MovementKind = kind,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedBy = "Historical Test"
    };

    private static Task<string> ReadMigrationAsync() => File.ReadAllTextAsync(Path.Combine(
        AppContext.BaseDirectory,
        "Data",
        "Migrations",
        "030_component_variant_movement_integrity.sql"));
}
