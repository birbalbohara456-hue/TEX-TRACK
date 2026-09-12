using Npgsql;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class FifoDormantFoundationTests
{
    [Fact]
    public async Task Migration_preserves_legacy_movement_values_and_creates_no_fifo_evidence()
    {
        await using var schema = await MigrationSchema.CreateAsync();
        await schema.ApplyMigrationsAsync(41);
        await schema.SeedLegacyMovementFixtureAsync();

        await using (var before = schema.Command("""
            SELECT quantity_change, rate, value_change
            FROM stock_movements
            WHERE movement_kind = 'MaterialOutDestination'
            """))
        await using (var reader = await before.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(10m, reader.GetDecimal(0));
            Assert.Equal(2m, reader.GetDecimal(1));
            Assert.Equal(20m, reader.GetDecimal(2));
        }

        await schema.ApplyMigrationsAsync(42, 41);

        await using var verify = schema.Command("""
            SELECT
                quantity_change = 10,
                rate = 2,
                value_change = 20,
                inventory_posting_id IS NULL,
                movement_line_order IS NULL,
                (SELECT COUNT(*) FROM inventory_postings) = 0,
                (SELECT COUNT(*) FROM inventory_cost_layers) = 0,
                (SELECT COUNT(*) FROM inventory_cost_allocations) = 0,
                (SELECT COUNT(*) FROM inventory_valuation_positions) = 0,
                (SELECT book_method FROM inventory_valuation_settings WHERE company_id = 1)
                    = 'LegacyPersistedValue',
                (SELECT lifecycle_state FROM inventory_valuation_settings WHERE company_id = 1)
                    = 'NotInitialized'
            FROM stock_movements
            WHERE movement_kind = 'MaterialOutDestination'
            """);
        await using var verification = await verify.ExecuteReaderAsync();
        Assert.True(await verification.ReadAsync());
        for (var column = 0; column < verification.FieldCount; column++)
            Assert.True(verification.GetBoolean(column));
    }

    [Fact]
    public async Task Existing_and_future_companies_are_seeded_dormant_without_activating_fifo()
    {
        await using var schema = await MigrationSchema.CreateAsync();
        await schema.ApplyMigrationsAsync(42);

        await using (var insert = schema.Command("""
            INSERT INTO companies
                (name, name_normalized, code, is_active, created_at_utc, modified_at_utc,
                 created_by, modified_by, concurrency_token)
            VALUES
                ('Future FIFO Test Company', 'FUTURE FIFO TEST COMPANY', 'FFT', true,
                 NOW(), NOW(), 'Test', 'Test', md5(random()::text))
            """))
        {
            await insert.ExecuteNonQueryAsync();
        }

        await using var verify = schema.Command("""
            SELECT
                (SELECT COUNT(*) FROM inventory_valuation_settings)
                    = (SELECT COUNT(*) FROM companies),
                (SELECT COUNT(*) FROM inventory_posting_sequences)
                    = (SELECT COUNT(*) FROM companies),
                NOT EXISTS
                    (SELECT 1 FROM inventory_valuation_settings
                     WHERE book_method <> 'LegacyPersistedValue'
                        OR lifecycle_state <> 'NotInitialized'),
                (SELECT COUNT(*) FROM inventory_postings) = 0,
                (SELECT COUNT(*) FROM inventory_cost_layers) = 0,
                (SELECT COUNT(*) FROM inventory_cost_allocations) = 0
            """);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var column = 0; column < reader.FieldCount; column++)
            Assert.True(reader.GetBoolean(column));
    }

    [Fact]
    public async Task Database_rejects_mismatched_postings_and_cost_evidence()
    {
        await using var schema = await MigrationSchema.CreateAsync();
        await schema.ApplyMigrationsAsync(42);
        await schema.SeedLegacyMovementFixtureAsync();

        var mismatchedPosting = schema.Command("""
            INSERT INTO inventory_postings
                (operation_id, company_id, financial_year_id, voucher_id, effective_date,
                 posting_order, event_kind, algorithm_version, created_at_utc, created_by)
            SELECT gen_random_uuid(), company_id, financial_year_id, id,
                   voucher_date + 1, 1, 'Original', 1, NOW(), 'Test'
            FROM vouchers WHERE voucher_number_normalized = 'FIFO-FOUNDATION-TEST'
            """);
        var postingError = await Assert.ThrowsAsync<PostgresException>(() =>
            mismatchedPosting.ExecuteNonQueryAsync());
        Assert.Contains("must match its voucher", postingError.MessageText, StringComparison.Ordinal);

        await using (var seedLayered = schema.Command("""
            INSERT INTO inventory_postings
                (operation_id, company_id, financial_year_id, voucher_id, effective_date,
                 posting_order, event_kind, algorithm_version, created_at_utc, created_by)
            SELECT '10000000-0000-0000-0000-000000000001', company_id, financial_year_id, id,
                   voucher_date, 1, 'Original', 1, NOW(), 'Test'
            FROM vouchers WHERE voucher_number_normalized = 'FIFO-FOUNDATION-TEST';

            INSERT INTO stock_movements
                (company_id, financial_year_id, voucher_id, movement_date, stock_item_id,
                 uqc_id, godown_id, quantity_change, rate, value_change, movement_kind,
                 created_at_utc, created_by, inventory_posting_id, movement_line_order)
            SELECT v.company_id, v.financial_year_id, v.id, v.voucher_date, si.id,
                   si.uqc_id, g.id, 10, 2, 20, 'MaterialOutDestination', NOW(), 'Test', p.id, 1
            FROM vouchers v
            JOIN inventory_postings p ON p.voucher_id = v.id
            CROSS JOIN (SELECT id, uqc_id FROM stock_items WHERE name_normalized = 'FIFO FOUNDATION ITEM') si
            CROSS JOIN (SELECT id FROM godowns WHERE name_normalized = 'FIFO FOUNDATION GODOWN') g
            WHERE v.voucher_number_normalized = 'FIFO-FOUNDATION-TEST';

            INSERT INTO stock_movements
                (company_id, financial_year_id, voucher_id, movement_date, stock_item_id,
                 uqc_id, godown_id, quantity_change, rate, value_change, movement_kind,
                 created_at_utc, created_by, inventory_posting_id, movement_line_order)
            SELECT v.company_id, v.financial_year_id, v.id, v.voucher_date, si.id,
                   si.uqc_id, g.id, -5, 2, -10, 'MaterialOutSource', NOW(), 'Test', p.id, 2
            FROM vouchers v
            JOIN inventory_postings p ON p.voucher_id = v.id
            CROSS JOIN (SELECT id, uqc_id FROM stock_items WHERE name_normalized = 'FIFO FOUNDATION ITEM') si
            CROSS JOIN (SELECT id FROM godowns WHERE name_normalized = 'FIFO FOUNDATION GODOWN') g
            WHERE v.voucher_number_normalized = 'FIFO-FOUNDATION-TEST';

            INSERT INTO inventory_cost_layers
                (company_id, receipt_movement_id, layer_sequence, stock_item_id, uqc_id,
                 godown_id, origin_kind, original_quantity, original_value, unit_cost,
                 remaining_quantity, remaining_value, effective_date, posting_order,
                 movement_line_order, algorithm_version, created_at_utc, created_by)
            SELECT sm.company_id, sm.id, 1, sm.stock_item_id, sm.uqc_id, sm.godown_id,
                   'Purchase', 10, 20, 2, 10, 20, sm.movement_date, 1,
                   sm.movement_line_order, 1, NOW(), 'Test'
            FROM stock_movements sm WHERE sm.inventory_posting_id IS NOT NULL AND sm.quantity_change > 0;
            """))
        {
            await seedLayered.ExecuteNonQueryAsync();
        }

        var wrongSnapshot = schema.Command("""
            INSERT INTO inventory_cost_allocations
                (operation_id, company_id, outward_movement_id, source_layer_id,
                 allocation_sequence, allocated_quantity, allocated_value, unit_cost_snapshot,
                 created_at_utc, created_by, algorithm_version)
            SELECT p.operation_id, sm.company_id, sm.id, layer.id,
                   1, 5, 10, 3, NOW(), 'Test', 1
            FROM stock_movements sm
            JOIN inventory_postings p ON p.id = sm.inventory_posting_id
            CROSS JOIN inventory_cost_layers layer
            WHERE sm.quantity_change < 0
            """);
        var snapshotError = await Assert.ThrowsAsync<PostgresException>(() =>
            wrongSnapshot.ExecuteNonQueryAsync());
        Assert.Contains("exact stock position", snapshotError.MessageText, StringComparison.Ordinal);

        var wrongSign = schema.Command("""
            INSERT INTO stock_movements
                (company_id, financial_year_id, voucher_id, movement_date, stock_item_id,
                 uqc_id, godown_id, quantity_change, rate, value_change, movement_kind,
                 created_at_utc, created_by, inventory_posting_id, movement_line_order)
            SELECT sm.company_id, sm.financial_year_id, sm.voucher_id, sm.movement_date,
                   sm.stock_item_id, sm.uqc_id, sm.godown_id, -1, 2, 2,
                   'MaterialOutSource', NOW(), 'Test', sm.inventory_posting_id, 3
            FROM stock_movements sm WHERE sm.inventory_posting_id IS NOT NULL LIMIT 1
            """);
        var signError = await Assert.ThrowsAsync<PostgresException>(() => wrongSign.ExecuteNonQueryAsync());
        Assert.Contains("value sign", signError.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_position_uniqueness_treats_the_base_variant_as_one_position()
    {
        await using var schema = await MigrationSchema.CreateAsync();
        await schema.ApplyMigrationsAsync(42);
        await schema.SeedLegacyMovementFixtureAsync();

        await using (var first = schema.Command("""
            INSERT INTO inventory_valuation_positions
                (company_id, stock_item_id, uqc_id, godown_id, state,
                 modified_at_utc, modified_by, concurrency_token)
            SELECT 1, si.id, si.uqc_id, g.id, 'Legacy', NOW(), 'Test', md5(random()::text)
            FROM stock_items si
            CROSS JOIN godowns g
            WHERE si.name_normalized = 'FIFO FOUNDATION ITEM'
              AND g.name_normalized = 'FIFO FOUNDATION GODOWN'
            """))
        {
            await first.ExecuteNonQueryAsync();
        }

        var duplicate = schema.Command("""
            INSERT INTO inventory_valuation_positions
                (company_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id, state,
                 modified_at_utc, modified_by, concurrency_token)
            SELECT 1, si.id, NULL, si.uqc_id, g.id, 'Legacy', NOW(), 'Test', md5(random()::text)
            FROM stock_items si
            CROSS JOIN godowns g
            WHERE si.name_normalized = 'FIFO FOUNDATION ITEM'
              AND g.name_normalized = 'FIFO FOUNDATION GODOWN'
            """);
        var duplicateError = await Assert.ThrowsAsync<PostgresException>(() =>
            duplicate.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateError.SqlState);
    }

    private sealed class MigrationSchema : IAsyncDisposable
    {
        private readonly NpgsqlConnection admin;
        private readonly string schemaName;

        private MigrationSchema(NpgsqlConnection admin, NpgsqlConnection connection, string schemaName)
        {
            this.admin = admin;
            Connection = connection;
            this.schemaName = schemaName;
        }

        private NpgsqlConnection Connection { get; }

        public static async Task<MigrationSchema> CreateAsync()
        {
            var source = new NpgsqlConnectionStringBuilder(TestDatabaseSettings.GetConnectionString())
            {
                SearchPath = string.Empty
            };
            var admin = new NpgsqlConnection(source.ConnectionString);
            await admin.OpenAsync();
            var schemaName = $"fifo_foundation_{Guid.NewGuid():N}";
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", admin))
                await create.ExecuteNonQueryAsync();

            var test = new NpgsqlConnectionStringBuilder(source.ConnectionString) { SearchPath = schemaName };
            var connection = new NpgsqlConnection(test.ConnectionString);
            await connection.OpenAsync();
            return new MigrationSchema(admin, connection, schemaName);
        }

        public NpgsqlCommand Command(string sql) => new(sql, Connection) { CommandTimeout = 120 };

        public async Task ApplyMigrationsAsync(int count, int skip = 0)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
            var migrations = Directory.GetFiles(directory, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Skip(skip)
                .Take(count - skip);
            foreach (var migration in migrations)
            {
                await using var command = Command(await File.ReadAllTextAsync(migration));
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task SeedLegacyMovementFixtureAsync()
        {
            await using var command = Command("""
                INSERT INTO stock_groups
                    (company_id, name, name_normalized, alias, root_classification,
                     is_system, is_active, created_at_utc, modified_at_utc,
                     created_by, modified_by, concurrency_token)
                VALUES
                    (1, 'FIFO Foundation Group', 'FIFO FOUNDATION GROUP', '', 'RawMaterial',
                     false, true, NOW(), NOW(), 'Test', 'Test', md5(random()::text));

                INSERT INTO uqcs
                    (company_id, name, name_normalized, alias, short_name, decimal_places,
                     is_system, is_active, created_at_utc, modified_at_utc,
                     created_by, modified_by, concurrency_token)
                VALUES
                    (1, 'FIFO Foundation Units', 'FIFO FOUNDATION UNITS', '', 'FFU', 4,
                     false, true, NOW(), NOW(), 'Test', 'Test', md5(random()::text));

                INSERT INTO godowns
                    (company_id, name, name_normalized, alias, address_line1, address_line2,
                     city, state, is_system, is_active, created_at_utc, modified_at_utc,
                     created_by, modified_by, concurrency_token)
                VALUES
                    (1, 'FIFO Foundation Godown', 'FIFO FOUNDATION GODOWN', '', '', '', '', '',
                     false, true, NOW(), NOW(), 'Test', 'Test', md5(random()::text));

                INSERT INTO stock_items
                    (company_id, name, name_normalized, alias, stock_group_id, uqc_id,
                     tax_mode, hsn_code, is_system, is_active, created_at_utc, modified_at_utc,
                     created_by, modified_by, concurrency_token)
                SELECT 1, 'FIFO Foundation Item', 'FIFO FOUNDATION ITEM', '', sg.id, u.id,
                       'NotApplicable', '', false, true, NOW(), NOW(), 'Test', 'Test', md5(random()::text)
                FROM stock_groups sg CROSS JOIN uqcs u
                WHERE sg.name_normalized = 'FIFO FOUNDATION GROUP'
                  AND u.name_normalized = 'FIFO FOUNDATION UNITS';

                INSERT INTO vouchers
                    (company_id, financial_year_id, voucher_type_id, sequence_number,
                     voucher_number, voucher_number_normalized, voucher_date,
                     status, created_at_utc, modified_at_utc, created_by, modified_by, concurrency_token)
                SELECT 1, 1, vt.id, 990001, 'FIFO-FOUNDATION-TEST', 'FIFO-FOUNDATION-TEST',
                       DATE '2026-04-01', 'Open', NOW(), NOW(), 'Test', 'Test', md5(random()::text)
                FROM voucher_types vt WHERE vt.company_id = 1 ORDER BY vt.id LIMIT 1;

                INSERT INTO stock_movements
                    (company_id, financial_year_id, voucher_id, movement_date, stock_item_id,
                     uqc_id, godown_id, quantity_change, rate, value_change, movement_kind,
                     created_at_utc, created_by)
                SELECT 1, 1, v.id, v.voucher_date, si.id, si.uqc_id, g.id,
                       10, 2, 20, 'MaterialOutDestination', NOW(), 'Test'
                FROM vouchers v
                CROSS JOIN stock_items si
                CROSS JOIN godowns g
                WHERE v.voucher_number_normalized = 'FIFO-FOUNDATION-TEST'
                  AND si.name_normalized = 'FIFO FOUNDATION ITEM'
                  AND g.name_normalized = 'FIFO FOUNDATION GODOWN';
                """);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
    }
}
