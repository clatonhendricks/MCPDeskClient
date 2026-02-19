namespace MCPClient.Core.Models;

public enum LlmProviderType
{
    OpenAI,
    AzureOpenAI,
    Anthropic,
    Ollama,
    GitHubCopilot
}

public class LlmProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public LlmProviderType Type { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }  // For Azure OpenAI or Ollama
    public bool Enabled { get; set; } = true;
}
