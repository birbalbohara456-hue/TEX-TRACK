using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;

namespace TexTrack.Web.Services;

/// <summary>Atomically reserves voucher numbers across every voucher repository.</summary>
public sealed class VoucherSequenceAllocator(CurrentCompanyContext companyContext)
{
    public async Task<int> ReserveAsync(
        TexTrackDbContext db,
        VoucherType voucherType,
        string actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Voucher sequence reservation must run inside the voucher transaction.");

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);

        var sequenceFinancialYearId = companyContext.FinancialYearId;
        var nextFloor = Math.Max(1, voucherType.StartingNumber);
        var resetsNever = string.Equals(voucherType.ResetPeriod, "Never", StringComparison.OrdinalIgnoreCase);
        if (resetsNever)
        {
            sequenceFinancialYearId = await db.FinancialYears.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId)
                .OrderBy(x => x.StartDate).ThenBy(x => x.Id)
                .Select(x => x.Id)
                .FirstAsync(cancellationToken);
            var highestSequence = await GetHighestSequenceAsync(
                db, connection, voucherType.Id, cancellationToken);
            nextFloor = Math.Max(nextFloor, (highestSequence ?? 0) + 1);
        }

        // Reconcile against the actual voucher rows in this scope (company/type, and financial
        // year unless this type never resets), in case any were created by a path that doesn't
        // reserve through this allocator - e.g. TallyXmlExchangeService.NewVoucherAsync computes
        // its own SequenceNumber directly from the Vouchers table and never touches
        // voucher_sequences. Without this, the counter table can drift below what already
        // exists in Vouchers and hand out a number that collides with an existing voucher.
        var highestVoucherQuery = db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.VoucherTypeId == voucherType.Id);
        if (!resetsNever) highestVoucherQuery = highestVoucherQuery.Where(x => x.FinancialYearId == sequenceFinancialYearId);
        var highestVoucherInScope = await highestVoucherQuery.Select(x => (int?)x.SequenceNumber).MaxAsync(cancellationToken);
        nextFloor = Math.Max(nextFloor, (highestVoucherInScope ?? 0) + 1);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO voucher_sequences
                (company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by)
            VALUES (@company, @year, @type, @starting, @modified, @actor)
            ON CONFLICT (company_id, financial_year_id, voucher_type_id)
            DO UPDATE SET last_number = GREATEST(voucher_sequences.last_number + 1, @starting),
                          modified_at_utc = @modified,
                          modified_by = @actor
            RETURNING last_number;
            """;
        AddParameter(command, "@company", companyContext.CompanyId);
        AddParameter(command, "@year", sequenceFinancialYearId);
        AddParameter(command, "@type", voucherType.Id);
        AddParameter(command, "@starting", nextFloor);
        AddParameter(command, "@modified", occurredAtUtc);
        AddParameter(command, "@actor", actor);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<int?> GetHighestSequenceAsync(
        TexTrackDbContext db,
        DbConnection connection,
        long voucherTypeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT MAX(last_number)
            FROM voucher_sequences
            WHERE company_id = @company AND voucher_type_id = @type;
            """;
        AddParameter(command, "@company", companyContext.CompanyId);
        AddParameter(command, "@type", voucherTypeId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull
            ? null
            : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
