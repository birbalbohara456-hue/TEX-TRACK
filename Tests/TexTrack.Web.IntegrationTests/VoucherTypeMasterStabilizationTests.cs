using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class VoucherTypeMasterStabilizationTests
{
    private static readonly string[] PermanentCodes =
    [
        "SALES", "PURCHASE", "SALES_RETURN", "PURCHASE_RETURN",
        "JOB_WORK_OUT_ORDER", "JOB_WORK_IN_ORDER", "MATERIAL_OUT", "MATERIAL_IN",
        "PAYMENT", "RECEIPT", "CONTRA", "JOURNAL", "STOCK_JOURNAL",
        "PURCHASE_ORDER", "SALES_ORDER", "MASTER_JOB_ORDER"
    ];

    [Fact]
    public async Task Migration_seed_is_idempotent_and_contains_all_approved_permanent_types()
    {
        var sourceConnection = TestDatabaseSettings.GetConnectionString();
        var adminBuilder = new NpgsqlConnectionStringBuilder(sourceConnection) { SearchPath = string.Empty };
        var schema = $"voucher_type_seed_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { SearchPath = schema };
            await using var connection = new NpgsqlConnection(testBuilder.ConnectionString);
            await connection.OpenAsync();
            var migrationPath = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations", "001_initial_foundation.sql");
            var sql = await File.ReadAllTextAsync(migrationPath);
            for (var run = 0; run < 2; run++)
            {
                await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync();
            }

            await using var query = new NpgsqlCommand(
                "SELECT system_type_code FROM voucher_types WHERE company_id=1 AND is_system ORDER BY system_type_code", connection);
            await using var reader = await query.ExecuteReaderAsync();
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            Assert.Equal(PermanentCodes.OrderBy(x => x), actual);
            Assert.Equal(actual.Count, actual.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Create_valid_custom_type_persists_canonical_parent_numbering_and_tally_name()
    {
        await using var environment = await CreateSeededAsync();
        var model = NewCustom("  Subcontract Issue  ", 1);
        model.TallyVoucherTypeName = "  Tally Subcontract  ";
        model.NumberingMode = "AutoPrefixSuffix";
        model.Prefix = "  SCI-  ";
        model.NumberWidth = 4;

        var result = await environment.VoucherTypeRepository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var entity = await db.VoucherTypes.SingleAsync(x => x.Id == result.EntityId);
        Assert.Equal("Subcontract Issue", entity.Name);
        Assert.StartsWith("CUSTOM_", entity.SystemTypeCode);
        Assert.Equal(1, entity.ParentVoucherTypeId);
        Assert.Equal("Tally Subcontract", entity.TallyVoucherTypeName);
        Assert.Equal("SCI-", entity.Prefix);
        Assert.False(entity.IsSystem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_and_whitespace_names_are_rejected_without_writes(string name)
    {
        await using var environment = await CreateSeededAsync();
        await using var beforeDb = environment.CreateDbContext();
        var before = await beforeDb.VoucherTypes.CountAsync();

        var result = await environment.VoucherTypeRepository.SaveAsync(NewCustom(name, 1));

        Assert.False(result.Success);
        Assert.Equal("Voucher Type Name is required.", result.Message);
        await using var afterDb = environment.CreateDbContext();
        Assert.Equal(before, await afterDb.VoucherTypes.CountAsync());
    }

    [Fact]
    public async Task Duplicate_display_name_is_rejected_on_create_and_update()
    {
        await using var environment = await CreateSeededAsync();
        var firstId = await CreateCustomAsync(environment, "Custom Issue", 1);
        var create = await environment.VoucherTypeRepository.SaveAsync(NewCustom(" custom issue ", 1));
        var secondId = await CreateCustomAsync(environment, "Second Custom", 1);
        var second = await environment.VoucherTypeRepository.GetForEditAsync(secondId) ?? throw new InvalidOperationException();
        second.Name = "CUSTOM ISSUE";
        var update = await environment.VoucherTypeRepository.SaveAsync(second);

        Assert.False(create.Success);
        Assert.False(update.Success);
        Assert.Contains("already exists", create.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = environment.CreateDbContext();
        Assert.Equal("Custom Issue", await db.VoucherTypes.Where(x => x.Id == firstId).Select(x => x.Name).SingleAsync());
        Assert.Equal("Second Custom", await db.VoucherTypes.Where(x => x.Id == secondId).Select(x => x.Name).SingleAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99999L)]
    public async Task Missing_or_invalid_parent_is_rejected(long? parentId)
    {
        await using var environment = await CreateSeededAsync();
        var result = await environment.VoucherTypeRepository.SaveAsync(NewCustom("Invalid Parent", parentId));
        Assert.False(result.Success);
        Assert.Contains(parentId is null ? "Select a permanent" : "unavailable", result.Message);
    }

    [Fact]
    public async Task Permanent_identity_system_flag_and_base_mapping_are_immutable_but_tally_name_persists()
    {
        await using var environment = await CreateSeededAsync();
        var model = await environment.VoucherTypeRepository.GetForEditAsync(1) ?? throw new InvalidOperationException();
        model.Name = "Impersonated Name";
        model.IsSystem = false;
        model.ParentVoucherTypeId = 2;
        model.TallyVoucherTypeName = "Tally JWO Custom";
        model.NumberingMode = "Manual";

        var result = await environment.VoucherTypeRepository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        var entity = await db.VoucherTypes.AsNoTracking().SingleAsync(x => x.Id == 1);
        Assert.Equal("Job Work Out Order", entity.Name);
        Assert.Equal("JobWorkOutOrder", entity.SystemTypeCode);
        Assert.True(entity.IsSystem);
        Assert.Null(entity.ParentVoucherTypeId);
        Assert.Equal("Tally JWO Custom", entity.TallyVoucherTypeName);
        Assert.Equal("Manual", entity.NumberingMode);
    }

    [Fact]
    public async Task Custom_rename_preserves_id_canonical_code_voucher_and_sequence_identity()
    {
        await using var environment = await CreateSeededAsync();
        var id = await CreateCustomAsync(environment, "Custom Material Issue", 1);
        await AddVoucherAndSequenceAsync(environment, id, 7);
        await using var beforeDb = environment.CreateDbContext();
        var canonical = await beforeDb.VoucherTypes.Where(x => x.Id == id).Select(x => x.SystemTypeCode).SingleAsync();
        var model = await environment.VoucherTypeRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        model.Name = "Renamed Material Issue";
        model.TallyVoucherTypeName = "Renamed Tally Type";

        var result = await environment.VoucherTypeRepository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        Assert.Equal(id, result.EntityId);
        await using var db = environment.CreateDbContext();
        var entity = await db.VoucherTypes.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(canonical, entity.SystemTypeCode);
        Assert.Equal("Renamed Tally Type", entity.TallyVoucherTypeName);
        Assert.True(await db.Vouchers.AnyAsync(x => x.VoucherTypeId == id));
        Assert.Equal(7, await ReadSequenceAsync(db, id));
    }

    [Fact]
    public async Task Unused_custom_type_can_change_base_but_historical_type_cannot_and_failed_change_rolls_back()
    {
        await using var environment = await CreateSeededAsync();
        var unusedId = await CreateCustomAsync(environment, "Unused Remap", 1);
        var unused = await environment.VoucherTypeRepository.GetForEditAsync(unusedId) ?? throw new InvalidOperationException();
        unused.ParentVoucherTypeId = 2;
        var allowed = await environment.VoucherTypeRepository.SaveAsync(unused);
        Assert.True(allowed.Success, allowed.Message);

        var usedId = await CreateCustomAsync(environment, "Used Remap", 1);
        await AddSequenceAsync(environment, usedId, 4);
        var used = await environment.VoucherTypeRepository.GetForEditAsync(usedId) ?? throw new InvalidOperationException();
        var originalToken = used.ConcurrencyToken;
        used.Name = "Must Roll Back";
        used.ParentVoucherTypeId = 2;
        var blocked = await environment.VoucherTypeRepository.SaveAsync(used);

        Assert.False(blocked.Success);
        Assert.Contains("cannot be changed after", blocked.Message);
        await using var db = environment.CreateDbContext();
        var persisted = await db.VoucherTypes.AsNoTracking().SingleAsync(x => x.Id == usedId);
        Assert.Equal("Used Remap", persisted.Name);
        Assert.Equal(1, persisted.ParentVoucherTypeId);
        Assert.Equal(originalToken, persisted.ConcurrencyToken);
        Assert.Equal(4, await ReadSequenceAsync(db, usedId));
    }

    [Fact]
    public async Task Permanent_type_delete_is_blocked_without_any_database_mutation()
    {
        await using var environment = await CreateSeededAsync();
        await using var beforeDb = environment.CreateDbContext();
        var beforeAudits = await beforeDb.AuditLogs.CountAsync();
        var before = await beforeDb.VoucherTypes.AsNoTracking().SingleAsync(x => x.Id == 1);

        var result = await environment.VoucherTypeRepository.DeleteAsync(1);

        Assert.False(result.Success);
        Assert.Equal("Permanent system Voucher Types cannot be deleted.", result.Message);
        await using var db = environment.CreateDbContext();
        var after = await db.VoucherTypes.AsNoTracking().SingleAsync(x => x.Id == 1);
        Assert.Equal(before.SystemTypeCode, after.SystemTypeCode);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(beforeAudits, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Completely_unused_custom_type_and_empty_sequence_configuration_are_deleted_transactionally()
    {
        await using var environment = await CreateSeededAsync();
        var id = await CreateCustomAsync(environment, "Disposable Type", 1);
        await AddSequenceAsync(environment, id, 0);

        var result = await environment.VoucherTypeRepository.DeleteAsync(id);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.VoucherTypes.AnyAsync(x => x.Id == id));
        Assert.Equal(-1, await ReadSequenceAsync(db, id));
    }

    [Fact]
    public async Task Voucher_or_positive_sequence_history_blocks_delete_and_preserves_all_links()
    {
        await using var voucherEnvironment = await CreateSeededAsync();
        var voucherTypeId = await CreateCustomAsync(voucherEnvironment, "Voucher Used Type", 1);
        await AddVoucherAndSequenceAsync(voucherEnvironment, voucherTypeId, 1);
        var voucherBlocked = await voucherEnvironment.VoucherTypeRepository.DeleteAsync(voucherTypeId);
        Assert.False(voucherBlocked.Success);
        Assert.Contains("used in vouchers", voucherBlocked.Message);
        await using (var db = voucherEnvironment.CreateDbContext())
        {
            Assert.True(await db.VoucherTypes.AnyAsync(x => x.Id == voucherTypeId));
            Assert.True(await db.Vouchers.AnyAsync(x => x.VoucherTypeId == voucherTypeId));
        }

        await using var sequenceEnvironment = await CreateSeededAsync();
        var sequenceTypeId = await CreateCustomAsync(sequenceEnvironment, "Sequence Used Type", 1);
        await AddSequenceAsync(sequenceEnvironment, sequenceTypeId, 9);
        var sequenceBlocked = await sequenceEnvironment.VoucherTypeRepository.DeleteAsync(sequenceTypeId);
        Assert.False(sequenceBlocked.Success);
        Assert.Contains("numbering history", sequenceBlocked.Message);
        await using var verify = sequenceEnvironment.CreateDbContext();
        Assert.True(await verify.VoucherTypes.AnyAsync(x => x.Id == sequenceTypeId));
        Assert.Equal(9, await ReadSequenceAsync(verify, sequenceTypeId));
    }

    [Fact]
    public async Task Create_and_update_share_numbering_and_length_validation()
    {
        await using var environment = await CreateSeededAsync();
        var create = NewCustom("Invalid Create", 1);
        create.NumberingMode = "Unsupported";
        var createResult = await environment.VoucherTypeRepository.SaveAsync(create);

        var id = await CreateCustomAsync(environment, "Valid Existing", 1);
        var update = await environment.VoucherTypeRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        update.NumberingMode = "Unsupported";
        update.TallyVoucherTypeName = new string('X', 201);
        var updateResult = await environment.VoucherTypeRepository.SaveAsync(update);

        Assert.False(createResult.Success);
        Assert.Equal("Select a valid Numbering Method.", createResult.Message);
        Assert.False(updateResult.Success);
        Assert.Equal("Tally Voucher Type Name cannot exceed 200 characters.", updateResult.Message);
        await using var db = environment.CreateDbContext();
        Assert.Equal("Auto", await db.VoucherTypes.Where(x => x.Id == id).Select(x => x.NumberingMode).SingleAsync());
    }

    [Fact]
    public async Task Search_matches_partial_display_tally_canonical_and_base_category_server_side()
    {
        await using var environment = await CreateSeededAsync();
        var id = await CreateCustomAsync(environment, "Subcontract Delivery", 1, "External Tally Dispatch");

        Assert.Contains(await environment.VoucherTypeRepository.GetListAsync("contract del"), x => x.Id == id);
        Assert.Contains(await environment.VoucherTypeRepository.GetListAsync("tally disp"), x => x.Id == id);
        Assert.Contains(await environment.VoucherTypeRepository.GetListAsync("job work out"), x => x.Id == id);
        Assert.Contains(await environment.VoucherTypeRepository.GetListAsync("materialout"), x => x.Id == 2);
    }

    [Fact]
    public void Page_uses_frozen_scopes_with_search_and_state_driven_focus_contract()
    {
        var path = Path.Combine(FindProjectRoot(), "Components", "Pages", "Masters", "VoucherTypes.razor");
        var source = File.ReadAllText(path);
        Assert.Contains("ContextId=\"voucher-types-list\"", source);
        Assert.Contains("ContextId=\"voucher-types-form\"", source);
        Assert.Contains("id=\"voucher-type-search\"", source);
        Assert.Contains("pendingFormFocus", source);
        Assert.Contains("pendingRowScroll", source);
        Assert.Contains("GetListAsync(searchText)", source);
        Assert.DoesNotContain("setTimeout", source, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PostgreSqlTestEnvironment> CreateSeededAsync()
    {
        var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await using var db = environment.CreateDbContext();
        var jwoParent = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
        jwoParent.IsSystem = true;
        db.VoucherTypes.Add(new VoucherType
        {
            Id = 2, CompanyId = 1, Name = "Material Out", NameNormalized = "MATERIAL OUT",
            SystemTypeCode = "MaterialOut", Nature = "Inventory Movement", PostingMode = "Inventory",
            Abbreviation = "MO", TallyVoucherTypeName = "Material Out", IsSystem = true, IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('voucher_types', 'id'), (SELECT MAX(id) FROM voucher_types))");
        return environment;
    }

    private static VoucherTypeEditModel NewCustom(string name, long? parentId) => new()
    {
        Name = name,
        ParentVoucherTypeId = parentId,
        Abbreviation = "CUS",
        NumberingMode = "Auto",
        StartingNumber = 1,
        ResetPeriod = "FinancialYear"
    };

    private static async Task<long> CreateCustomAsync(
        PostgreSqlTestEnvironment environment,
        string name,
        long parentId,
        string tallyName = "")
    {
        var model = NewCustom(name, parentId);
        model.TallyVoucherTypeName = tallyName;
        var result = await environment.VoucherTypeRepository.SaveAsync(model);
        Assert.True(result.Success, result.Message);
        return result.EntityId ?? throw new InvalidOperationException("Custom Voucher Type ID was not returned.");
    }

    private static async Task AddVoucherAndSequenceAsync(PostgreSqlTestEnvironment environment, long typeId, int lastNumber)
    {
        await using var db = environment.CreateDbContext();
        db.Vouchers.Add(new Voucher
        {
            Id = 20, CompanyId = 1, FinancialYearId = 1, VoucherTypeId = typeId,
            SequenceNumber = lastNumber, VoucherNumber = $"CUS-{lastNumber}", VoucherNumberNormalized = $"CUS-{lastNumber}",
            VoucherDate = new DateOnly(2026, 7, 23), Status = "Open",
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await AddSequenceAsync(environment, typeId, lastNumber);
    }

    private static async Task AddSequenceAsync(PostgreSqlTestEnvironment environment, long typeId, int lastNumber)
    {
        await using var db = environment.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by)
            VALUES (1, 1, {typeId}, {lastNumber}, {DateTimeOffset.UtcNow}, 'Test')
            ON CONFLICT (company_id, financial_year_id, voucher_type_id)
            DO UPDATE SET last_number=EXCLUDED.last_number, modified_at_utc=EXCLUDED.modified_at_utc, modified_by=EXCLUDED.modified_by
            """);
    }

    private static async Task<int> ReadSequenceAsync(TexTrack.Web.Data.TexTrackDbContext db, long typeId)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync();
        command.CommandText = "SELECT COALESCE(MAX(last_number), -1) FROM voucher_sequences WHERE company_id=1 AND voucher_type_id=@type";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "type";
        parameter.Value = typeId;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? -1);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
