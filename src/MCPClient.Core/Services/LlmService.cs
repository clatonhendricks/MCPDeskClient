using System;
using System.Collections.Generic;
using System.Linq;
using MCPClient.Core.LlmProviders;
using MCPClient.Core.Models;

namespace MCPClient.Core.Services;

public interface ILlmService
{
    ILlmProvider? CurrentProvider { get; }
    IReadOnlyList<ILlmProvider> AvailableProviders { get; }
    void SetCurrentProvider(string providerId);
    void ConfigureProviders(AppConfig config);
}

public class LlmService : ILlmService
{
    private readonly Dictionary<string, ILlmProvider> _providers = new();
    private string? _currentProviderId;
    
    public ILlmProvider? CurrentProvider => 
        _currentProviderId != null && _providers.TryGetValue(_currentProviderId, out var provider) 
            ? provider 
            : null;
    
    public IReadOnlyList<ILlmProvider> AvailableProviders => 
        _providers.Values.Where(p => p.IsAuthenticated).ToList();
    
    public LlmService()
    {
        // Register available provider types
        _providers["openai"] = new OpenAiProvider();
        _providers["anthropic"] = new AnthropicProvider();
        _providers["ollama"] = new OllamaProvider();
        _providers["github-copilot"] = new GitHubCopilotProvider();
    }
    
    public void SetCurrentProvider(string providerId)
    {
        if (_providers.ContainsKey(providerId))
        {
            _currentProviderId = providerId;
        }
    }
    
    public void ConfigureProviders(AppConfig config)
    {
        foreach (var (id, providerConfig) in config.LlmProviders)
        {
            if (!providerConfig.Enabled) continue;
            
            // Reuse existing provider instance if already created for this id
            if (!_providers.TryGetValue(id, out var provider))
            {
                provider = providerConfig.Type switch
                {
                    LlmProviderType.OpenAI => new OpenAiProvider(),
                    LlmProviderType.Anthropic => new AnthropicProvider(),
                    LlmProviderType.Ollama => new OllamaProvider(),
                    LlmProviderType.AzureOpenAI => new OpenAiProvider(),
                    LlmProviderType.GitHubCopilot => new GitHubCopilotProvider(),
                    _ => throw new NotSupportedException($"Provider type {providerConfig.Type} is not supported")
                };
                _providers[id] = provider;
            }
            
            // Only configure if not already authenticated (avoid resetting live tokens)
            if (!provider.IsAuthenticated)
            {
                provider.Configure(providerConfig);
            }
        }
        
        if (!string.IsNullOrEmpty(config.DefaultProviderId) && _providers.ContainsKey(config.DefaultProviderId))
        {
            _currentProviderId = config.DefaultProviderId;
        }
        else if (_providers.Count > 0)
        {
            _currentProviderId = _providers.Keys.First();
        }
    }
}
