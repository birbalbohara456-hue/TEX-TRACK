using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class VoucherSequenceAllocatorTests
{
    [Fact]
    public async Task Never_reset_sequence_continues_across_financial_years()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        VoucherType type;
        await using (var setup = environment.CreateDbContext())
        {
            type = await setup.VoucherTypes.SingleAsync(x => x.Id == 1);
            type.SystemTypeCode = "JOB_WORK_OUT_ORDER";
            type.ResetPeriod = "Never";
            setup.FinancialYears.Add(new FinancialYear
            {
                Id = 2, CompanyId = 1, Name = "2027-28",
                StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2028, 3, 31),
                IsActive = false, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            await setup.SaveChangesAsync();
        }

        await using (var firstDb = environment.CreateDbContext())
        await using (var firstTx = await firstDb.Database.BeginTransactionAsync())
        {
            var first = await new VoucherSequenceAllocator(CreateCompanyContext(1))
                .ReserveAsync(firstDb, type, "Sequence test", now);
            Assert.Equal(2, first);
            await firstTx.CommitAsync();
        }

        await using (var secondDb = environment.CreateDbContext())
        await using (var secondTx = await secondDb.Database.BeginTransactionAsync())
        {
            var second = await new VoucherSequenceAllocator(CreateCompanyContext(2))
                .ReserveAsync(secondDb, type, "Sequence test", now);
            Assert.Equal(3, second);
            await secondTx.CommitAsync();
        }

        await using var verify = environment.CreateDbContext();
        var rows = await verify.Database.SqlQueryRaw<SequenceRow>(
                "SELECT financial_year_id AS \"FinancialYearId\", last_number AS \"LastNumber\" FROM voucher_sequences WHERE company_id = 1 AND voucher_type_id = 1")
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(1, row.FinancialYearId);
        Assert.Equal(3, row.LastNumber);
    }

    [Fact]
    public async Task Twenty_simultaneous_reservations_are_unique_and_gap_free()
    {
        var source = new NpgsqlConnectionStringBuilder(TestDatabaseSettings.GetConnectionString()) { SearchPath = string.Empty };
        var schema = $"sequence_allocator_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(source.ConnectionString);
        await admin.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            SET search_path TO "{schema}";
            -- The allocator also reads the existing voucher high-water mark.
            CREATE TABLE vouchers
            (
                company_id bigint NOT NULL,
                financial_year_id bigint NOT NULL,
                voucher_type_id bigint NOT NULL,
                sequence_number integer NOT NULL
            );
            CREATE TABLE voucher_sequences
            (
                company_id bigint NOT NULL,
                financial_year_id bigint NOT NULL,
                voucher_type_id bigint NOT NULL,
                last_number integer NOT NULL,
                modified_at_utc timestamptz NOT NULL,
                modified_by varchar(100) NOT NULL,
                PRIMARY KEY(company_id, financial_year_id, voucher_type_id)
            );
            """, admin)) await setup.ExecuteNonQueryAsync();

        try
        {
            source.SearchPath = schema;
            var options = new DbContextOptionsBuilder<TexTrackDbContext>().UseNpgsql(source.ConnectionString).Options;
            var context = CreateCompanyContext();
            var allocator = new VoucherSequenceAllocator(context);
            var type = new VoucherType { Id = 7, StartingNumber = 1 };
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var db = new TexTrackDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                var reserved = await allocator.ReserveAsync(db, type, context.Actor, DateTimeOffset.UtcNow);
                await transaction.CommitAsync();
                return reserved;
            });

            var numbers = await Task.WhenAll(tasks);
            Assert.Equal(Enumerable.Range(1, 20), numbers.Order());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static CurrentCompanyContext CreateCompanyContext(long financialYearId = 1)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "9001"),
            new Claim(ClaimTypes.Name, "Concurrency Test"),
            new Claim(TexTrackClaimTypes.CompanyId, "1"),
            new Claim(TexTrackClaimTypes.CompanyName, "Test Company"),
            new Claim(TexTrackClaimTypes.FinancialYearId, financialYearId.ToString()),
            new Claim(TexTrackClaimTypes.FinancialYearName, financialYearId == 1 ? "2026-27" : "2027-28")
        ], "Test");
        return new CurrentCompanyContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        }, new BusinessRuleTestSessionValidator());
    }

    private sealed record SequenceRow(long FinancialYearId, int LastNumber);
}
