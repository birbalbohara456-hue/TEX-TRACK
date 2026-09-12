using System.Data;
using System.Security.Claims;
using Npgsql;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class DatabaseMaintenanceService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    DeveloperAccessService developerAccess,
    UserIdentityService identities,
    IHttpContextAccessor httpContextAccessor)
{
    public const string ConfirmationPhrase = "CLEAR TEXTRACK DATABASE";

    public async Task<OperationResult> ClearAllBusinessDataAsync(
        string confirmation,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!developerAccess.IsDeveloper)
            return OperationResult.Fail("Developer authentication is required.");
        if (!await identities.VerifyCurrentUserPasswordAsync(
                httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(), password, cancellationToken))
            return OperationResult.Fail("Developer password is incorrect.");
        if (!string.Equals(confirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
            return OperationResult.Fail($"Type {ConfirmationPhrase} exactly to continue.");

        var connectionString = configuration.GetConnectionString("TexTrackDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            return OperationResult.Fail("The TexTrack database connection is unavailable.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var tableNames = new List<string>();
            await using (var list = new NpgsqlCommand("""
                SELECT tablename FROM pg_tables
                WHERE schemaname = 'public'
                  AND tablename NOT IN
                  (
                      'schema_versions',
                      'audit_logs',
                      'voucher_audit_revisions',
                      'application_users',
                      'security_roles',
                      'application_user_roles',
                      'application_user_companies'
                  )
                ORDER BY tablename
                """, connection, transaction))
            await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) tableNames.Add(reader.GetString(0));

            if (tableNames.Count > 0)
            {
                var quoted = string.Join(", ", tableNames.Select(x => $"\"{x.Replace("\"", "\"\"")}\""));
                await using var truncate = new NpgsqlCommand($"TRUNCATE TABLE {quoted} RESTART IDENTITY CASCADE", connection, transaction);
                truncate.CommandTimeout = 120;
                await truncate.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecuteSeedSectionAsync(connection, transaction, "001_initial_foundation.sql", "INSERT INTO companies", cancellationToken);
            await ExecuteSeedSectionAsync(connection, transaction, "002_ledger_master.sql", "INSERT INTO ledgers", cancellationToken);
            await ExecuteSeedSectionAsync(connection, transaction, "023_production_chain_integrity.sql", "INSERT INTO stock_groups", cancellationToken);
            await ExecuteSeedSectionAsync(connection, transaction,
                "031_native_inventory_inward_foundation.sql",
                "-- RESET-SEED: native inward voucher types",
                cancellationToken,
                "-- RESET-SEED-END");

            // Company rows are business data and are recreated by the foundation
            // seed. TRUNCATE ... CASCADE therefore removes the old memberships,
            // while the identity and role tables above deliberately survive.
            // Reattach every active identity to the newly-created companies; the
            // first company becomes its one default membership.
            await using (var restoreMemberships = new NpgsqlCommand("""
                INSERT INTO application_user_companies(user_id, company_id, is_default, is_active)
                SELECT u.id,
                       c.id,
                       c.id = (SELECT MIN(c2.id) FROM companies c2 WHERE c2.is_active),
                       true
                FROM application_users u
                CROSS JOIN companies c
                WHERE u.is_active AND c.is_active
                ON CONFLICT (user_id, company_id) DO UPDATE
                SET is_active = EXCLUDED.is_active,
                    is_default = EXCLUDED.is_default
                """, connection, transaction))
                await restoreMemberships.ExecuteNonQueryAsync(cancellationToken);

            var actor = httpContextAccessor.HttpContext?.User.Identity?.Name ?? "Developer";
            await using (var audit = new NpgsqlCommand("""
                INSERT INTO audit_logs
                    (company_id, entity_type, entity_id, action, success, description, performed_by, performed_at_utc)
                VALUES
                    (@company, 'DatabaseMaintenance', NULL, 'ClearAllBusinessData', true,
                     'Cleared TexTrack business data and restored the mandatory system foundation.', @actor, NOW())
                """, connection, transaction))
            {
                var companyClaim = httpContextAccessor.HttpContext?.User.FindFirst(TexTrackClaimTypes.CompanyId)?.Value;
                audit.Parameters.AddWithValue("company",
                    long.TryParse(companyClaim, out var companyId) ? companyId : DBNull.Value);
                audit.Parameters.AddWithValue("actor", actor);
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok("All TexTrack business data was cleared. The audit trail, user identities, security roles and Developer access were preserved, and the mandatory system foundation was restored.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail($"Nothing was cleared. The database reset was rolled back: {ex.GetBaseException().Message}");
        }
    }

    private async Task ExecuteSeedSectionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string fileName, string marker, CancellationToken cancellationToken, string? endMarker = null)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "Migrations", fileName);
        var sql = await File.ReadAllTextAsync(path, cancellationToken);
        var start = sql.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) throw new InvalidOperationException($"Seed marker was not found in {fileName}.");
        var seedSql = sql[start..];
        if (!string.IsNullOrWhiteSpace(endMarker))
        {
            var end = seedSql.IndexOf(endMarker, marker.Length, StringComparison.OrdinalIgnoreCase);
            if (end < 0) throw new InvalidOperationException($"Seed end marker was not found in {fileName}.");
            seedSql = seedSql[..end];
        }
        await using var command = new NpgsqlCommand(seedSql, connection, transaction) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
