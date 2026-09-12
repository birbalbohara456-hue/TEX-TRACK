using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class InventoryPeriodControlTests
{
    private static readonly DateOnly FreezeDate = new(2026, 6, 30);

    [Fact]
    public async Task Inclusive_boundary_blocks_dates_through_freeze_and_allows_next_day()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetFreezeAsync(environment, FreezeDate);
        await using var db = environment.CreateDbContext();

        var before = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryPeriodControlService.EnsurePostingDateIsOpenAsync(
                db, 1, FreezeDate.AddDays(-1), "Material Out", CancellationToken.None));
        var boundary = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryPeriodControlService.EnsurePostingDateIsOpenAsync(
                db, 1, FreezeDate, "Material Out", CancellationToken.None));

        Assert.Contains("frozen through 30-Jun-2026", before.Message, StringComparison.Ordinal);
        Assert.Contains("Use 01-Jul-2026 or a later date", boundary.Message, StringComparison.Ordinal);
        await environment.InventoryPeriodControlService.EnsurePostingDateIsOpenAsync(
            db, 1, FreezeDate.AddDays(1), "Material Out", CancellationToken.None);
    }

    [Fact]
    public async Task Closed_original_date_cannot_be_hidden_by_amending_voucher_into_open_period()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetFreezeAsync(environment, FreezeDate);
        await using var db = environment.CreateDbContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.InventoryPeriodControlService.EnsureVoucherMutationIsOpenAsync(
                db, 1, FreezeDate, FreezeDate.AddDays(10), "Material In MI-1", CancellationToken.None));

        Assert.StartsWith("The original Material In MI-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Native_material_out_and_material_in_creation_use_the_shared_period_gate()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await SetFreezeAsync(environment, FreezeDate);

        var moError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.MaterialOutRepository.SaveAsync(new MaterialOutSaveRequest
            {
                VoucherDate = FreezeDate
            }, CancellationToken.None));
        var miError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.MaterialInRepository.SaveAsync(new MaterialInSaveRequest
            {
                VoucherDate = FreezeDate
            }, CancellationToken.None));

        Assert.StartsWith("Material Out is dated", moError.Message, StringComparison.Ordinal);
        Assert.StartsWith("Material In is dated", miError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closed_existing_material_vouchers_cannot_be_altered_cancelled_or_deleted()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var tokens = await SeedMaterialVouchersAsync(environment);
        await SetFreezeAsync(environment, FreezeDate);

        var moAlter = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.MaterialOutRepository.UpdateAsync(new MaterialOutSaveRequest
            {
                VoucherId = 2,
                VoucherDate = FreezeDate.AddDays(1),
                ConcurrencyToken = tokens.MaterialOut
            }, CancellationToken.None));
        var miAlter = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            environment.MaterialInRepository.SaveAsync(new MaterialInSaveRequest
            {
                VoucherId = 3,
                VoucherDate = FreezeDate.AddDays(1),
                JwoVoucherId = 1,
                ConcurrencyToken = tokens.MaterialIn
            }, CancellationToken.None));

        var moCancel = await environment.MaterialOutRepository.CancelAsync(2, "Period-control test", CancellationToken.None);
        var moDelete = await environment.MaterialOutRepository.DeleteAsync(2, CancellationToken.None);
        var miCancel = await environment.MaterialInRepository.CancelAsync(3, "Period-control test", CancellationToken.None);
        var miDelete = await environment.MaterialInRepository.DeleteAsync(3, CancellationToken.None);

        Assert.Contains("original Material Out MO-1", moAlter.Message, StringComparison.Ordinal);
        Assert.Contains("original Material In MI-1", miAlter.Message, StringComparison.Ordinal);
        Assert.All(new[] { moCancel, moDelete, miCancel, miDelete }, result =>
        {
            Assert.False(result.Success);
            Assert.Contains("stock is frozen through 30-Jun-2026", result.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Null_freeze_preserves_existing_open_period_behaviour()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await using var db = environment.CreateDbContext();

        await environment.InventoryPeriodControlService.EnsurePostingDateIsOpenAsync(
            db, 1, new DateOnly(1900, 1, 1), "Material Out", CancellationToken.None);
    }

    private static async Task SetFreezeAsync(PostgreSqlTestEnvironment environment, DateOnly date)
    {
        await using var db = environment.CreateDbContext();
        await db.Companies.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.StockFrozenThrough, date), CancellationToken.None);
    }

    private static async Task<(string MaterialOut, string MaterialIn)> SeedMaterialVouchersAsync(
        PostgreSqlTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        db.VoucherTypes.AddRange(
            VoucherType(2, "Material Out", "MATERIAL_OUT", "MO", now),
            VoucherType(3, "Material In", "MATERIAL_IN", "MI", now));
        var materialOut = Voucher(2, 2, "MO-1", FreezeDate, now);
        var materialIn = Voucher(3, 3, "MI-1", FreezeDate, now);
        db.Vouchers.AddRange(materialOut, materialIn);
        db.MaterialInDetails.Add(new MaterialInDetail
        {
            VoucherId = 3,
            JwoVoucherId = 1,
            ConsumptionGodownId = 2,
            ReceivingGodownId = 1
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return (materialOut.ConcurrencyToken, materialIn.ConcurrencyToken);
    }

    private static VoucherType VoucherType(
        long id, string name, string systemType, string abbreviation, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        Name = name,
        NameNormalized = name.ToUpperInvariant(),
        SystemTypeCode = systemType,
        Nature = "Inventory",
        PostingMode = "Voucher",
        Abbreviation = abbreviation,
        NumberingMode = "Auto",
        ResetPeriod = "FinancialYear",
        IsActive = true,
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static Voucher Voucher(
        long id, long voucherTypeId, string number, DateOnly date, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        FinancialYearId = 1,
        VoucherTypeId = voucherTypeId,
        SequenceNumber = 1,
        VoucherNumber = number,
        VoucherNumberNormalized = number,
        VoucherDate = date,
        Status = "Open",
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };
}
