using System.Text.Json;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class SecretHygieneTests
{
    [Fact]
    public async Task Repository_configuration_contains_no_embedded_credentials_or_detailed_database_errors()
    {
        var root = FindProjectRoot();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "appsettings.json")));
        var configuration = document.RootElement;
        var connection = configuration.GetProperty("ConnectionStrings").GetProperty("TexTrackDatabase").GetString() ?? string.Empty;

        Assert.DoesNotContain("Password=", connection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pwd=", connection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID=", connection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username=", connection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Include Error Detail=true", connection, StringComparison.OrdinalIgnoreCase);
        Assert.False(configuration.TryGetProperty("DeveloperAccess", out _));
    }

    [Fact]
    public async Task Development_secrets_use_the_dotnet_user_secrets_provider()
    {
        var project = await File.ReadAllTextAsync(Path.Combine(FindProjectRoot(), "TexTrack.Web.csproj"));
        Assert.Contains("<UserSecretsId>", project, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
