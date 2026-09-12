using Microsoft.Extensions.Configuration;

namespace TexTrack.Web.IntegrationTests;

internal static class TestDatabaseSettings
{
    public static string GetConnectionString()
    {
        var userSecretsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", "TexTrack-Web-20260828", "secrets.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .AddJsonFile(userSecretsPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("TexTrackDatabase")
            ?? throw new InvalidOperationException(
                "TexTrackDatabase is missing. Configure it with .NET User Secrets or the ConnectionStrings__TexTrackDatabase environment variable.");
    }
}
