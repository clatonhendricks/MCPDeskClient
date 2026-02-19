using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.Models;

namespace MCPClient.Core.LlmProviders;

public class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private LlmProviderConfig? _config;
    
    public string Id => "ollama";
    public string DisplayName => _config?.DisplayName ?? "Ollama (Local)";
    public bool IsConfigured => !string.IsNullOrEmpty(_config?.Endpoint);
    public string CurrentModel => _config?.Model ?? "llama2";
    
    public void SetModel(string modelId)
    {
        if (_config != null) _config.Model = modelId;
    }
    
    public Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> models = new List<ModelInfo>
        {
            new() { Id = "llama2", DisplayName = "Llama 2" },
            new() { Id = "mistral", DisplayName = "Mistral" },
            new() { Id = "codellama", DisplayName = "Code Llama" },
        };
        return Task.FromResult(models);
    }
    
    public OllamaProvider()
    {
        _httpClient = new HttpClient();
    }
    
    public void Configure(LlmProviderConfig config)
    {
        _config = config;
        var endpoint = config.Endpoint ?? "http://localhost:11434";
        _httpClient.BaseAddress = new Uri(endpoint);
    }
    
    public async Task<ChatResponse> ChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Ollama provider is not configured");
        
        var request = new OllamaChatRequest
        {
            Model = _config.Model ?? "llama2",
            Messages = messages.Select(m => new OllamaMessage
            {
                Role = m.Role switch
                {
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    MessageRole.System => "system",
                    _ => "user"
                },
                Content = m.Content
            }).ToList(),
            Stream = false
        };
        
        var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        
        return new ChatResponse
        {
            Content = result?.Message?.Content ?? string.Empty
        };
    }
    
    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
        
        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = new();
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }
    
    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
    
    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }
}
