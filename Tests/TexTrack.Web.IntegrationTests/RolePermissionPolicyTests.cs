using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class RolePermissionPolicyTests
{
    [Theory]
    [InlineData(SecurityRoleCodes.Viewer, SecurityPolicies.ViewReports, true)]
    [InlineData(SecurityRoleCodes.Viewer, SecurityPolicies.OperateVouchers, false)]
    [InlineData(SecurityRoleCodes.Operator, SecurityPolicies.OperateVouchers, true)]
    [InlineData(SecurityRoleCodes.Operator, SecurityPolicies.ManageMasters, false)]
    [InlineData(SecurityRoleCodes.Manager, SecurityPolicies.ManageMasters, true)]
    [InlineData(SecurityRoleCodes.Manager, SecurityPolicies.CancelOrDeleteVouchers, true)]
    [InlineData(SecurityRoleCodes.Manager, SecurityPolicies.DeveloperOnly, false)]
    [InlineData(SecurityRoleCodes.Developer, SecurityPolicies.DeveloperOnly, true)]
    public void Permission_matrix_is_least_privilege(string role, string policy, bool expected)
    {
        Assert.Equal(expected, SecurityPolicies.RoleCanAccess(role, policy));
    }

    // Protected_surface_types_declare_role_policies (H2-1 finding) only checked that policy-name
    // strings appeared in source files - it never proved an unauthorized operation was actually
    // blocked. The ManageMasters (master pages, e.g. StockItems.razor / LedgerRepository) and
    // DeveloperOnly (maintenance pages, e.g. DataMaintenance.razor / AssistantSettingsStore) cases
    // now have real behavioral proof in AuthorizationEnforcementBehaviorTests.cs, and
    // OperateVouchers/CancelOrDeleteVouchers already have real behavioral proof in
    // CurrentCompanyContextSecurityTests.cs. The two remaining checks below stay as source-text
    // assertions deliberately: ViewReports is enforced only at the Razor page level (no
    // service-layer RequirePolicy call exists to test directly), and Program.cs's
    // RequireAuthorization(SecurityPolicies.ExportData) is ASP.NET Core route middleware, not a
    // repository call - proving either behaviorally would need a WebApplicationFactory/TestServer
    // or a Blazor component-testing setup (e.g. bUnit), neither of which exists in this project
    // today. That is a test-infrastructure decision, not a "smallest necessary fix."
    [Fact]
    public async Task Report_and_export_surfaces_declare_their_role_policies()
    {
        var root = FindProjectRoot();
        var program = await File.ReadAllTextAsync(Path.Combine(root, "Program.cs"));
        var report = await File.ReadAllTextAsync(Path.Combine(root, "Components", "Pages", "Reports", "InventoryClosingStock.razor"));

        Assert.Contains("SecurityPolicies.Configure", program, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SecurityPolicies.ExportData)", program, StringComparison.Ordinal);
        Assert.Contains("SecurityPolicies.ViewReports", report, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
