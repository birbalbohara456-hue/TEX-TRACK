using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class AuthenticationBoundaryTests
{
    [Fact]
    public async Task Application_uses_an_authenticated_fallback_policy_and_authorized_route_view()
    {
        var root = FindProjectRoot();
        var program = await File.ReadAllTextAsync(Path.Combine(root, "Program.cs"));
        var routes = await File.ReadAllTextAsync(Path.Combine(root, "Components", "Routes.razor"));

        Assert.Contains("FallbackPolicy", program, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedUser", program, StringComparison.Ordinal);
        Assert.Contains("<AuthorizeRouteView", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("<RouteView ", routes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_api_endpoints_require_authorization()
    {
        var program = await File.ReadAllTextAsync(Path.Combine(FindProjectRoot(), "Program.cs"));

        Assert.Contains("tallyExportApi.RequireAuthorization(SecurityPolicies.ExportData);", program, StringComparison.Ordinal);
        Assert.Contains("stockItemPhotoApi.RequireAuthorization(SecurityPolicies.ViewReports);", program, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
