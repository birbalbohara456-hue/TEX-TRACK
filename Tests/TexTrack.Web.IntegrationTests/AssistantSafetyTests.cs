using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class AssistantSafetyTests
{
    [Theory]
    [InlineData("http://localhost:11434", "api/chat", "api/chat", "http://localhost:11434/api/chat")]
    [InlineData("https://api.example.test/v1", "v1/chat/completions", "chat/completions", "https://api.example.test/v1/chat/completions")]
    public void ProviderEndpoint_IsNormalizedWithoutDuplicatingV1(string baseUrl, string normalPath, string versionedPath, string expected)
    {
        var endpoint = AssistantModelClient.BuildEndpoint(baseUrl, normalPath, versionedPath);
        Assert.Equal(expected, endpoint.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void OpenAiCompatibleProvider_RequiresApiKey()
    {
        var settings = new AssistantProviderSettings
        {
            Provider = AssistantProviderKind.OpenAiCompatible,
            BaseUrl = "https://api.example.test/v1",
            Model = "test-model",
            ApiKey = string.Empty
        };
        Assert.Equal("An API key is required for the OpenAI-compatible provider.", AssistantSettingsStore.Validate(settings));
    }

    [Fact]
    public void EmbeddedCredentials_AreRejected()
    {
        var settings = new AssistantProviderSettings
        {
            Provider = AssistantProviderKind.Ollama,
            BaseUrl = "http://user:password@localhost:11434",
            Model = "test-model"
        };
        Assert.NotNull(AssistantSettingsStore.Validate(settings));
    }
}
