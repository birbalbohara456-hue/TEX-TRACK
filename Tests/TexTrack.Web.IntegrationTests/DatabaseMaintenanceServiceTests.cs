using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task Clear_all_business_data_preserves_identity_audit_and_role_tables_and_reseeds_the_foundation()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var setupService = environment.CreateService();
        var developerUserId = await setupService.CreateUserAsync(new CreateApplicationUserRequest(
            "maintenance-developer@example.test", "Maintenance Developer", "Correct Horse Battery Staple",
            [SecurityRoleCodes.Developer], [1]));

        var login = await setupService.AuthenticateAsync("maintenance-developer@example.test", "Correct Horse Battery Staple");
        Assert.True(login.Succeeded);
        var accessor = new TestCircuitHttpContextAccessor { HttpContext = new DefaultHttpContext { User = login.Principal! } };
        var actingIdentityService = environment.CreateService(accessor);
        var developerAccess = new DeveloperAccessService(accessor);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:TexTrackDatabase"] = environment.ConnectionString })
            .Build();
        var webHostEnvironment = new TestHostEnvironment(AppContext.BaseDirectory);
        var maintenance = new DatabaseMaintenanceService(configuration, webHostEnvironment, developerAccess, actingIdentityService, accessor);

        long ledgerCountBefore;
        var retainedAuditIdentity = Guid.NewGuid();
        await using (var db = environment.CreateDb())
        {
            ledgerCountBefore = await db.Ledgers.CountAsync();
            db.VoucherAuditRevisions.Add(new VoucherAuditRevision
            {
                VoucherAuditIdentity = retainedAuditIdentity, VoucherId = 9999,
                CompanyId = 1, CompanyName = "Historical Company", FinancialYearId = 1,
                FinancialYearName = "Historical FY", VoucherTypeId = 1,
                VoucherTypeName = "Historical Voucher", VoucherTypeCode = "HISTORICAL",
                VoucherNumber = "H-1", RevisionNumber = 1, SnapshotSchemaVersion = 1,
                Action = VoucherAuditActions.Delete, Reason = "Retention test",
                SnapshotJson = "{}", ChangesJson = "{}",
                ContentHash = new string('A', 64), PreviousChainHash = string.Empty,
                ChainHash = new string('B', 64), RecordedAtUtc = DateTimeOffset.UtcNow,
                RecordedBy = "user:1:Retention Test"
            });
            await db.SaveChangesAsync();
        }
        Assert.True(ledgerCountBefore > 0);

        var result = await maintenance.ClearAllBusinessDataAsync(
            DatabaseMaintenanceService.ConfirmationPhrase, "Correct Horse Battery Staple");

        Assert.True(result.Success, result.Message);

        await using var after = environment.CreateDb();
        Assert.True(await after.ApplicationUsers.AnyAsync(x => x.Id == developerUserId), "identity must survive a business-data clear");
        Assert.True(await after.SecurityRoles.AnyAsync(x => x.Code == SecurityRoleCodes.Developer), "roles must survive a business-data clear");
        Assert.True(await after.ApplicationUserCompanies.AnyAsync(x => x.UserId == developerUserId && x.CompanyId == 1),
            "the acting developer's company membership must be restored after truncation");
        Assert.True(await after.AuditLogs.AnyAsync(x => x.Action == "ClearAllBusinessData" && x.Success),
            "the reset itself must be audited");
        Assert.True(await after.VoucherAuditRevisions.AnyAsync(x => x.VoucherAuditIdentity == retainedAuditIdentity),
            "full voucher history must survive normal business-data maintenance");
        Assert.True(await after.Companies.AnyAsync(x => x.Id == 1), "the mandatory company foundation must be reseeded");
        Assert.True(await after.VoucherTypes.AnyAsync(x =>
            x.CompanyId == 1 && x.SystemTypeCode == "OPENING_STOCK" && x.IsSystem && x.IsActive),
            "Opening Stock must remain available after business-data maintenance");
        Assert.True(await after.VoucherTypes.AnyAsync(x =>
            x.CompanyId == 1 && x.SystemTypeCode == "PURCHASE" && x.PostingMode == "Inventory Inward"),
            "Purchase must retain its native inventory-inward posting mode after business-data maintenance");
    }

    [Fact]
    public async Task Clear_all_business_data_is_refused_without_developer_role()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var setupService = environment.CreateService();
        var managerUserId = await setupService.CreateUserAsync(new CreateApplicationUserRequest(
            "maintenance-manager@example.test", "Maintenance Manager", "Correct Horse Battery Staple",
            [SecurityRoleCodes.Manager], [1]));

        var actingIdentity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, managerUserId.ToString()),
            new Claim(ClaimTypes.Name, "Maintenance Manager"),
            new Claim(ClaimTypes.Role, SecurityRoleCodes.Manager)
        ], "IntegrationTest");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(actingIdentity) } };
        var actingIdentityService = environment.CreateService(accessor);
        var developerAccess = new DeveloperAccessService(accessor);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:TexTrackDatabase"] = environment.ConnectionString })
            .Build();
        var webHostEnvironment = new TestHostEnvironment(AppContext.BaseDirectory);
        var maintenance = new DatabaseMaintenanceService(configuration, webHostEnvironment, developerAccess, actingIdentityService, accessor);

        var result = await maintenance.ClearAllBusinessDataAsync(
            DatabaseMaintenanceService.ConfirmationPhrase, "Correct Horse Battery Staple");

        Assert.False(result.Success);
        await using var db = environment.CreateDb();
        Assert.True(await db.Ledgers.AnyAsync(), "a refused reset must not touch business data");
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
