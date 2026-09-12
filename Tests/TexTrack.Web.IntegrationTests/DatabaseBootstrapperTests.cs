using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TexTrack.Web.Data;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class DatabaseBootstrapperTests
{
    [Fact]
    public async Task Older_package_stops_startup_and_preserves_newer_schema_history_and_data()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        var futureFile = Path.Combine(environment.MigrationsPath, "043_future_test.sql");
        await File.WriteAllTextAsync(futureFile,
            "CREATE TABLE future_data(id integer PRIMARY KEY, note text); INSERT INTO future_data VALUES (1, 'preserve me');");
        Assert.True((await environment.RunAsync()).IsConnected);
        var identity = await environment.GetMigrationIdentityAsync(43);

        // Simulate running the old executable's package against the upgraded schema.
        File.Delete(futureFile);
        var status = new DatabaseStatus();
        status.MarkConnected("Previously ready");
        var error = await Assert.ThrowsAsync<DatabaseVersionCompatibilityException>(() => environment.RunAsync(status));

        Assert.False(status.IsConnected);
        Assert.Contains("043", error.Message);
        Assert.Contains("042", error.Message);
        Assert.Contains("Startup has been stopped", status.Message);
        Assert.DoesNotContain("Run 1_SETUP", status.Message);
        Assert.Equal(43, await environment.VersionCountAsync());
        Assert.Equal(identity, await environment.GetMigrationIdentityAsync(43));
        Assert.Equal(1, await environment.CountFutureDataAsync());
        Assert.True(await environment.MigrationLockIsAvailableAsync());

        // A correctly packaged build can still open the same database afterwards.
        await File.WriteAllTextAsync(futureFile,
            "CREATE TABLE future_data(id integer PRIMARY KEY, note text); INSERT INTO future_data VALUES (1, 'preserve me');");
        Assert.True((await environment.RunAsync()).IsConnected);
        Assert.Equal(1, await environment.CountFutureDataAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task Unsupported_history_is_rejected_before_a_pending_migration_executes(int unsupportedVersion)
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        Assert.True((await environment.RunAsync()).IsConnected);
        await environment.InsertVersionAsync(unsupportedVersion);
        await File.WriteAllTextAsync(Path.Combine(environment.MigrationsPath, "043_must_not_execute.sql"),
            "CREATE TABLE forbidden_pending_effect(id integer);");

        var status = new DatabaseStatus();
        await Assert.ThrowsAsync<DatabaseVersionCompatibilityException>(() => environment.RunAsync(status));

        Assert.False(status.IsConnected);
        Assert.False(await environment.RelationExistsAsync("forbidden_pending_effect"));
        Assert.Equal(43, await environment.VersionCountAsync());
        Assert.Equal(("unsupported_test.sql", new string('A', 64)), await environment.GetMigrationIdentityAsync(unsupportedVersion));
    }

    [Fact]
    public async Task Supported_older_database_still_upgrades_forward_normally()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        Assert.True((await environment.RunAsync()).IsConnected);
        await File.WriteAllTextAsync(Path.Combine(environment.MigrationsPath, "043_forward_test.sql"),
            "CREATE TABLE forward_effect(id integer);");

        var upgraded = await environment.RunAsync();

        Assert.True(upgraded.IsConnected, upgraded.Message);
        Assert.Equal(43, await environment.VersionCountAsync());
        Assert.True(await environment.RelationExistsAsync("forward_effect"));
        Assert.True((await environment.RunAsync()).IsConnected);
        Assert.Equal(43, await environment.VersionCountAsync());
    }

    [Fact]
    public async Task Bootstrapper_applies_once_and_rejects_a_modified_applied_migration()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        var first = await environment.RunAsync();
        Assert.True(first.IsConnected, first.Message);
        Assert.Equal(42, await environment.VersionCountAsync());

        var second = await environment.RunAsync();
        Assert.True(second.IsConnected, second.Message);
        Assert.Equal(42, await environment.VersionCountAsync());

        var migration = Directory.GetFiles(environment.MigrationsPath, "001_*.sql").Single();
        await File.AppendAllTextAsync(migration, Environment.NewLine + "-- forbidden mutation");
        var tampered = await environment.RunAsync();
        Assert.False(tampered.IsConnected);
        Assert.Contains("Migration integrity check failed", tampered.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstrapper_rolls_back_failed_migration_and_version_row_together()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(environment.MigrationsPath, "043_intentional_failure.sql"),
            "CREATE TABLE must_rollback(id integer); SELECT 1 / 0;");

        var status = await environment.RunAsync();
        Assert.False(status.IsConnected);
        Assert.Equal(42, await environment.VersionCountAsync());
        Assert.False(await environment.RelationExistsAsync("must_rollback"));
    }

    [Fact]
    public async Task Simultaneous_bootstrappers_are_serialized_and_apply_each_version_once()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        var results = await Task.WhenAll(environment.RunAsync(), environment.RunAsync());
        Assert.All(results, status => Assert.True(status.IsConnected, status.Message));
        Assert.Equal(42, await environment.VersionCountAsync());
        Assert.Equal(42, await environment.DistinctVersionCountAsync());

    }

    [Fact]
    public async Task Bootstrapper_rejects_a_gap_before_executing_any_migration()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        File.Delete(Directory.GetFiles(environment.MigrationsPath, "013_*.sql").Single());

        var status = await environment.RunAsync();

        Assert.False(status.IsConnected);
        Assert.Contains("migration 013 is missing", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await environment.VersionCountAsync());
    }

    [Theory]
    [InlineData(11, "011_jwo_component_rate_material_out_cancel_fix.sql", "1B0FF0E7AC0160DF8C533EDD2F5E1C7FE2CA36C14836DB39CEB85A63469A25CF")]
    [InlineData(14, "014_nature_of_process.sql", "368BEADDA484C4C4558F977D7535B992F22DCD5BE01DA5F772B517EF5E63702C")]
    public async Task Bootstrapper_accepts_reviewed_legacy_identity_without_rewriting_history(
        int version, string legacyFileName, string legacyChecksum)
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        Assert.True((await environment.RunAsync()).IsConnected);
        await environment.ReplaceMigrationIdentityAsync(version, legacyFileName, legacyChecksum);

        var status = await environment.RunAsync();

        Assert.True(status.IsConnected, status.Message);
        Assert.Equal((legacyFileName, legacyChecksum), await environment.GetMigrationIdentityAsync(version));
        Assert.Equal(42, await environment.VersionCountAsync());
    }

    [Fact]
    public async Task Bootstrapper_accepts_the_same_migration_with_a_windows_line_ending_checksum()
    {
        await using var environment = await BootstrapEnvironment.CreateAsync();
        Assert.True((await environment.RunAsync()).IsConnected);
        await environment.ReplaceMigrationIdentityAsync(
            14,
            "014_material_in_stock_movement_kinds.sql",
            "B04E5EAE51E8B89C00EE07A3FA431D43DD518F6BBAD406346AD1D4C71FE28193");

        var status = await environment.RunAsync();

        Assert.True(status.IsConnected, status.Message);
        Assert.Equal(
            ("014_material_in_stock_movement_kinds.sql",
             "B04E5EAE51E8B89C00EE07A3FA431D43DD518F6BBAD406346AD1D4C71FE28193"),
            await environment.GetMigrationIdentityAsync(14));
        Assert.Equal(42, await environment.VersionCountAsync());
    }

    private sealed class BootstrapEnvironment : IAsyncDisposable
    {
        private readonly NpgsqlConnection admin;
        private readonly string schema;
        private readonly string root;
        private readonly IConfiguration configuration;

        private BootstrapEnvironment(NpgsqlConnection admin, string schema, string root, IConfiguration configuration)
        {
            this.admin = admin;
            this.schema = schema;
            this.root = root;
            this.configuration = configuration;
        }

        public string MigrationsPath => Path.Combine(root, "Data", "Migrations");

        public static async Task<BootstrapEnvironment> CreateAsync()
        {
            var source = new NpgsqlConnectionStringBuilder(TestDatabaseSettings.GetConnectionString()) { SearchPath = string.Empty };
            var schema = $"bootstrapper_{Guid.NewGuid():N}";
            var admin = new NpgsqlConnection(source.ConnectionString);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
                await create.ExecuteNonQueryAsync();

            var root = Path.Combine(Path.GetTempPath(), $"textrack-bootstrap-{Guid.NewGuid():N}");
            var target = Path.Combine(root, "Data", "Migrations");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data", "Migrations"), "*.sql"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

            source.SearchPath = schema;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TexTrackDatabase"] = source.ConnectionString
            }).Build();
            return new BootstrapEnvironment(admin, schema, root, configuration);
        }

        public async Task<DatabaseStatus> RunAsync(DatabaseStatus? status = null)
        {
            status ??= new DatabaseStatus();
            var bootstrapper = new DatabaseBootstrapper(
                configuration, new TestHostEnvironment(root), status, NullLogger<DatabaseBootstrapper>.Instance);
            await bootstrapper.InitializeAsync();
            return status;
        }

        public Task<int> VersionCountAsync() => ScalarIntAsync("SELECT COUNT(*) FROM schema_versions");
        public Task<int> CountFutureDataAsync() => ScalarIntAsync("SELECT COUNT(*) FROM future_data WHERE id = 1 AND note = 'preserve me'");

        public async Task InsertVersionAsync(int version)
        {
            await using var command = new NpgsqlCommand(
                $"SET search_path TO \"{schema}\"; INSERT INTO schema_versions(version, file_name, checksum, applied_at_utc) VALUES (@version, 'unsupported_test.sql', @checksum, NOW())", admin);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("checksum", new string('A', 64));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<bool> MigrationLockIsAvailableAsync()
        {
            // A different connection must be able to acquire the lock after failure.
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", admin);
            command.Parameters.AddWithValue("key", 0x544558545241434BL);
            var acquired = (bool)(await command.ExecuteScalarAsync())!;
            if (acquired)
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", admin);
                release.Parameters.AddWithValue("key", 0x544558545241434BL);
                await release.ExecuteScalarAsync();
            }
            return acquired;
        }
        public Task<int> DistinctVersionCountAsync() => ScalarIntAsync("SELECT COUNT(DISTINCT version) FROM schema_versions");
        public async Task ReplaceMigrationIdentityAsync(int version, string fileName, string checksum)
        {
            await using var command = new NpgsqlCommand(
                $"SET search_path TO \"{schema}\"; UPDATE schema_versions SET file_name = @file, checksum = @checksum WHERE version = @version",
                admin);
            command.Parameters.AddWithValue("file", fileName);
            command.Parameters.AddWithValue("checksum", checksum);
            command.Parameters.AddWithValue("version", version);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<(string FileName, string Checksum)> GetMigrationIdentityAsync(int version)
        {
            await using var command = new NpgsqlCommand(
                $"SET search_path TO \"{schema}\"; SELECT file_name, checksum FROM schema_versions WHERE version = @version",
                admin);
            command.Parameters.AddWithValue("version", version);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetString(1));
        }
        public async Task<bool> RelationExistsAsync(string name)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT to_regclass('{schema}.{name}') IS NOT NULL", admin);
            return (bool)(await command.ExecuteScalarAsync() ?? false);
        }

        private async Task<int> ScalarIntAsync(string sql)
        {
            await using var command = new NpgsqlCommand($"SET search_path TO \"{schema}\"; {sql}", admin);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin))
                await drop.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TexTrack.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
