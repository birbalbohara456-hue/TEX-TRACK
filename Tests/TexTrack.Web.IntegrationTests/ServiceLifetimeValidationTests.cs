using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class ServiceLifetimeValidationTests
{
    [Fact]
    public async Task Program_registers_assistant_settings_store_as_scoped()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(FindProjectRoot(), "Program.cs"));

        Assert.Contains("AddScoped<AssistantSettingsStore>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<AssistantSettingsStore>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Assistant_settings_store_resolves_with_scope_validation_enabled()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddScoped<ISessionValidator, BusinessRuleTestSessionValidator>();
        services.AddScoped<CurrentCompanyContext>();
        services.AddScoped<AssistantSettingsStore>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AssistantSettingsStore>());
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
