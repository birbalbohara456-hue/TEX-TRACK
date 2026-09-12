using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public interface IAssistantModelClient
{
    Task<string> CompleteAsync(
        AssistantProviderSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed class AssistantModelClient(HttpClient httpClient) : IAssistantModelClient
{
    public async Task<string> CompleteAsync(
        AssistantProviderSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var validation = AssistantSettingsStore.Validate(settings);
        if (validation is not null) throw new InvalidOperationException(validation);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        return settings.Provider switch
        {
            AssistantProviderKind.Ollama => await CompleteOllamaAsync(settings, systemPrompt, messages, timeout.Token),
            AssistantProviderKind.OpenAiCompatible => await CompleteOpenAiCompatibleAsync(settings, systemPrompt, messages, timeout.Token),
            _ => throw new InvalidOperationException("Unsupported AI provider.")
        };
    }

    private async Task<string> CompleteOllamaAsync(
        AssistantProviderSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(settings.BaseUrl, "api/chat", "api/chat");
        var requestMessages = new[] { new { role = "system", content = systemPrompt } }
            .Concat(messages.Select(x => new { role = x.Role, content = x.Content }))
            .ToArray();
        using var response = await httpClient.PostAsJsonAsync(endpoint, new
        {
            model = settings.Model,
            messages = requestMessages,
            stream = false,
            options = new { temperature = 0.1 }
        }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim()
               ?? throw new InvalidOperationException("Ollama returned an empty response.");
    }

    private async Task<string> CompleteOpenAiCompatibleAsync(
        AssistantProviderSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(settings.BaseUrl, "v1/chat/completions", "chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        var requestMessages = new[] { new { role = "system", content = systemPrompt } }
            .Concat(messages.Select(x => new { role = x.Role, content = x.Content }))
            .ToArray();
        request.Content = JsonContent.Create(new
        {
            model = settings.Model,
            messages = requestMessages,
            temperature = 0.1
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim()
               ?? throw new InvalidOperationException("The AI provider returned an empty response.");
    }

    public static Uri BuildEndpoint(string baseUrl, string normalPath, string versionedPath)
    {
        var normalized = baseUrl.TrimEnd('/') + "/";
        var baseUri = new Uri(normalized, UriKind.Absolute);
        var path = baseUri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? versionedPath
            : normalPath;
        return new Uri(baseUri, path);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        var safeBody = body.Length > 500 ? body[..500] : body;
        throw new HttpRequestException($"AI provider returned {(int)response.StatusCode}: {safeBody}");
    }
}
