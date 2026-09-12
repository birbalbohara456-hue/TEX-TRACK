using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class AssistantSettingsStore
{
    private const string Purpose = "TexTrack.Assistant.ProviderSettings.v1";
    private readonly IDataProtector protector;
    private readonly CurrentCompanyContext companyContext;
    private readonly string settingsPath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public AssistantSettingsStore(IDataProtectionProvider dataProtection, CurrentCompanyContext companyContext)
    {
        this.companyContext = companyContext;
        protector = dataProtection.CreateProtector(Purpose);
        settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TexTrack",
            "assistant-settings.protected");
    }

    public async Task<AssistantSettingsView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        return ToView(settings);
    }

    public async Task<AssistantProviderSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AssistantProviderSettings> ResolveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.DeveloperOnly, cancellationToken);
        var current = await GetAsync(cancellationToken);
        return BuildSettings(update, current);
    }

    public async Task<AssistantOperationResult> SaveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.DeveloperOnly, cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadUnsafeAsync(cancellationToken);
            var settings = BuildSettings(update, current);
            var validation = Validate(settings);
            if (validation is not null) return new(false, validation);

            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var json = JsonSerializer.Serialize(settings);
            var protectedValue = protector.Protect(json);
            await File.WriteAllTextAsync(settingsPath, protectedValue, cancellationToken);
            return new(true, "AI provider settings saved securely on the TexTrack server.");
        }
        catch (Exception ex)
        {
            return new(false, $"Could not save AI settings: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public static string? Validate(AssistantProviderSettings settings)
    {
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return "Base URL must be an absolute HTTP or HTTPS address without embedded credentials.";
        if (string.IsNullOrWhiteSpace(settings.Model)) return "Model name is required.";
        if (settings.TimeoutSeconds is < 10 or > 300) return "Timeout must be between 10 and 300 seconds.";
        if (settings.Provider == AssistantProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(settings.ApiKey))
            return "An API key is required for the OpenAI-compatible provider.";
        return null;
    }

    private async Task<AssistantProviderSettings> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath)) return new();
        try
        {
            var protectedValue = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            var json = protector.Unprotect(protectedValue);
            return JsonSerializer.Deserialize<AssistantProviderSettings>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static AssistantProviderSettings BuildSettings(
        AssistantSettingsUpdate update,
        AssistantProviderSettings current) => new()
    {
        Provider = update.Provider,
        BaseUrl = update.BaseUrl.Trim().TrimEnd('/'),
        Model = update.Model.Trim(),
        TimeoutSeconds = update.TimeoutSeconds,
        ApiKey = update.ClearApiKey
            ? string.Empty
            : string.IsNullOrWhiteSpace(update.ApiKey) ? current.ApiKey : update.ApiKey.Trim()
    };

    private static AssistantSettingsView ToView(AssistantProviderSettings settings) => new()
    {
        Provider = settings.Provider,
        BaseUrl = settings.BaseUrl,
        Model = settings.Model,
        ApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.ApiKey),
        TimeoutSeconds = settings.TimeoutSeconds
    };
}
