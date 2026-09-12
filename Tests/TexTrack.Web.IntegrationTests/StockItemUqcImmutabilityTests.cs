using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class StockItemUqcImmutabilityTests
{
    private const string LockedMessage =
        "Cannot change UQC because this Stock Item has already been used or linked.";

    [Fact]
    public async Task Voucher_history_uses_postgresql_month_and_page_queries()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.UseCanonicalJwoTypeCodeAsync();
        await environment.AddJwoFinishedGoodUsageAsync(seed.StockItemId);

        var months = await environment.VoucherHistoryRepository.GetMonthsAsync("job-out-orders");
        Assert.Single(months);
        var month = months[0];
        var from = new DateOnly(month.Year, month.Month, 1);
        var page = await environment.VoucherHistoryRepository.GetPageAsync(
            "job-out-orders", from, from.AddMonths(1).AddDays(-1), string.Empty, 0, 15);

        Assert.Single(page.Rows);
        Assert.Equal(1, page.TotalCount);
        Assert.NotEqual("Total -", page.ActiveQuantityTotals);
    }

    [Fact]
    public async Task Unused_item_can_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        var model = await environment.GetEditModelAsync(seed.StockItemId);

        Assert.False(model.IsUqcLocked);
        model.UqcId = seed.AlternateUqcId;

        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        Assert.Equal(seed.AlternateUqcId, await environment.GetPersistedUqcIdAsync(seed.StockItemId));
    }

    [Fact]
    public async Task Linked_through_jwo_finished_good_cannot_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddJwoFinishedGoodUsageAsync(seed.StockItemId);

        await AssertUqcChangeBlockedAsync(environment, seed);
    }

    [Fact]
    public async Task Linked_through_jwo_component_cannot_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddJwoComponentUsageAsync(seed.StockItemId, seed.PrimaryUqcId);

        await AssertUqcChangeBlockedAsync(environment, seed);
    }

    [Fact]
    public async Task Linked_through_size_allocation_cannot_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddSizeAllocationUsageAsync(seed.StockItemVariantId);

        await AssertUqcChangeBlockedAsync(environment, seed);
    }

    [Fact]
    public async Task Linked_through_material_out_line_cannot_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddMaterialOutLineUsageAsync(seed.StockItemId, seed.PrimaryUqcId);

        await AssertUqcChangeBlockedAsync(environment, seed);
    }

    [Fact]
    public async Task Linked_through_stock_movement_cannot_change_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddStockMovementUsageAsync(seed.StockItemId, seed.PrimaryUqcId);

        await AssertUqcChangeBlockedAsync(environment, seed);
    }

    [Fact]
    public async Task Structural_variants_alone_do_not_lock_uqc()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.ReplaceBaseVariantWithStructuralMappingsAsync(seed.StockItemId);
        var model = await environment.GetEditModelAsync(seed.StockItemId);

        Assert.False(model.IsUqcLocked);
        model.UqcId = seed.AlternateUqcId;

        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        Assert.Equal(seed.AlternateUqcId, await environment.GetPersistedUqcIdAsync(seed.StockItemId));
    }

    [Fact]
    public async Task Unchanged_uqc_allows_permitted_edits()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddJwoFinishedGoodUsageAsync(seed.StockItemId);
        var model = await environment.GetEditModelAsync(seed.StockItemId);

        Assert.True(model.IsUqcLocked);
        model.Name = "Renamed Transactional Item";
        model.HsnCode = "HSN-UPDATED";
        model.TaxMode = "DirectRates";
        model.IgstRate = 5;
        model.CgstRate = 2.5m;
        model.SgstRate = 2.5m;

        var result = await environment.Repository.SaveAsync(model);

        Assert.True(result.Success, result.Message);
        var persisted = await environment.GetPersistedItemAsync(seed.StockItemId);
        Assert.Equal(seed.PrimaryUqcId, persisted.UqcId);
        Assert.Equal("Renamed Transactional Item", persisted.Name);
        Assert.Equal("HSN-UPDATED", persisted.HsnCode);
        Assert.Equal("DirectRates", persisted.TaxMode);
    }

    [Fact]
    public async Task Manipulated_model_is_rejected()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddJwoFinishedGoodUsageAsync(seed.StockItemId);
        var manipulatedModel = await environment.GetEditModelAsync(seed.StockItemId);

        Assert.True(manipulatedModel.IsUqcLocked);
        manipulatedModel.IsUqcLocked = false;
        manipulatedModel.UqcId = seed.AlternateUqcId;

        var result = await environment.Repository.SaveAsync(manipulatedModel);

        Assert.False(result.Success);
        Assert.Equal(LockedMessage, result.Message);
        Assert.Equal(seed.PrimaryUqcId, await environment.GetPersistedUqcIdAsync(seed.StockItemId));
    }

    [Fact]
    public async Task Failed_update_leaves_all_persisted_data_unchanged()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddJwoFinishedGoodUsageAsync(seed.StockItemId);
        var original = await environment.GetPersistedSnapshotAsync(seed.StockItemId);
        var model = await environment.GetEditModelAsync(seed.StockItemId);

        model.UqcId = seed.AlternateUqcId;
        model.Name = "Must Not Persist";
        model.HsnCode = "MUST-NOT-PERSIST";
        model.TaxMode = "DirectRates";
        model.IgstRate = 18;
        model.ColourIds.Add(seed.ColourId);
        model.SizeIds.Add(seed.SizeId);

        var result = await environment.Repository.SaveAsync(model);

        Assert.False(result.Success);
        Assert.Equal(LockedMessage, result.Message);
        Assert.Equal(original, await environment.GetPersistedSnapshotAsync(seed.StockItemId));
    }

    private static async Task AssertUqcChangeBlockedAsync(
        PostgreSqlTestEnvironment environment,
        SeedState seed)
    {
        var model = await environment.GetEditModelAsync(seed.StockItemId);
        Assert.True(model.IsUqcLocked);
        model.UqcId = seed.AlternateUqcId;

        var result = await environment.Repository.SaveAsync(model);

        Assert.False(result.Success);
        Assert.Equal(LockedMessage, result.Message);
        Assert.Equal(seed.PrimaryUqcId, await environment.GetPersistedUqcIdAsync(seed.StockItemId));
    }
}

internal sealed class PostgreSqlTestEnvironment : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _schemaName;
    private readonly DbContextOptions<TexTrackDbContext> _options;

    private PostgreSqlTestEnvironment(
        string adminConnectionString,
        string schemaName,
        DbContextOptions<TexTrackDbContext> options)
    {
        _adminConnectionString = adminConnectionString;
        _schemaName = schemaName;
        _options = options;
        var factory = new TestDbContextFactory(options);
        var companyContext = CreateCompanyContext();
        var stockPosting = new StockPostingService();
        StockPostingService = stockPosting;
        var sequenceAllocator = new VoucherSequenceAllocator(companyContext);
        var auditHistory = new VoucherAuditHistoryService(factory);
        InventoryPeriodControlService = new InventoryPeriodControlService();
        InventoryPolicySettingsService = new InventoryPolicySettingsService(
            factory, companyContext, stockPosting);
        Repository = new StockItemRepository(
            factory, companyContext, stockPosting, sequenceAllocator, auditHistory,
            InventoryPeriodControlService);
        MasterRepository = new MasterRepository(factory, companyContext);
        VoucherTypeRepository = new VoucherTypeRepository(factory, companyContext);
        JobWorkerRepository = new JobWorkerRepository(factory, companyContext);
        var lifecycle = new VoucherLifecycleService();
        JobWorkOrderRepository = new JobWorkOrderRepository(
            factory, companyContext, lifecycle, sequenceAllocator);
        BillOfMaterialRepository = new BillOfMaterialRepository(factory, companyContext);
        MaterialOutRepository = new MaterialOutRepository(factory, companyContext, lifecycle, stockPosting);
        MaterialInRepository = new MaterialInRepository(factory, companyContext, lifecycle, stockPosting);
        InventoryInwardRepository = new InventoryInwardRepository(
            factory, companyContext, lifecycle, stockPosting,
            sequenceAllocator, auditHistory,
            InventoryPeriodControlService);
        PurchaseOrderRepository = new PurchaseOrderRepository(
            factory, companyContext, lifecycle, sequenceAllocator, auditHistory);
        PurchaseReturnRepository = new PurchaseReturnRepository(
            factory, companyContext, lifecycle, stockPosting, sequenceAllocator, auditHistory,
            InventoryPeriodControlService);
        StockPositionService = new StockPositionService(factory, companyContext);
        OperationalReportingService = new OperationalReportingService(factory, companyContext);
        VoucherHistoryRepository = new VoucherHistoryRepository(factory, companyContext);
        TallyXmlExchangeService = new TallyXmlExchangeService(factory, companyContext, new TallyXmlParser(), lifecycle, stockPosting);
        TallyXmlExporter = new TallyXmlExporter(factory, companyContext);
    }

    private static CurrentCompanyContext CreateCompanyContext()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "9001"),
            new Claim(ClaimTypes.Name, "Integration Test Developer"),
            new Claim(TexTrackClaimTypes.CompanyId, "1"),
            new Claim(TexTrackClaimTypes.CompanyName, "Test Company"),
            new Claim(TexTrackClaimTypes.FinancialYearId, "1"),
            new Claim(TexTrackClaimTypes.FinancialYearName, "2026-27"),
            new Claim(ClaimTypes.Role, SecurityRoleCodes.Developer)
        ], "IntegrationTest");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return new CurrentCompanyContext(accessor, new BusinessRuleTestSessionValidator());
    }

    public StockItemRepository Repository { get; }
    public MasterRepository MasterRepository { get; }
    public VoucherTypeRepository VoucherTypeRepository { get; }
    public JobWorkerRepository JobWorkerRepository { get; }
    public JobWorkOrderRepository JobWorkOrderRepository { get; }
    public BillOfMaterialRepository BillOfMaterialRepository { get; }
    public MaterialOutRepository MaterialOutRepository { get; }
    public MaterialInRepository MaterialInRepository { get; }
    public InventoryPeriodControlService InventoryPeriodControlService { get; }
    public InventoryPolicySettingsService InventoryPolicySettingsService { get; }
    public StockPostingService StockPostingService { get; }
    public InventoryInwardRepository InventoryInwardRepository { get; }
    public PurchaseOrderRepository PurchaseOrderRepository { get; }
    public PurchaseReturnRepository PurchaseReturnRepository { get; }
    public StockPositionService StockPositionService { get; }
    public OperationalReportingService OperationalReportingService { get; }
    public VoucherHistoryRepository VoucherHistoryRepository { get; }
    public TallyXmlExchangeService TallyXmlExchangeService { get; }
    public TallyXmlExporter TallyXmlExporter { get; }

    public static async Task<PostgreSqlTestEnvironment> CreateAsync()
    {
        var connectionString = TestDatabaseSettings.GetConnectionString();

        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = string.Empty
        };
        var schemaName = $"uqc_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            SearchPath = schemaName
        };
        var options = new DbContextOptionsBuilder<TexTrackDbContext>()
            .UseNpgsql(testBuilder.ConnectionString)
            .Options;

        await using (var db = new TexTrackDbContext(options))
        {
            var createScript = db.Database.GenerateCreateScript();
            await db.Database.ExecuteSqlRawAsync(createScript);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE voucher_sequences
                (
                    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
                    financial_year_id bigint NOT NULL REFERENCES financial_years(id) ON DELETE RESTRICT,
                    voucher_type_id bigint NOT NULL REFERENCES voucher_types(id) ON DELETE RESTRICT,
                    last_number integer NOT NULL,
                    modified_at_utc timestamp with time zone NOT NULL,
                    modified_by varchar(100) NOT NULL,
                    PRIMARY KEY (company_id, financial_year_id, voucher_type_id),
                    CONSTRAINT ck_voucher_sequences_positive CHECK (last_number >= 0)
                )
                """);
        }

        return new PostgreSqlTestEnvironment(adminBuilder.ConnectionString, schemaName, options);
    }

    public async Task<SeedState> SeedAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = CreateDbContext();

        db.Companies.Add(new Company
        {
            Id = 1,
            Name = "Test Company",
            NameNormalized = "TEST COMPANY",
            Code = "TEST",
            AllowNegativeStock = true,
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        await db.SaveChangesAsync();

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            CompanyId = 1,
            Name = "2026-27",
            StartDate = new DateOnly(2026, 4, 1),
            EndDate = new DateOnly(2027, 3, 31),
            IsActive = true,
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.StockGroups.Add(new StockGroup
        {
            Id = 1,
            CompanyId = 1,
            Name = "Raw Material",
            NameNormalized = "RAW MATERIAL",
            RootClassification = "RawMaterial",
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.Uqcs.AddRange(
            NewUqc(1, "Pieces", "PCS", now),
            NewUqc(2, "Metres", "MTS", now));
        db.Colours.Add(new Colour
        {
            Id = 1,
            CompanyId = 1,
            Name = "Black",
            NameNormalized = "BLACK",
            ColourCode = "BLK",
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.Sizes.Add(new SizeMaster
        {
            Id = 1,
            CompanyId = 1,
            Name = "Medium",
            NameNormalized = "MEDIUM",
            DisplayOrder = 1,
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.Godowns.AddRange(
            NewGodown(1, "Main Godown", now),
            NewGodown(2, "Job Worker Godown", now));
        db.VoucherTypes.Add(new VoucherType
        {
            Id = 1,
            CompanyId = 1,
            Name = "Job Work Out Order",
            NameNormalized = "JOB WORK OUT ORDER",
            SystemTypeCode = "JobWorkOutOrder",
            Nature = "Inventory",
            PostingMode = "Order",
            Abbreviation = "JWO",
            NumberingMode = "Auto",
            ResetPeriod = "FinancialYear",
            TallyVoucherTypeName = "Job Work Out Order",
            IsActive = true,
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        db.StockItems.AddRange(
            NewStockItem(1, "Candidate Item", 1, now),
            NewStockItem(2, "Helper Finished Good", 1, now),
            NewStockItem(3, "Helper Component", 1, now));
        await db.SaveChangesAsync();

        db.StockItemVariants.AddRange(
            NewVariant(1, 1, "BASE", now),
            NewVariant(2, 2, "BASE", now),
            NewVariant(3, 3, "BASE", now));
        db.Vouchers.Add(NewVoucher(1, 1, "JWO-1", now));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('stock_items', 'id'), (SELECT MAX(id) FROM stock_items), true)");
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('stock_item_variants', 'id'), (SELECT MAX(id) FROM stock_item_variants), true)");
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('vouchers', 'id'), (SELECT MAX(id) FROM vouchers), true)");

        return new SeedState(1, 1, 2, 1, 1, 1);
    }

    public async Task<StockItemEditModel> GetEditModelAsync(long stockItemId) =>
        await Repository.GetForEditAsync(stockItemId) ??
        throw new InvalidOperationException("Seeded Stock Item was not found.");

    public async Task AddOpeningStockVoucherTypeAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = CreateDbContext();
        db.VoucherTypes.Add(new VoucherType
        {
            Id = 2,
            CompanyId = 1,
            Name = "Opening Stock",
            NameNormalized = "OPENING STOCK",
            SystemTypeCode = "OPENING_STOCK",
            Nature = "Inventory",
            PostingMode = "Inventory Inward",
            Abbreviation = "OPN",
            NumberingMode = "Auto",
            ResetPeriod = "FinancialYear",
            IsSystem = true,
            IsActive = true,
            CreatedAtUtc = now,
            ModifiedAtUtc = now
        });
        await db.SaveChangesAsync();
    }

    public async Task<long> GetPersistedUqcIdAsync(long stockItemId)
    {
        await using var db = CreateDbContext();
        return await db.StockItems.Where(x => x.Id == stockItemId).Select(x => x.UqcId).SingleAsync();
    }

    public async Task<StockItem> GetPersistedItemAsync(long stockItemId)
    {
        await using var db = CreateDbContext();
        return await db.StockItems.AsNoTracking().SingleAsync(x => x.Id == stockItemId);
    }

    public async Task<PersistedSnapshot> GetPersistedSnapshotAsync(long stockItemId)
    {
        await using var db = CreateDbContext();
        var item = await db.StockItems.AsNoTracking().SingleAsync(x => x.Id == stockItemId);
        var colourIds = await db.StockItemColours.AsNoTracking()
            .Where(x => x.StockItemId == stockItemId)
            .OrderBy(x => x.ColourId)
            .Select(x => x.ColourId)
            .ToArrayAsync();
        var sizeIds = await db.StockItemSizes.AsNoTracking()
            .Where(x => x.StockItemId == stockItemId)
            .OrderBy(x => x.SizeId)
            .Select(x => x.SizeId)
            .ToArrayAsync();
        var variantKeys = await db.StockItemVariants.AsNoTracking()
            .Where(x => x.StockItemId == stockItemId)
            .OrderBy(x => x.VariantKey)
            .Select(x => x.VariantKey)
            .ToArrayAsync();

        return new PersistedSnapshot(
            item.Name,
            item.HsnCode,
            item.TaxMode,
            item.UqcId,
            item.ConcurrencyToken,
            string.Join(',', colourIds),
            string.Join(',', sizeIds),
            string.Join(',', variantKeys));
    }

    public async Task AddJwoFinishedGoodUsageAsync(long stockItemId)
    {
        await using var db = CreateDbContext();
        db.JobWorkOrderFinishedGoods.Add(NewFinishedGood(10, stockItemId));
        await db.SaveChangesAsync();
    }

    public async Task UseCanonicalJwoTypeCodeAsync()
    {
        await using var db = CreateDbContext();
        var type = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
        type.SystemTypeCode = "JOB_WORK_OUT_ORDER";
        type.IsSystem = true;
        await db.SaveChangesAsync();
    }

    public async Task AddJwoComponentUsageAsync(long stockItemId, long uqcId)
    {
        await using var db = CreateDbContext();
        db.JobWorkOrderFinishedGoods.Add(NewFinishedGood(10, 2));
        db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
        {
            Id = 10,
            FinishedGoodId = 10,
            LineNumber = 1,
            StockItemId = stockItemId,
            UqcId = uqcId,
            RequiredQuantity = 10
        });
        await db.SaveChangesAsync();
    }

    public async Task AddSizeAllocationUsageAsync(long stockItemVariantId)
    {
        await using var db = CreateDbContext();
        db.JobWorkOrderFinishedGoods.Add(NewFinishedGood(10, 2));
        db.JobWorkOrderSizeAllocations.Add(new JobWorkOrderSizeAllocation
        {
            Id = 10,
            FinishedGoodId = 10,
            StockItemVariantId = stockItemVariantId,
            Quantity = 5
        });
        await db.SaveChangesAsync();
    }

    public async Task AddMaterialOutLineUsageAsync(long stockItemId, long uqcId)
    {
        await using var db = CreateDbContext();
        db.JobWorkOrderFinishedGoods.Add(NewFinishedGood(10, 2));
        db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
        {
            Id = 10,
            FinishedGoodId = 10,
            LineNumber = 1,
            StockItemId = 3,
            UqcId = 1,
            RequiredQuantity = 10
        });
        db.Vouchers.Add(NewVoucher(2, 2, "MO-1", DateTimeOffset.UtcNow));
        db.MaterialOutLines.Add(new MaterialOutLine
        {
            Id = 10,
            VoucherId = 2,
            JwoVoucherId = 1,
            JwoFinishedGoodId = 10,
            JwoComponentId = 10,
            LineNumber = 1,
            StockItemId = stockItemId,
            UqcId = uqcId,
            SourceGodownId = 1,
            DestinationGodownId = 2,
            RequiredQuantity = 10,
            IssuedQuantity = 5,
            Rate = 2,
            Amount = 10
        });
        await db.SaveChangesAsync();
    }

    public async Task AddStockMovementUsageAsync(long stockItemId, long uqcId)
    {
        await using var db = CreateDbContext();
        db.Vouchers.Add(NewVoucher(2, 2, "MO-1", DateTimeOffset.UtcNow));
        db.StockMovements.Add(new StockMovement
        {
            Id = 10,
            CompanyId = 1,
            FinancialYearId = 1,
            VoucherId = 2,
            MovementDate = new DateOnly(2026, 7, 23),
            StockItemId = stockItemId,
            UqcId = uqcId,
            GodownId = 1,
            QuantityChange = -5,
            Rate = 2,
            ValueChange = -10,
            MovementKind = "MaterialOutSource",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task ReplaceBaseVariantWithStructuralMappingsAsync(long stockItemId)
    {
        await using var db = CreateDbContext();
        var baseVariant = await db.StockItemVariants.SingleAsync(x => x.StockItemId == stockItemId);
        db.StockItemVariants.Remove(baseVariant);
        db.StockItemColours.Add(new StockItemColour { StockItemId = stockItemId, ColourId = 1 });
        db.StockItemSizes.Add(new StockItemSize { StockItemId = stockItemId, SizeId = 1 });
        db.StockItemVariants.Add(NewVariant(10, stockItemId, "C:1|S:1", DateTimeOffset.UtcNow, 1, 1));
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    internal TexTrackDbContext CreateDbContext() => new(_options);

    private static Uqc NewUqc(long id, string name, string shortName, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        Name = name,
        NameNormalized = name.ToUpperInvariant(),
        ShortName = shortName,
        DecimalPlaces = 2,
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static Godown NewGodown(long id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        Name = name,
        NameNormalized = name.ToUpperInvariant(),
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static StockItem NewStockItem(long id, string name, long uqcId, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        Name = name,
        NameNormalized = name.ToUpperInvariant(),
        StockGroupId = 1,
        UqcId = uqcId,
        TaxMode = "NotApplicable",
        HsnCode = "HSN-ORIGINAL",
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static StockItemVariant NewVariant(
        long id,
        long stockItemId,
        string key,
        DateTimeOffset now,
        long? colourId = null,
        long? sizeId = null) => new()
    {
        Id = id,
        CompanyId = 1,
        StockItemId = stockItemId,
        ColourId = colourId,
        SizeId = sizeId,
        VariantKey = key,
        IsActive = true,
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static Voucher NewVoucher(long id, int sequence, string number, DateTimeOffset now) => new()
    {
        Id = id,
        CompanyId = 1,
        FinancialYearId = 1,
        VoucherTypeId = 1,
        SequenceNumber = sequence,
        VoucherNumber = number,
        VoucherNumberNormalized = number,
        VoucherDate = new DateOnly(2026, 7, 23),
        Status = "Open",
        CreatedAtUtc = now,
        ModifiedAtUtc = now
    };

    private static JobWorkOrderFinishedGood NewFinishedGood(long id, long stockItemId) => new()
    {
        Id = id,
        VoucherId = 1,
        LineNumber = 1,
        StockItemId = stockItemId,
        OrderedQuantity = 10
    };
}

internal sealed class TestDbContextFactory(DbContextOptions<TexTrackDbContext> options)
    : IDbContextFactory<TexTrackDbContext>
{
    public TexTrackDbContext CreateDbContext() => new(options);

    public Task<TexTrackDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

internal sealed record SeedState(
    long StockItemId,
    long PrimaryUqcId,
    long AlternateUqcId,
    long StockItemVariantId,
    long ColourId,
    long SizeId);

internal sealed record PersistedSnapshot(
    string Name,
    string HsnCode,
    string TaxMode,
    long UqcId,
    string ConcurrencyToken,
    string ColourIds,
    string SizeIds,
    string VariantKeys);
