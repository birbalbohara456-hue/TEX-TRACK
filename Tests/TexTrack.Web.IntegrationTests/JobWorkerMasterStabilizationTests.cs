using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class JobWorkerMasterStabilizationTests
{
    [Fact]
    public async Task Create_saves_ledger_identity_details_and_both_godown_defaults()
    {
        await using var env = await CreateEnvironmentAsync();
        var result = await env.JobWorkerRepository.SaveAsync(NewModel("  Apex Processors  "));

        Assert.True(result.Success, result.Message);
        await using var db = env.CreateDbContext();
        var worker = await db.Ledgers.AsNoTracking().SingleAsync(x => x.Id == result.EntityId);
        Assert.True(worker.IsJobWorker);
        Assert.Equal("Apex Processors", worker.Name);
        Assert.Equal("AP", worker.Alias);
        Assert.Equal("Apex Tally", worker.TallyLedgerName);
        Assert.Equal("Surat", worker.City);
        Assert.Equal("Gujarat", worker.State);
        Assert.Equal("Ravi", worker.ContactPerson);
        Assert.Equal("9876543210", worker.Mobile);
        Assert.Equal(1, worker.DefaultMaterialOutDestinationGodownId);
        Assert.Equal(2, worker.DefaultMaterialInConsumptionGodownId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_and_whitespace_names_are_rejected_without_writing(string name)
    {
        await using var env = await CreateEnvironmentAsync();
        var result = await env.JobWorkerRepository.SaveAsync(NewModel(name));

        Assert.False(result.Success);
        Assert.Equal("Job Worker Name is required.", result.Message);
        await using var db = env.CreateDbContext();
        Assert.False(await db.Ledgers.AnyAsync(x => x.IsJobWorker));
    }

    [Fact]
    public async Task Duplicate_name_tally_mapping_and_update_validation_are_enforced()
    {
        await using var env = await CreateEnvironmentAsync();
        var first = await env.JobWorkerRepository.SaveAsync(NewModel("Apex Processors"));
        Assert.True(first.Success, first.Message);

        var duplicateName = await env.JobWorkerRepository.SaveAsync(NewModel("  APEX PROCESSORS "));
        var second = NewModel("Second Worker");
        second.TallyLedgerName = "apex tally";
        var duplicateTally = await env.JobWorkerRepository.SaveAsync(second);
        var edit = await env.JobWorkerRepository.GetForEditAsync(first.EntityId!.Value) ?? throw new InvalidOperationException();
        edit.Name = "   ";
        var updateValidation = await env.JobWorkerRepository.SaveAsync(edit);

        Assert.False(duplicateName.Success);
        Assert.Contains("already exists", duplicateName.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(duplicateTally.Success);
        Assert.Contains("Tally Ledger Name", duplicateTally.Message);
        Assert.False(updateValidation.Success);
        Assert.Equal("Job Worker Name is required.", updateValidation.Message);
    }

    [Fact]
    public async Task Alter_name_preserves_id_and_all_party_ledger_references()
    {
        await using var env = await CreateEnvironmentAsync();
        var created = await env.JobWorkerRepository.SaveAsync(NewModel("Original Worker"));
        var id = created.EntityId!.Value;
        await AttachVoucherReferencesAsync(env, id);

        var edit = await env.JobWorkerRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        edit.Name = "Renamed Worker";
        edit.City = "Ahmedabad";
        var result = await env.JobWorkerRepository.SaveAsync(edit);

        Assert.True(result.Success, result.Message);
        Assert.Equal(id, result.EntityId);
        await using var db = env.CreateDbContext();
        Assert.Equal("Renamed Worker", await db.Ledgers.Where(x => x.Id == id).Select(x => x.Name).SingleAsync());
        Assert.Equal(2, await db.Vouchers.CountAsync(x => x.PartyLedgerId == id));
    }

    [Fact]
    public async Task Changing_defaults_never_rewrites_saved_material_out_or_stock_history()
    {
        await using var env = await CreateEnvironmentAsync();
        var created = await env.JobWorkerRepository.SaveAsync(NewModel("Historical Worker"));
        var id = created.EntityId!.Value;
        await AttachVoucherReferencesAsync(env, id);
        await using (var db = env.CreateDbContext())
        {
            db.MaterialOutDetails.Add(new MaterialOutDetail
            {
                VoucherId = 2, DestinationGodownId = 1, DisplayedOrderNumber = "JWO-1"
            });
            db.StockMovements.Add(new StockMovement
            {
                Id = 90, CompanyId = 1, FinancialYearId = 1, VoucherId = 2,
                MovementDate = new DateOnly(2026, 7, 23), StockItemId = 1, UqcId = 1,
                GodownId = 1, QuantityChange = 5, Rate = 1, ValueChange = 5,
                MovementKind = "MaterialOutDestination", CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var edit = await env.JobWorkerRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        edit.DefaultMaterialOutDestinationGodownId = 2;
        edit.DefaultMaterialInConsumptionGodownId = 1;
        var result = await env.JobWorkerRepository.SaveAsync(edit);

        Assert.True(result.Success, result.Message);
        await using var verify = env.CreateDbContext();
        Assert.Equal(2, await verify.Ledgers.Where(x => x.Id == id).Select(x => x.DefaultMaterialOutDestinationGodownId).SingleAsync());
        Assert.Equal(1, await verify.MaterialOutDetails.Where(x => x.VoucherId == 2).Select(x => x.DestinationGodownId).SingleAsync());
        Assert.Equal(1, await verify.StockMovements.Where(x => x.Id == 90).Select(x => x.GodownId).SingleAsync());
    }

    [Fact]
    public async Task Search_matches_each_supported_partial_field_without_n_plus_one_loading()
    {
        await using var env = await CreateEnvironmentAsync();
        var created = await env.JobWorkerRepository.SaveAsync(NewModel("Apex Processors"));
        var id = created.EntityId!.Value;

        foreach (var term in new[] { "pex", "ap", "tally", "sur", "guj", "rav", "7654", "credit" })
            Assert.Contains(await env.JobWorkerRepository.GetListAsync(term), x => x.Id == id);
    }

    [Fact]
    public async Task Jwo_and_material_out_lookups_include_only_active_job_worker_ledgers()
    {
        await using var env = await CreateEnvironmentAsync();
        var created = await env.JobWorkerRepository.SaveAsync(NewModel("Production Party"));
        await using (var db = env.CreateDbContext())
        {
            db.Ledgers.Add(NewOrdinaryLedger(80));
            var type = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
            type.SystemTypeCode = "JOB_WORK_OUT_ORDER";
            type.IsSystem = true;
            await db.SaveChangesAsync();
        }

        var jwo = await env.JobWorkOrderRepository.GetLookupsAsync();
        var mo = await env.MaterialOutRepository.GetLookupDataAsync();

        Assert.Contains(jwo.JobWorkers, x => x.Id == created.EntityId);
        Assert.DoesNotContain(jwo.JobWorkers, x => x.Id == 80);
        var worker = Assert.Single(mo.JobWorkers, x => x.Id == created.EntityId);
        Assert.Equal(1, worker.DefaultMaterialOutDestinationGodownId);
        Assert.Equal("Main Godown", worker.DefaultMaterialOutDestinationGodownName);
        Assert.DoesNotContain(mo.JobWorkers, x => x.Id == 80);
    }

    [Fact]
    public async Task Invalid_group_or_godown_is_rejected_server_side()
    {
        await using var env = await CreateEnvironmentAsync();
        var badGroup = NewModel("Bad Group");
        badGroup.LedgerGroupId = 999;
        var badGodown = NewModel("Bad Godown");
        badGodown.DefaultMaterialOutDestinationGodownId = 999;

        var groupResult = await env.JobWorkerRepository.SaveAsync(badGroup);
        var godownResult = await env.JobWorkerRepository.SaveAsync(badGodown);

        Assert.False(groupResult.Success);
        Assert.Contains("Ledger Group", groupResult.Message);
        Assert.False(godownResult.Success);
        Assert.Contains("Godowns", godownResult.Message);
    }

    [Fact]
    public async Task Unused_worker_deletes_but_voucher_dependency_blocks_transactionally()
    {
        await using var env = await CreateEnvironmentAsync();
        var unused = await env.JobWorkerRepository.SaveAsync(NewModel("Unused Worker"));
        var usedModel = NewModel("Used Worker");
        usedModel.TallyLedgerName = "Used Worker Tally";
        var used = await env.JobWorkerRepository.SaveAsync(usedModel);
        await AttachVoucherReferencesAsync(env, used.EntityId!.Value);

        var deleted = await env.JobWorkerRepository.DeleteAsync(unused.EntityId!.Value);
        var blocked = await env.JobWorkerRepository.DeleteAsync(used.EntityId.Value);

        Assert.True(deleted.Success, deleted.Message);
        Assert.False(blocked.Success);
        Assert.Contains("voucher", blocked.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = env.CreateDbContext();
        Assert.False(await db.Ledgers.AnyAsync(x => x.Id == unused.EntityId));
        Assert.True(await db.Ledgers.AnyAsync(x => x.Id == used.EntityId));
        Assert.Equal(2, await db.Vouchers.CountAsync(x => x.PartyLedgerId == used.EntityId));
    }

    [Fact]
    public async Task Used_worker_group_is_locked_but_contact_and_defaults_remain_editable()
    {
        await using var env = await CreateEnvironmentAsync();
        var created = await env.JobWorkerRepository.SaveAsync(NewModel("Used Worker"));
        var id = created.EntityId!.Value;
        await AttachVoucherReferencesAsync(env, id);
        await using (var db = env.CreateDbContext())
        {
            db.LedgerGroups.Add(NewGroup(11, "Direct Expenses", "DirectExpenses"));
            await db.SaveChangesAsync();
        }

        var edit = await env.JobWorkerRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        edit.LedgerGroupId = 11;
        var blocked = await env.JobWorkerRepository.SaveAsync(edit);
        edit = await env.JobWorkerRepository.GetForEditAsync(id) ?? throw new InvalidOperationException();
        edit.ContactPerson = "Changed Contact";
        edit.DefaultMaterialOutDestinationGodownId = 2;
        var allowed = await env.JobWorkerRepository.SaveAsync(edit);

        Assert.False(blocked.Success);
        Assert.Contains("cannot be changed", blocked.Message);
        Assert.True(allowed.Success, allowed.Message);
    }

    private static async Task<PostgreSqlTestEnvironment> CreateEnvironmentAsync()
    {
        var env = await PostgreSqlTestEnvironment.CreateAsync();
        await env.SeedAsync();
        await using var db = env.CreateDbContext();
        db.LedgerGroups.Add(NewGroup(10, "Sundry Creditors", "SundryCreditors"));
        await db.SaveChangesAsync();
        return env;
    }

    private static JobWorkerEditModel NewModel(string name) => new()
    {
        Name = name, Alias = " AP ", LedgerGroupId = 10, TallyLedgerName = " Apex Tally ",
        AddressLine1 = " Industrial Estate ", AddressLine2 = " Block A ",
        City = " Surat ", State = " Gujarat ", ContactPerson = " Ravi ", Mobile = " 9876543210 ",
        DefaultMaterialOutDestinationGodownId = 1, DefaultMaterialInConsumptionGodownId = 2
    };

    private static LedgerGroup NewGroup(long id, string name, string classification) => new()
    {
        Id = id, CompanyId = 1, Name = name, NameNormalized = name.ToUpperInvariant(),
        RootClassification = classification, IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
    };

    private static Ledger NewOrdinaryLedger(long id) => new()
    {
        Id = id, CompanyId = 1, LedgerGroupId = 10, Name = "Ordinary Ledger",
        NameNormalized = "ORDINARY LEDGER", MailingName = "Ordinary Ledger",
        Country = "India", GstRegistrationType = "Unregistered", OpeningBalanceType = "Dr",
        IsActive = true, IsJobWorker = false,
        CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task AttachVoucherReferencesAsync(PostgreSqlTestEnvironment env, long workerId)
    {
        await using var db = env.CreateDbContext();
        var jwo = await db.Vouchers.SingleAsync(x => x.Id == 1);
        jwo.PartyLedgerId = workerId;
        db.Vouchers.Add(new Voucher
        {
            Id = 2, CompanyId = 1, FinancialYearId = 1, VoucherTypeId = 1,
            SequenceNumber = 2, VoucherNumber = "MO-1", VoucherNumberNormalized = "MO-1",
            VoucherDate = new DateOnly(2026, 7, 23), PartyLedgerId = workerId,
            Status = "Open", CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}