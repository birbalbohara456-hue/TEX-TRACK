using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TexTrack.Web.Data;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class SessionRevocationTests
{
    private const string Password = "Correct Horse Battery Staple";

    [Theory]
    [InlineData("deactivate")]
    [InlineData("password")]
    [InlineData("membership-disabled")]
    [InlineData("membership-deleted")]
    [InlineData("role")]
    [InlineData("company")]
    [InlineData("financial-year")]
    public async Task Cached_circuit_cannot_write_or_read_after_persisted_access_is_revoked(string reason)
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var admin = environment.CreateService();
        var userId = await admin.CreateUserAsync(new("session-owner", "Session Owner", Password,
            [SecurityRoleCodes.Owner], [1]));
        var login = await admin.AuthenticateAsync("session-owner", Password);
        Assert.True(login.Succeeded);
        var principal = login.Principal!;
        var factory = environment.CreateFactory();
        var validator = new PersistedSessionValidator(factory, TimeProvider.System);
        var accessor = new TestCircuitHttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var context = new CurrentCompanyContext(accessor, validator);
        var ledger = new LedgerRepository(factory, context);
        var groupId = await environment.CreateLedgerGroupAsync(1, "Session Revocation Group");
        var before = await ledger.SaveAsync(new LedgerEditModel { Name = "Allowed before revocation", LedgerGroupId = groupId });
        Assert.True(before.Success, before.Message);
        Assert.True(await admin.IsPrincipalValidAsync(principal));

        // Reproduce the original bug: the HTTP request is gone, but the circuit still
        // holds its authenticated claims. We neither replace nor sign out that principal.
        accessor.HttpContext = null;
        Assert.Equal(1, context.CompanyId);
        await context.RequirePolicyAsync(SecurityPolicies.ManageMasters);
        await using (var db = environment.CreateDb())
        {
            switch (reason)
            {
                case "deactivate": await admin.DeactivateUserAsync(userId); break;
                case "password": await admin.ResetPasswordAsync(userId, "Replacement Password For Tests"); break;
                case "membership-disabled":
                    await db.ApplicationUserCompanies.Where(x => x.UserId == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "membership-deleted":
                    await db.ApplicationUserCompanies.Where(x => x.UserId == userId).ExecuteDeleteAsync(); break;
                case "role":
                    await db.ApplicationUserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync(); break;
                case "company":
                    await db.Companies.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "financial-year":
                    var yearId = long.Parse(principal.FindFirstValue(TexTrackClaimTypes.FinancialYearId)!);
                    await db.FinancialYears.Where(x => x.Id == yearId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
            }
        }

        Assert.True(principal.Identity!.IsAuthenticated); // Claims alone still look valid.
        Assert.False(await admin.IsPrincipalValidAsync(principal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ledger.SaveAsync(
            new LedgerEditModel { Name = "Must never persist", LedgerGroupId = groupId }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ledger.GetListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ledger.DeleteAsync(before.EntityId!.Value));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new OperationalReportingService(factory, context).GetLookupsAsync());

        var lifecycle = new VoucherLifecycleService();
        var stock = new StockPostingService();
        var sequence = new VoucherSequenceAllocator(context);
        var mo = new MaterialOutRepository(factory, context, lifecycle, stock, sequence);
        var mi = new MaterialInRepository(factory, context, lifecycle, stock, sequence);
        var jwo = new JobWorkOrderRepository(factory, context, lifecycle, sequence);
        // Invalid payloads make guard ordering observable: authorization must reject
        // before business validation, sequence allocation, transactions or stock writes.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mo.SaveAsync(new MaterialOutSaveRequest()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mo.UpdateAsync(new MaterialOutSaveRequest()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mi.SaveAsync(new MaterialInSaveRequest()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => jwo.SaveAsync(new JobWorkOrderEditModel()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mo.CancelAsync(0, "Test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mi.DeleteAsync(0));
        await using var after = environment.CreateDb();
        Assert.False(await after.Ledgers.AnyAsync(x => x.Name == "Must never persist"));
        Assert.True(await after.Ledgers.AnyAsync(x => x.Id == before.EntityId));
    }

    [Fact]
    public async Task Password_reset_requires_new_login_but_new_session_can_work()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var admin = environment.CreateService();
        var id = await admin.CreateUserAsync(new("reset-owner", "Reset Owner", Password, [SecurityRoleCodes.Owner], [1]));
        var oldLogin = await admin.AuthenticateAsync("reset-owner", Password);
        await admin.ResetPasswordAsync(id, "Replacement Password For Tests");
        Assert.False(await admin.IsPrincipalValidAsync(oldLogin.Principal!));
        Assert.False((await admin.AuthenticateAsync("reset-owner", Password)).Succeeded);
        var newLogin = await admin.AuthenticateAsync("reset-owner", "Replacement Password For Tests");
        Assert.True(newLogin.Succeeded);
        Assert.True(await admin.IsPrincipalValidAsync(newLogin.Principal!));
        var context = new CurrentCompanyContext(new TestCircuitHttpContextAccessor
        { HttpContext = new DefaultHttpContext { User = newLogin.Principal! } },
            new PersistedSessionValidator(environment.CreateFactory(), TimeProvider.System));
        var groupId = await environment.CreateLedgerGroupAsync(1, "Reset Group");
        var result = await new LedgerRepository(environment.CreateFactory(), context).SaveAsync(
            new LedgerEditModel { Name = "Allowed after fresh login", LedgerGroupId = groupId });
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task Revoked_developer_cannot_administer_users_or_confirm_maintenance_password()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var admin = environment.CreateService();
        var id = await admin.CreateUserAsync(new("revoked-developer", "Revoked Developer", Password,
            [SecurityRoleCodes.Developer], [1]));
        var login = await admin.AuthenticateAsync("revoked-developer", Password);
        var accessor = new TestCircuitHttpContextAccessor { HttpContext = new DefaultHttpContext { User = login.Principal! } };
        var oldService = environment.CreateService(accessor);
        accessor.HttpContext = null;
        await admin.ResetPasswordAsync(id, "Replacement Password For Tests");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => oldService.CreateUserAsync(
            new("forbidden-user", "Forbidden", Password, [SecurityRoleCodes.Owner], [1])));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => oldService.DeactivateUserAsync(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => oldService.ResetPasswordAsync(id, Password));
        Assert.False(await oldService.VerifyCurrentUserPasswordAsync(login.Principal!, "Replacement Password For Tests"));
    }

    [Fact]
    public async Task Session_expiry_is_enforced_without_another_HTTP_request()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var admin = environment.CreateService();
        await admin.CreateUserAsync(new("expiry-owner", "Expiry Owner", Password, [SecurityRoleCodes.Owner], [1]));
        var login = await admin.AuthenticateAsync("expiry-owner", Password);
        Assert.True(login.Succeeded);
        var principal = login.Principal!;
        var expiresAt = long.Parse(principal.FindFirstValue(TexTrackClaimTypes.SessionExpiresAt)!);
        var clock = new TestClock { Now = DateTimeOffset.FromUnixTimeSeconds(expiresAt - 1) };
        var context = new CurrentCompanyContext(new TestCircuitHttpContextAccessor
        { HttpContext = new DefaultHttpContext { User = principal } },
            new PersistedSessionValidator(environment.CreateFactory(), clock));
        await context.RequirePolicyAsync(SecurityPolicies.OperateVouchers);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.RequirePolicyAsync(SecurityPolicies.OperateVouchers));
        var legacy = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims.Where(x => x.Type != TexTrackClaimTypes.SessionExpiresAt), "Test"));
        Assert.False(await admin.IsPrincipalValidAsync(legacy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_circuit_becomes_anonymous_when_revalidation_fails(bool databaseFailure)
    {
        var sessions = new ControlledValidator();
        using var provider = new FastRevalidator(sessions);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Circuit User")], "Test"));
        var signedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AuthenticationStateChanged += async task =>
        {
            if (!(await task).User.Identity!.IsAuthenticated) signedOut.TrySetResult();
        };
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        await sessions.Checked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        sessions.Throw = databaseFailure;
        sessions.Valid = false;
        await signedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Validation_database_failure_does_not_authorize_an_operation()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, SecurityRoleCodes.Owner),
            new Claim(TexTrackClaimTypes.CompanyId, "1"),
            new Claim(TexTrackClaimTypes.FinancialYearId, "1")], "Test"));
        var context = new CurrentCompanyContext(new TestCircuitHttpContextAccessor
        { HttpContext = new DefaultHttpContext { User = principal } }, new ControlledValidator { Throw = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RequirePolicyAsync(SecurityPolicies.OperateVouchers));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ControlledValidator : ISessionValidator
    {
        public volatile bool Valid = true;
        public volatile bool Throw;
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("Simulated validation database failure");
            var result = Valid;
            Checked.TrySetResult();
            return Task.FromResult(result);
        }
    }

    private sealed class FastRevalidator(ISessionValidator sessions)
        : TexTrackRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, sessions)
    {
        protected override TimeSpan RevalidationInterval => TimeSpan.FromMilliseconds(10);
    }
}
