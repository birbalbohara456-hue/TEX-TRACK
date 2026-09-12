using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class StockPositionServiceTests
{
    [Fact]
    public async Task Query_keeps_item_variant_uqc_godown_and_as_on_dimensions_exact()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SeedMovementsAsync(environment);

        var early = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(1, 1, 1, 1, new DateOnly(2026, 4, 3)),
            CancellationToken.None);
        var current = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(1, 1, 1, 1, new DateOnly(2026, 4, 30)),
            CancellationToken.None);
        var basePosition = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(1, null, 1, 1, new DateOnly(2026, 4, 30)),
            CancellationToken.None);
        var otherGodown = await environment.StockPositionService.GetAsync(
            new StockPositionRequest(1, 1, 1, 2, new DateOnly(2026, 4, 30)),
            CancellationToken.None);

        Assert.Equal(10, early.Quantity);
        Assert.Equal(100, early.Value);
        Assert.Equal(6, current.Quantity);
        Assert.Equal(60, current.Value);
        Assert.Equal(10, current.AverageRate);
        Assert.Equal(5, basePosition.Quantity);
        Assert.Equal(50, basePosition.Value);
        Assert.Equal(7, otherGodown.Quantity);
        Assert.Equal(70, otherGodown.Value);
    }

    [Fact]
    public async Task Query_rejects_masters_owned_by_another_company()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SeedOtherCompanyAsync(environment);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.StockPositionService.GetAsync(
                new StockPositionRequest(20, null, 20, 20, new DateOnly(2026, 4, 30)),
                CancellationToken.None));

        Assert.Contains("mismatched company master", error.Message, StringComparison.Ordinal);
    }

    private static async Task SeedMovementsAsync(PostgreSqlTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        db.StockMovements.AddRange(
            Movement(101, new DateOnly(2026, 4, 1), 1, 1, 1, 1, 10, 100, now),
            Movement(102, new DateOnly(2026, 4, 5), 1, 1, 1, 1, -4, -40, now),
            Movement(103, new DateOnly(2026, 4, 2), 1, null, 1, 1, 5, 50, now),
            Movement(104, new DateOnly(2026, 4, 2), 1, 1, 1, 2, 7, 70, now),
            Movement(105, new DateOnly(2026, 4, 2), 1, 1, 2, 1, 11, 110, now),
            Movement(106, new DateOnly(2026, 4, 2), 2, 2, 1, 1, 13, 130, now));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static StockMovement Movement(
        long id, DateOnly date, long itemId, long? variantId, long uqcId, long godownId,
        decimal quantity, decimal value, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        FinancialYearId = 1,
        VoucherId = 1,
        MovementDate = date,
        StockItemId = itemId,
        StockItemVariantId = variantId,
        UqcId = uqcId,
        GodownId = godownId,
        QuantityChange = quantity,
        Rate = quantity == 0 ? 0 : value / quantity,
        ValueChange = value,
        MovementKind = "MaterialInFinishedGoods",
        CreatedAtUtc = now,
        CreatedBy = "Test"
    };

    private static async Task SeedOtherCompanyAsync(PostgreSqlTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        db.Companies.Add(new Company
        {
            Id = 2,
            Name = "Other Company",
            NameNormalized = "OTHER COMPANY",
            Code = "OTHER",
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.StockGroups.Add(new StockGroup
        {
            Id = 20, CompanyId = 2, Name = "Other Raw Material", NameNormalized = "OTHER RAW MATERIAL",
            RootClassification = "RawMaterial", CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.Uqcs.Add(new Uqc
        {
            Id = 20, CompanyId = 2, Name = "Other Pieces", NameNormalized = "OTHER PIECES",
            ShortName = "OPC", DecimalPlaces = 2, CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.Godowns.Add(new Godown
        {
            Id = 20, CompanyId = 2, Name = "Other Godown", NameNormalized = "OTHER GODOWN",
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        db.StockItems.Add(new StockItem
        {
            Id = 20, CompanyId = 2, Name = "Other Item", NameNormalized = "OTHER ITEM",
            StockGroupId = 20, UqcId = 20, TaxMode = "NotApplicable",
            CreatedAtUtc = now, ModifiedAtUtc = now
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
