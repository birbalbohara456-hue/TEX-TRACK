using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class GodownMasterStabilizationTests
{
    [Fact]
    public async Task Create_trims_fields_and_server_search_finds_name_alias_and_address()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await AdvanceGodownSequenceAsync(environment);

        var result = await environment.MasterRepository.SaveAsync(MasterKind.Godown, new MasterEditModel
        {
            Name = "  North Warehouse  ",
            Alias = "  NW  ",
            AddressLine1 = "  Industrial Estate  ",
            AddressLine2 = "  Block B  ",
            City = "  Surat  ",
            State = "  Gujarat  "
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.EntityId);
        var byName = await environment.MasterRepository.GetGodownListAsync("north");
        var byAlias = await environment.MasterRepository.GetGodownListAsync("nw");
        var byAddress = await environment.MasterRepository.GetGodownListAsync("industrial");
        Assert.Contains(byName, x => x.Id == result.EntityId && x.Name == "North Warehouse");
        Assert.Contains(byAlias, x => x.Id == result.EntityId && x.Alias == "NW");
        Assert.Contains(byAddress, x => x.Id == result.EntityId && x.City == "Surat" && x.State == "Gujarat");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_whitespace_name_is_rejected_without_writing(string name)
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await using var beforeDb = environment.CreateDbContext();
        var before = await beforeDb.Godowns.CountAsync();

        var result = await environment.MasterRepository.SaveAsync(MasterKind.Godown, new MasterEditModel { Name = name });

        Assert.False(result.Success);
        Assert.Equal("Name is required.", result.Message);
        await using var afterDb = environment.CreateDbContext();
        Assert.Equal(before, await afterDb.Godowns.CountAsync());
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_for_create_and_alteration()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var create = await environment.MasterRepository.SaveAsync(MasterKind.Godown, new MasterEditModel { Name = " main godown " });
        var altered = await environment.MasterRepository.GetGodownForEditAsync(2) ?? throw new InvalidOperationException();
        altered.Name = "MAIN GODOWN";
        var update = await environment.MasterRepository.SaveAsync(MasterKind.Godown, altered);

        Assert.False(create.Success);
        Assert.False(update.Success);
        Assert.Contains("already exists", create.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already exists", update.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = environment.CreateDbContext();
        Assert.Equal("Job Worker Godown", await db.Godowns.Where(x => x.Id == 2).Select(x => x.Name).SingleAsync());
    }

    [Fact]
    public async Task Alteration_preserves_identity_and_material_out_references()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddMaterialOutLineUsageAsync(seed.StockItemId, seed.PrimaryUqcId);
        var model = await environment.MasterRepository.GetGodownForEditAsync(1) ?? throw new InvalidOperationException();
        var originalToken = model.ConcurrencyToken;
        model.Name = "Main Warehouse Renamed";
        model.City = "Ahmedabad";

        var result = await environment.MasterRepository.SaveAsync(MasterKind.Godown, model);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.EntityId);
        await using var db = environment.CreateDbContext();
        var godown = await db.Godowns.AsNoTracking().SingleAsync(x => x.Id == 1);
        var line = await db.MaterialOutLines.AsNoTracking().SingleAsync();
        Assert.Equal("Main Warehouse Renamed", godown.Name);
        Assert.NotEqual(originalToken, godown.ConcurrencyToken);
        Assert.Equal(1, line.SourceGodownId);
        Assert.Equal(2, line.DestinationGodownId);
    }

    [Fact]
    public async Task Unused_godown_can_be_deleted()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var result = await environment.MasterRepository.DeleteAsync(MasterKind.Godown, 2);

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDbContext();
        Assert.False(await db.Godowns.AnyAsync(x => x.Id == 2));
    }

    [Fact]
    public async Task Jwo_reference_blocks_delete_with_specific_message()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await using (var db = environment.CreateDbContext())
        {
            db.JobWorkOrderFinishedGoods.Add(new JobWorkOrderFinishedGood
            {
                Id = 10, VoucherId = 1, LineNumber = 1, StockItemId = 2,
                OrderedQuantity = 10, FinishedGoodsGodownId = 1
            });
            await db.SaveChangesAsync();
        }

        var result = await environment.MasterRepository.DeleteAsync(MasterKind.Godown, 1);

        Assert.False(result.Success);
        Assert.Contains("Job Work Out Orders", result.Message);
        await AssertGodownStillExistsAsync(environment, 1);
    }

    [Fact]
    public async Task Material_out_reference_blocks_delete_with_specific_message()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddMaterialOutLineUsageAsync(seed.StockItemId, seed.PrimaryUqcId);

        var result = await environment.MasterRepository.DeleteAsync(MasterKind.Godown, 1);

        Assert.False(result.Success);
        Assert.Contains("Material Out vouchers", result.Message);
        await AssertGodownStillExistsAsync(environment, 1);
    }

    [Fact]
    public async Task Stock_movement_history_blocks_delete_even_at_zero_balance_and_keeps_all_data()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.AddStockMovementUsageAsync(seed.StockItemId, seed.PrimaryUqcId);
        await using (var db = environment.CreateDbContext())
        {
            var movement = await db.StockMovements.SingleAsync();
            movement.QuantityChange = 0;
            movement.ValueChange = 0;
            await db.SaveChangesAsync();
        }

        var result = await environment.MasterRepository.DeleteAsync(MasterKind.Godown, 1);

        Assert.False(result.Success);
        Assert.Contains("even when its current balance is zero", result.Message);
        await using var verify = environment.CreateDbContext();
        Assert.True(await verify.Godowns.AnyAsync(x => x.Id == 1));
        Assert.True(await verify.StockMovements.AnyAsync(x => x.GodownId == 1));
        Assert.True(await verify.AuditLogs.AnyAsync(x => x.EntityType == "Godown" && x.EntityId == 1 && !x.Success));
    }

    [Fact]
    public async Task Approved_godown_schema_remains_flat()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await using var db = environment.CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(Godown));

        Assert.NotNull(entity);
        Assert.DoesNotContain(entity!.GetProperties(), x => x.Name.Equals("ParentId", StringComparison.Ordinal));
        Assert.DoesNotContain(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Godown));
    }

    [Fact]
    public void Dedicated_page_uses_frozen_context_scope_and_stateful_focus_contract()
    {
        var path = Path.Combine(FindProjectRoot(), "Components", "Pages", "Masters", "Godowns.razor");
        var source = File.ReadAllText(path);

        Assert.Contains("ContextId=\"godowns-list\"", source);
        Assert.Contains("ContextId=\"godowns-form\"", source);
        Assert.Contains("Mode=\"@(editModel.Id == 0 ? KeyboardContextMode.Create : KeyboardContextMode.Alteration)\"", source);
        Assert.Contains("GetGodownListAsync(searchText)", source);
        Assert.Contains("pendingRowScroll", source);
        Assert.Contains("InitialFocusTarget=\"godown-name-input\"", source);
        Assert.DoesNotContain("setTimeout", source, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AdvanceGodownSequenceAsync(PostgreSqlTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('godowns', 'id'), (SELECT MAX(id) FROM godowns))");
    }

    private static async Task AssertGodownStillExistsAsync(PostgreSqlTestEnvironment environment, long id)
    {
        await using var db = environment.CreateDbContext();
        Assert.True(await db.Godowns.AnyAsync(x => x.Id == id));
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
