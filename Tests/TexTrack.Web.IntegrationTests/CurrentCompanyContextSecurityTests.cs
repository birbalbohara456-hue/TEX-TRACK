using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class CurrentCompanyContextSecurityTests
{
    [Fact]
    public void Company_and_financial_year_are_derived_from_immutable_identity_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(TexTrackClaimTypes.CompanyId, "42"),
            new Claim(TexTrackClaimTypes.CompanyName, "Tenant Forty Two"),
            new Claim(TexTrackClaimTypes.FinancialYearId, "7"),
            new Claim(TexTrackClaimTypes.FinancialYearName, "2026-27")
        ], "test"));

        var context = CreateContext(principal);

        Assert.Equal(42, context.CompanyId);
        Assert.Equal("Tenant Forty Two", context.CompanyName);
        Assert.Equal(7, context.FinancialYearId);
        Assert.Equal("2026-27", context.FinancialYearName);
    }

    [Fact]
    public void Missing_tenant_claims_fail_closed_instead_of_falling_back_to_company_one()
    {
        var context = CreateContext(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Throws<UnauthorizedAccessException>(() => context.CompanyId);
        Assert.Throws<UnauthorizedAccessException>(() => context.FinancialYearId);
    }

    [Fact]
    public async Task Operator_cannot_escalate_to_cancel_or_delete_mutations()
    {
        var context = CreateContext(CreatePrincipal(SecurityRoleCodes.Operator));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            context.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers));
    }

    [Fact]
    public async Task Manager_can_perform_cancel_or_delete_mutations()
    {
        var context = CreateContext(CreatePrincipal(SecurityRoleCodes.Manager));

        await context.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers);
    }

    [Fact]
    public void Audit_actor_is_bound_to_immutable_user_id_and_authenticated_name()
    {
        var context = CreateContext(CreatePrincipal(SecurityRoleCodes.Manager));

        Assert.Equal("user:9001:Security Tester", context.Actor);
        Assert.Equal("user:9001:Security Tester via Tally XML", context.ActorFor("Tally XML"));
    }

    [Fact]
    public void Missing_user_identity_claim_fails_closed_for_audit_actor()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(TexTrackClaimTypes.CompanyId, "42"),
            new Claim(TexTrackClaimTypes.CompanyName, "Tenant Forty Two"),
            new Claim(TexTrackClaimTypes.FinancialYearId, "7"),
            new Claim(TexTrackClaimTypes.FinancialYearName, "2026-27"),
            new Claim(ClaimTypes.Role, SecurityRoleCodes.Manager)
        ], "test"));

        Assert.Throws<UnauthorizedAccessException>(() => CreateContext(principal).Actor);
    }

    private static ClaimsPrincipal CreatePrincipal(string role) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, "9001"),
        new Claim(ClaimTypes.Name, "Security Tester"),
        new Claim(TexTrackClaimTypes.CompanyId, "42"),
        new Claim(TexTrackClaimTypes.CompanyName, "Tenant Forty Two"),
        new Claim(TexTrackClaimTypes.FinancialYearId, "7"),
        new Claim(TexTrackClaimTypes.FinancialYearName, "2026-27"),
        new Claim(ClaimTypes.Role, role)
    ], "test"));

    private static CurrentCompanyContext CreateContext(ClaimsPrincipal principal) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = principal }
    }, new BusinessRuleTestSessionValidator());
}
