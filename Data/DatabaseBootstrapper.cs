using System.Security.Cryptography;
using System.Text;
using Npgsql;
using TexTrack.Web.Services;

namespace TexTrack.Web.Data;

public sealed class DatabaseBootstrapper(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    DatabaseStatus status,
    ILogger<DatabaseBootstrapper> logger)
{
    private const long MigrationLockKey = 0x544558545241434B; // "TEXTRACK"

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("TexTrackDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            status.MarkUnavailable("Connection string 'TexTrackDatabase' is missing.");
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await AcquireMigrationLockAsync(connection, cancellationToken);
            Exception? initializationFailure = null;
            try
            {
                await EnsureVersionTableAsync(connection, cancellationToken);
                var migrationDirectory = Path.Combine(environment.ContentRootPath, "Data", "Migrations");

                if (!Directory.Exists(migrationDirectory))
                {
                    throw new DirectoryNotFoundException($"Migration folder not found: {migrationDirectory}");
                }

                var migrationFiles = Directory
                    .EnumerateFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                ValidateMigrationSequence(migrationFiles);
                // An older package must not migrate or serve an already-upgraded database.
                // Check the database's complete version range while holding the same lock
                // used to apply migrations, before executing any pending migration SQL.
                await EnsureDatabaseVersionSupportedAsync(connection,
                    migrationFiles.Select(ParseVersion).Max(), cancellationToken);

                foreach (var file in migrationFiles)
                {
                    var version = ParseVersion(file);
                    var fileName = Path.GetFileName(file);
                    var sql = await File.ReadAllTextAsync(file, cancellationToken);
                    var checksum = ComputeMigrationChecksum(sql);
                    var applied = await GetAppliedMigrationAsync(connection, version, cancellationToken);
                    if (applied is not null)
                    {
                        if (!string.Equals(applied.FileName, fileName, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(applied.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                        {
                            var sameSqlWithDifferentLineEndings =
                                string.Equals(applied.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                                IsLineEndingEquivalent(applied.Checksum, sql);
                            if (!sameSqlWithDifferentLineEndings &&
                                !MigrationCompatibilityManifest.IsApprovedEquivalent(
                                    version, applied.FileName, applied.Checksum, fileName, checksum))
                            {
                                throw new InvalidOperationException(
                                    $"Migration integrity check failed for version {version:000}. " +
                                    $"Database has '{applied.FileName}' ({applied.Checksum}); application has '{fileName}' ({checksum}). " +
                                    "Applied migrations are immutable. Restore the original migration file or deploy the correct application build.");
                            }

                            logger.LogWarning(
                                "Accepted reviewed legacy migration identity {Version}: {AppliedFile}; current package equivalent is {CurrentFile}. Historical migration metadata was preserved.",
                                version, applied.FileName, fileName);
                        }

                        continue;
                    }

                    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                    await using (var migrationCommand = new NpgsqlCommand(sql, connection, transaction))
                    {
                        migrationCommand.CommandTimeout = 120;
                        await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var versionCommand = new NpgsqlCommand(
                        "INSERT INTO schema_versions(version, file_name, checksum, applied_at_utc) VALUES (@version, @file, @checksum, NOW())",
                        connection,
                        transaction))
                    {
                        versionCommand.Parameters.AddWithValue("version", version);
                        versionCommand.Parameters.AddWithValue("file", fileName);
                        versionCommand.Parameters.AddWithValue("checksum", checksum);
                        await versionCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("Applied TexTrack database migration {Version}: {File}", version, fileName);
                }
            }
            catch (Exception exception)
            {
                initializationFailure = exception;
                throw;
            }
            finally
            {
                try
                {
                    await ReleaseMigrationLockAsync(connection);
                }
                catch (Exception releaseException) when (initializationFailure is not null)
                {
                    // Preserve the original failure, especially fatal incompatibility.
                    // Connection disposal still runs if explicit lock release fails.
                    logger.LogWarning(releaseException, "Migration lock cleanup failed after initialization failed.");
                }
            }

            status.MarkConnected("PostgreSQL connected. Database migrations are current.");
        }
        catch (DatabaseVersionCompatibilityException exception)
        {
            logger.LogCritical(exception, "TexTrack startup stopped: incompatible database version.");
            status.MarkUnavailable(exception.Message);
            // Program awaits InitializeAsync before starting the web host. Do not let
            // an incompatible executable reach login or voucher/report endpoints.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "TexTrack database initialization failed.");
            status.MarkUnavailable(BuildFriendlyMessage(exception));
        }
    }

    private static string ComputeMigrationChecksum(string sql) =>
        HashSql(NormalizeLineEndings(sql));

    private static bool IsLineEndingEquivalent(string appliedChecksum, string currentSql)
    {
        var normalized = NormalizeLineEndings(currentSql);
        return new[]
            {
                HashSql(normalized),
                HashSql(normalized.Replace("\n", "\r\n", StringComparison.Ordinal)),
                HashSql(normalized.Replace("\n", "\r", StringComparison.Ordinal))
            }
            .Any(x => string.Equals(x, appliedChecksum, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    private static string HashSql(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    private static async Task EnsureDatabaseVersionSupportedAsync(
        NpgsqlConnection connection, int packagedVersion, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT version FROM schema_versions WHERE version < 1 OR version > @packagedVersion ORDER BY version DESC LIMIT 1",
            connection);
        command.Parameters.AddWithValue("packagedVersion", packagedVersion);
        if (await command.ExecuteScalarAsync(cancellationToken) is int unsupportedVersion)
            throw new DatabaseVersionCompatibilityException(
                $"This database contains migration version {unsupportedVersion:000}, which this TexTrack build does not support " +
                $"(packaged through {packagedVersion:000}). Startup has been stopped to protect existing data. " +
                "Use the matching or newer compatible TexTrack build. Do not rerun database setup, " +
                "delete migration history, or attempt to downgrade this database.");
    }

    private static async Task EnsureVersionTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_versions
            (
                version integer PRIMARY KEY,
                file_name varchar(255) NOT NULL,
                checksum varchar(64) NOT NULL,
                applied_at_utc timestamp with time zone NOT NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AppliedMigration?> GetAppliedMigrationAsync(
        NpgsqlConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT file_name, checksum FROM schema_versions WHERE version = @version",
            connection);
        command.Parameters.AddWithValue("version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AppliedMigration(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task AcquireMigrationLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", MigrationLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseMigrationLockAsync(NpgsqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open) return;
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", MigrationLockKey);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record AppliedMigration(string FileName, string Checksum);

    private static int ParseVersion(string file)
    {
        var prefix = Path.GetFileName(file).Split('_', 2)[0];
        return int.TryParse(prefix, out var version)
            ? version
            : throw new InvalidOperationException($"Migration file must start with a number: {file}");
    }

    internal static void ValidateMigrationSequence(IReadOnlyList<string> migrationFiles)
    {
        if (migrationFiles.Count == 0)
            throw new InvalidOperationException("No database migrations were packaged with the application.");

        var versions = migrationFiles.Select(ParseVersion).ToList();
        var duplicate = versions.GroupBy(x => x).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate database migration version {duplicate.Key:000} was packaged.");

        for (var expected = 1; expected <= versions.Count; expected++)
        {
            if (!versions.Contains(expected))
                throw new InvalidOperationException($"Database migration {expected:000} is missing from the application package.");
        }
    }

    private static string BuildFriendlyMessage(Exception exception)
    {
        var detail = exception.GetBaseException().Message;
        return $"PostgreSQL is not ready. Run 1_SETUP_POSTGRESQL_DATABASE.bat, then restart TexTrack. Details: {detail}";
    }
}

/// <summary>A fatal startup incompatibility, not a database-setup or connectivity error.</summary>
public sealed class DatabaseVersionCompatibilityException(string message) : InvalidOperationException(message);
