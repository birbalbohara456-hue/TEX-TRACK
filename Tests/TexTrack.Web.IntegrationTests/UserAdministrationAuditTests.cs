using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class UserAdministrationAuditTests
{
    private const string OriginalPassword = "Original private password 123!";
    private const string NewPassword = "Replacement private password 456!";

    private static CreateApplicationUserRequest Request(string name = "audit-target") =>
        new(name, "Audit target", OriginalPassword, [SecurityRoleCodes.Operator], [1]);

    [Fact]
    public async Task Unvalidated_claims_are_not_recorded_as_a_trusted_actor()
    {
        await using var env = await IdentityTestEnvironment.CreateAsync();
        var forged = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123456789"),
            new Claim(ClaimTypes.Role, SecurityRoleCodes.Developer)
        }, "Untrusted"));
        var service = env.CreateService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = forged }
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateUserAsync(Request()));
        await using var db = env.CreateDb();
        var entry = await db.AuditLogs.SingleAsync(x => x.EntityType == "UserAdministration");
        Assert.Equal("Anonymous", entry.PerformedBy);
        Assert.False(entry.Success);
        Assert.Null(entry.EntityId);
        Assert.False(await db.ApplicationUsers.AnyAsync(x => x.UserName == "audit-target"));
    }

    [Fact]
    public async Task Successful_administration_records_actor_target_and_safe_metadata()
    {
        await using var env = await IdentityTestEnvironment.CreateAsync();
        var service = env.CreateService();
        await using var db = env.CreateDb();
        var actorId = await db.ApplicationUsers.Select(x => x.Id).SingleAsync();
        var id = await service.CreateUserAsync(Request());
        await service.ResetPasswordAsync(id, NewPassword);
        await service.DeactivateUserAsync(id);
        var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        var events = await db.AuditLogs.Where(x => x.EntityType == "UserAdministration" && x.EntityId == id)
            .OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { "UserCreated", "UserPasswordReset", "UserDeactivated" }, events.Select(x => x.Action));
        Assert.All(events, e =>
        {
            Assert.True(e.Success);
            Assert.Equal($"User:{actorId}", e.PerformedBy);
            Assert.Null(e.CompanyId);
            Assert.NotEqual(default, e.PerformedAtUtc);
            var serialized = System.Text.Json.JsonSerializer.Serialize(e);
            foreach (var secret in new[] { OriginalPassword, NewPassword, user.PasswordHash, user.SecurityStamp })
                Assert.DoesNotContain(secret, serialized);
        });
        Assert.Contains("Operator", events[0].Description);
        Assert.False(user.IsActive);
        Assert.True(PasswordCredentialService.VerifyPassword(NewPassword, user.PasswordHash));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("reset")]
    [InlineData("deactivate")]
    public async Task Audit_insert_failure_rolls_back_the_account_change(string operation)
    {
        await using var env = await IdentityTestEnvironment.CreateAsync();
        var service = env.CreateService();
        var id = await service.CreateUserAsync(Request());
        await using var db = env.CreateDb();
        var before = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        var count = await db.AuditLogs.CountAsync();
        // Disposable schema only: deliberately reject new administration events.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_logs ADD CONSTRAINT test_reject_admin_audit CHECK (entity_type <> 'UserAdministration') NOT VALID");
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (operation == "create") await service.CreateUserAsync(Request("must-not-survive"));
            else if (operation == "reset") await service.ResetPasswordAsync(id, NewPassword);
            else await service.DeactivateUserAsync(id);
        });
        var after = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.ModifiedAtUtc, after.ModifiedAtUtc);
        Assert.False(await db.ApplicationUsers.AnyAsync(x => x.UserName == "must-not-survive"));
        Assert.Equal(count, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Rejected_requests_are_audited_without_mutation_or_submitted_secrets()
    {
        await using var env = await IdentityTestEnvironment.CreateAsync();
        var admin = env.CreateService();
        var id = await admin.CreateUserAsync(Request());
        var viewer = env.CreateService(SecurityRoleCodes.Viewer);
        await using var db = env.CreateDb();
        var before = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.DeactivateUserAsync(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.ResetPasswordAsync(id, NewPassword));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.CreateUserAsync(Request("private-submitted-name")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.ResetPasswordAsync(id, "short"));
        var events = await db.AuditLogs.Where(x => x.EntityType == "UserAdministration" && !x.Success).ToListAsync();
        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.StartsWith("User:", e.PerformedBy));
        var text = System.Text.Json.JsonSerializer.Serialize(events);
        Assert.DoesNotContain(NewPassword, text);
        Assert.DoesNotContain("short", text);
        Assert.DoesNotContain("private-submitted-name", text);
        var after = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.True(after.IsActive);
    }
}
