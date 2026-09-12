using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class NegativeStockPolicyTests
{
    private static readonly DateOnly PostingDate = new(2026, 7, 10);

    [Fact]
    public async Task Blocking_uses_the_exact_godown_and_does_not_use_future_inward_stock()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetPolicyAsync(environment, allow: false);

        await using var db = environment.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        environment.StockPostingService.Post(db, Draft(1, PostingDate.AddDays(1), 2, 10, "PurchaseInward"), "Test", DateTimeOffset.UtcNow);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, -1, "MaterialOutSource"), "Test", DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.StockPostingService.EnsureNegativeStockPolicyAsync(db, 1));

        Assert.Contains("Negative stock is blocked", error.Message, StringComparison.Ordinal);
        Assert.Contains("Main Godown", error.Message, StringComparison.Ordinal);
        Assert.Contains("10-Jul-2026", error.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Multiple_outward_lines_in_one_transaction_are_checked_as_one_final_position()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetPolicyAsync(environment, allow: false);

        await using var db = environment.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, 10, "OpeningStockInward"), "Test", DateTimeOffset.UtcNow);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, -6, "MaterialOutSource"), "Test", DateTimeOffset.UtcNow);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, -6, "MaterialOutSource"), "Test", DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.StockPostingService.EnsureNegativeStockPolicyAsync(db, 1));

        Assert.Contains("short by 2 PCS", error.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Base_identity_is_not_satisfied_by_stock_of_a_concrete_variant()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetPolicyAsync(environment, allow: false);

        await using var db = environment.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, 10, "OpeningStockInward"), "Test", DateTimeOffset.UtcNow);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, -1, "MaterialOutSource", variantId: null), "Test", DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.StockPostingService.EnsureNegativeStockPolicyAsync(db, 1));

        Assert.Contains("Base / unspecified", error.Message, StringComparison.Ordinal);
        Assert.Contains("short by 1 PCS", error.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Allow_mode_preserves_the_explicit_company_override()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        await using var db = environment.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        environment.StockPostingService.Post(db, Draft(1, PostingDate, 1, -5, "MaterialOutSource"), "Test", DateTimeOffset.UtcNow);
        await environment.StockPostingService.EnsureNegativeStockPolicyAsync(db, 1);
        await transaction.CommitAsync();

        await using var verify = environment.CreateDbContext();
        Assert.Equal(-5, await verify.StockMovements.SumAsync(x => x.QuantityChange));
    }

    [Fact]
    public async Task Administrator_cannot_enable_blocking_until_historical_negatives_are_resolved()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await AddPersistedMovementAsync(environment, Draft(1, PostingDate, 1, -5, "MaterialOutSource"));

        var result = await environment.InventoryPolicySettingsService.UpdateAsync(allowNegativeStock: false);

        Assert.False(result.Success);
        Assert.Contains("Resolve the 1 historical negative", result.Message, StringComparison.Ordinal);
        await using var db = environment.CreateDbContext();
        Assert.True(await db.Companies.Where(x => x.Id == 1).Select(x => x.AllowNegativeStock).SingleAsync());
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityType == "CompanyInventoryPolicy" && x.Action == "BlockRejected" && !x.Success));
    }

    [Fact]
    public async Task Clean_company_can_enable_blocking_and_the_change_is_audited()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var result = await environment.InventoryPolicySettingsService.UpdateAsync(allowNegativeStock: false);

        Assert.True(result.Success);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.Companies.Where(x => x.Id == 1).Select(x => x.AllowNegativeStock).SingleAsync());
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityType == "CompanyInventoryPolicy" && x.Action == "Update" && x.Success));
    }

    [Fact]
    public async Task Competing_issues_are_serialized_and_the_second_cannot_over_consume()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetPolicyAsync(environment, allow: false);
        await AddVoucherAsync(environment, 2, "MO-2");
        await AddVoucherAsync(environment, 3, "MO-3");
        await AddPersistedMovementAsync(environment, Draft(1, PostingDate, 1, 10, "OpeningStockInward"));

        await using var firstDb = environment.CreateDbContext();
        await using var secondDb = environment.CreateDbContext();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var firstPosting = new StockPostingService();
        var secondPosting = new StockPostingService();
        firstPosting.Post(firstDb, Draft(2, PostingDate, 1, -6, "MaterialOutSource"), "First", DateTimeOffset.UtcNow);
        secondPosting.Post(secondDb, Draft(3, PostingDate, 1, -6, "MaterialOutSource"), "Second", DateTimeOffset.UtcNow);

        await firstPosting.EnsureNegativeStockPolicyAsync(firstDb, 1);
        var secondCheck = secondPosting.EnsureNegativeStockPolicyAsync(secondDb, 1);
        await Task.Delay(150);
        Assert.False(secondCheck.IsCompleted);
        await firstTransaction.CommitAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => secondCheck);
        Assert.Contains("short by 2 PCS", error.Message, StringComparison.Ordinal);
        await secondTransaction.RollbackAsync();
    }

    private static async Task SetPolicyAsync(PostgreSqlTestEnvironment environment, bool allow)
    {
        await using var db = environment.CreateDbContext();
        await db.Companies.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.AllowNegativeStock, allow));
    }

    private static async Task AddVoucherAsync(PostgreSqlTestEnvironment environment, long id, string number)
    {
        await using var db = environment.CreateDbContext();
        db.Vouchers.Add(new Voucher
        {
            Id = id,
            CompanyId = 1,
            FinancialYearId = 1,
            VoucherTypeId = 1,
            SequenceNumber = (int)id,
            VoucherNumber = number,
            VoucherNumberNormalized = number,
            VoucherDate = PostingDate,
            Status = VoucherLifecycleService.OpenStatus,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ModifiedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddPersistedMovementAsync(
        PostgreSqlTestEnvironment environment,
        StockMovementDraft draft)
    {
        await using var db = environment.CreateDbContext();
        db.StockMovements.Add(new StockMovement
        {
            CompanyId = draft.CompanyId,
            FinancialYearId = draft.FinancialYearId,
            VoucherId = draft.VoucherId,
            MovementDate = draft.MovementDate,
            StockItemId = draft.StockItemId,
            StockItemVariantId = draft.StockItemVariantId,
            UqcId = draft.UqcId,
            GodownId = draft.GodownId,
            QuantityChange = draft.QuantityChange,
            Rate = draft.Rate,
            ValueChange = draft.ValueChange,
            MovementKind = draft.MovementKind,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "Test"
        });
        await db.SaveChangesAsync();
    }

    private static StockMovementDraft Draft(
        long voucherId,
        DateOnly date,
        long godownId,
        decimal quantity,
        string kind,
        long? variantId = 1) => new(
            CompanyId: 1,
            FinancialYearId: 1,
            VoucherId: voucherId,
            MovementDate: date,
            StockItemId: 1,
            UqcId: 1,
            GodownId: godownId,
            QuantityChange: quantity,
            Rate: 1,
            ValueChange: quantity,
            MovementKind: kind,
            StockItemVariantId: variantId);
}
