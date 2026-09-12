using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class UserIdentitySecurityTests
{
    [Fact]
    public async Task Concurrent_failures_lock_once_without_losing_attempts_and_audit_all_outcomes()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var service = environment.CreateService();
        var id = await service.CreateUserAsync(new CreateApplicationUserRequest(
            "parallel@example.test", "Parallel", "Correct Horse Battery Staple",
            [SecurityRoleCodes.Owner], [1]));
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => service.AuthenticateAsync("parallel@example.test", "wrong-secret")));
        Assert.All(results, result => Assert.False(result.Succeeded));
        await using var db = environment.CreateDb();
        var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(UserIdentityService.MaxFailedAttempts, user.FailedLoginCount);
        Assert.True(user.LockoutEndUtc > DateTimeOffset.UtcNow);
        var events = await db.AuditLogs.Where(x => x.EntityType == "Authentication" && x.EntityId == id).ToListAsync();
        Assert.Equal(5, events.Count(x => x.Action == "LoginFailure"));
        Assert.Equal(5, events.Count(x => x.Action == "LoginBlocked"));
        Assert.Single(events.Where(x => x.Action == "LoginLockout"));
        Assert.All(events, e => Assert.DoesNotContain("wrong-secret", e.Description));

        // Expiry starts a fresh attempt window, not immediate re-lockout.
        await db.ApplicationUsers.Where(x => x.Id == id).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.LockoutEndUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var failed = await service.AuthenticateAsync("parallel@example.test", "wrong-secret");
        Assert.False(failed.IsLockedOut);
        Assert.Equal(1, await db.ApplicationUsers.Where(x => x.Id == id).Select(x => x.FailedLoginCount).SingleAsync());
        Assert.True((await service.AuthenticateAsync("parallel@example.test", "Correct Horse Battery Staple")).Succeeded);
        Assert.Equal(0, await db.ApplicationUsers.Where(x => x.Id == id).Select(x => x.FailedLoginCount).SingleAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "LoginSuccess"));
        Assert.False((await service.AuthenticateAsync("unknown@example.test", "wrong-secret")).Succeeded);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityType == "Authentication" && x.EntityId == null && x.Action == "LoginFailure"));
    }

    [Fact]
    public void Password_hashes_are_salted_and_wrong_passwords_are_rejected()
    {
        var first = PasswordCredentialService.HashPassword("Correct Horse Battery Staple");
        var second = PasswordCredentialService.HashPassword("Correct Horse Battery Staple");

        Assert.NotEqual(first, second);
        Assert.True(PasswordCredentialService.VerifyPassword("Correct Horse Battery Staple", first));
        Assert.False(PasswordCredentialService.VerifyPassword("wrong", first));
        Assert.False(PasswordCredentialService.VerifyPassword("Correct Horse Battery Staple", "malformed"));
    }

    [Fact]
    public async Task Persisted_user_authentication_locks_repeated_failures_and_emits_immutable_claims()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var service = environment.CreateService();
        var userId = await service.CreateUserAsync(new CreateApplicationUserRequest(
            "owner@example.test", "Owner One", "Correct Horse Battery Staple",
            [SecurityRoleCodes.Owner], [1]));

        for (var attempt = 0; attempt < UserIdentityService.MaxFailedAttempts; attempt++)
        {
            var failed = await service.AuthenticateAsync("owner@example.test", "wrong");
            Assert.False(failed.Succeeded);
        }

        var locked = await service.AuthenticateAsync("owner@example.test", "Correct Horse Battery Staple");
        Assert.False(locked.Succeeded);
        Assert.True(locked.IsLockedOut);

        await environment.ClearLockoutAsync(userId);
        var authenticated = await service.AuthenticateAsync("OWNER@example.test", "Correct Horse Battery Staple");

        Assert.True(authenticated.Succeeded);
        Assert.NotNull(authenticated.Principal);
        Assert.Equal(userId.ToString(), authenticated.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("1", authenticated.Principal.FindFirstValue(TexTrackClaimTypes.CompanyId));
        Assert.False(string.IsNullOrWhiteSpace(authenticated.Principal.FindFirstValue(TexTrackClaimTypes.CompanyName)));
        Assert.False(string.IsNullOrWhiteSpace(authenticated.Principal.FindFirstValue(TexTrackClaimTypes.FinancialYearId)));
        Assert.True(authenticated.Principal.IsInRole(SecurityRoleCodes.Owner));
    }

    [Fact]
    public async Task Deactivated_user_cannot_authenticate()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var service = environment.CreateService();
        var userId = await service.CreateUserAsync(new CreateApplicationUserRequest(
            "operator@example.test", "Operator One", "Correct Horse Battery Staple",
            [SecurityRoleCodes.Operator], [1]));

        await service.DeactivateUserAsync(userId);

        var result = await service.AuthenticateAsync("operator@example.test", "Correct Horse Battery Staple");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Non_developer_cannot_invoke_user_administration_service_directly()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var service = environment.CreateService(SecurityRoleCodes.Manager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateUserAsync(
            new CreateApplicationUserRequest(
                "blocked@example.test", "Blocked Manager", "Correct Horse Battery Staple",
                [SecurityRoleCodes.Operator], [1])));
    }
}

internal sealed class IdentityTestEnvironment : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schemaName;
    private readonly DbContextOptions<TexTrackDbContext> options;

    public string ConnectionString { get; }

    private IdentityTestEnvironment(string adminConnectionString, string schemaName, DbContextOptions<TexTrackDbContext> options, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        this.schemaName = schemaName;
        this.options = options;
        ConnectionString = connectionString;
    }

    public static async Task<IdentityTestEnvironment> CreateAsync()
    {
        var source = TestDatabaseSettings.GetConnectionString();
        var adminBuilder = new NpgsqlConnectionStringBuilder(source) { SearchPath = string.Empty };
        var schema = $"identity_test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { SearchPath = schema };
        var options = new DbContextOptionsBuilder<TexTrackDbContext>().UseNpgsql(testBuilder.ConnectionString).Options;
        await using (var connection = new NpgsqlConnection(testBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            var migrationsDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
            foreach (var migration in Directory.GetFiles(migrationsDirectory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(migration), connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync();
            }
        }
        await using (var db = new TexTrackDbContext(options))
        {
            Assert.True(await db.Companies.AnyAsync(x => x.Id == 1));
        }

        return new IdentityTestEnvironment(adminBuilder.ConnectionString, schema, options, testBuilder.ConnectionString);
    }

    public UserIdentityService CreateService(string role = SecurityRoleCodes.Developer)
    {
        // User administration checks persisted authority, not just a role claim.
        using var db = CreateDb();
        var persistedRole = db.SecurityRoles.Single(x => x.Code == role);
        var company = db.Companies.Single(x => x.Id == 1);
        var year = db.FinancialYears.First(x => x.CompanyId == 1 && x.IsActive);
        var name = $"fixture-{Guid.NewGuid():N}";
        var user = new ApplicationUser
        {
            UserName = name, UserNameNormalized = name.ToUpperInvariant(), DisplayName = name,
            PasswordHash = PasswordCredentialService.HashPassword("Fixture password for tests only"),
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow,
            Roles = [new ApplicationUserRole { RoleId = persistedRole.Id }],
            Companies = [new ApplicationUserCompany { CompanyId = 1, IsDefault = true }]
        };
        db.ApplicationUsers.Add(user);
        db.SaveChanges();
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Role, role),
            new Claim(TexTrackClaimTypes.SecurityStamp, user.SecurityStamp),
            new Claim(TexTrackClaimTypes.SessionExpiresAt, DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString()),
            new Claim(TexTrackClaimTypes.CompanyId, company.Id.ToString()),
            new Claim(TexTrackClaimTypes.CompanyName, company.Name),
            new Claim(TexTrackClaimTypes.FinancialYearId, year.Id.ToString()),
            new Claim(TexTrackClaimTypes.FinancialYearName, year.Name)
        ], "IntegrationTest");
        var accessor = new TestCircuitHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return new UserIdentityService(new TestDbContextFactory(options), accessor);
    }

    public UserIdentityService CreateService(IHttpContextAccessor accessor) =>
        new(new TestDbContextFactory(options), accessor);

    public TexTrackDbContext CreateDb() => new(options);

    public IDbContextFactory<TexTrackDbContext> CreateFactory() => new TestDbContextFactory(options);

    public CurrentCompanyContext CreateCompanyContext(string role, long companyId)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "9001"),
            new Claim(ClaimTypes.Name, "Authorization Test Actor"),
            new Claim(ClaimTypes.Role, role),
            new Claim(TexTrackClaimTypes.CompanyId, companyId.ToString()),
            new Claim(TexTrackClaimTypes.CompanyName, $"Company {companyId}"),
            new Claim(TexTrackClaimTypes.FinancialYearId, "1"),
            new Claim(TexTrackClaimTypes.FinancialYearName, "Test FY")
        ], "IntegrationTest");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return new CurrentCompanyContext(accessor, new BusinessRuleTestSessionValidator());
    }

    public LedgerRepository CreateLedgerRepository(string role, long companyId) =>
        new(new TestDbContextFactory(options), CreateCompanyContext(role, companyId));

    public AssistantSettingsStore CreateAssistantSettingsStore(string role, long companyId) =>
        new(new EphemeralDataProtectionProvider(), CreateCompanyContext(role, companyId));

    public async Task<long> CreateLedgerGroupAsync(long companyId, string name)
    {
        await using var db = new TexTrackDbContext(options);
        var group = new LedgerGroup
        {
            CompanyId = companyId,
            Name = name,
            NameNormalized = name.Trim().ToUpperInvariant(),
            RootClassification = "Assets",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ModifiedAtUtc = DateTimeOffset.UtcNow
        };
        db.LedgerGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    public async Task<long> CreateCompanyAsync(string name)
    {
        await using var db = new TexTrackDbContext(options);
        var company = new Company
        {
            Name = name,
            NameNormalized = name.Trim().ToUpperInvariant(),
            Code = name.Length >= 4 ? name[..4].ToUpperInvariant() : name.ToUpperInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ModifiedAtUtc = DateTimeOffset.UtcNow
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    public async Task ClearLockoutAsync(long userId)
    {
        await using var db = new TexTrackDbContext(options);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Id == userId);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
