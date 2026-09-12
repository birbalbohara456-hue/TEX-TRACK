using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class RequestBoundarySecurityTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/reports/inventory/closing-stock?groupId=1")]
    public void Local_return_urls_are_accepted(string value)
    {
        Assert.True(SecurityRequestGuard.IsLocalReturnUrl(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("reports")]
    [InlineData("//evil.example/path")]
    [InlineData("/\\evil.example/path")]
    [InlineData("https://evil.example/path")]
    public void External_or_malformed_return_urls_are_rejected(string? value)
    {
        Assert.False(SecurityRequestGuard.IsLocalReturnUrl(value));
    }

    [Fact]
    public async Task Cookie_authenticated_posts_validate_antiforgery_tokens()
    {
        var root = FindProjectRoot();
        var program = await File.ReadAllTextAsync(Path.Combine(root, "Program.cs"));
        var loginRenderer = await File.ReadAllTextAsync(Path.Combine(root, "Services", "LoginPageRenderer.cs"));
        var maintenance = await File.ReadAllTextAsync(Path.Combine(
            root, "Components", "Pages", "Settings", "DataMaintenance.razor"));

        Assert.DoesNotContain("DisableAntiforgery", program, StringComparison.Ordinal);
        Assert.Contains("GetAndStoreTokens", loginRenderer, StringComparison.Ordinal);
        Assert.Equal(3, Count(program, "ValidateRequestAsync"));
        Assert.Contains("<AntiforgeryToken />", maintenance, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
