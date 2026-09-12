namespace TexTrack.Web.Models;

public enum AssistantProviderKind
{
    Ollama,
    OpenAiCompatible
}

public sealed class AssistantProviderSettings
{
    public AssistantProviderKind Provider { get; set; } = AssistantProviderKind.Ollama;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 90;
}

public sealed class AssistantSettingsView
{
    public AssistantProviderKind Provider { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool ApiKeyConfigured { get; set; }
    public int TimeoutSeconds { get; set; }
}

public sealed class AssistantSettingsUpdate
{
    public AssistantProviderKind Provider { get; set; } = AssistantProviderKind.Ollama;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool ClearApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 90;
}

public sealed record AssistantChatMessage(string Role, string Content);
public sealed record AssistantSourceLink(string Label, string Url);
public sealed record AssistantAnswer(string Content, IReadOnlyList<AssistantSourceLink> Sources);
public sealed record AssistantEvidence(string Text, IReadOnlyList<AssistantSourceLink> Sources);
public sealed record AssistantOperationResult(bool Success, string Message);

