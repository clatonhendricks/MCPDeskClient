using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using MCPClient.Core.Models;

namespace MCPClient.Core.LlmProviders;

public class AnthropicProvider : ILlmProvider
{
    private AnthropicClient? _client;
    private LlmProviderConfig? _config;
    
    public string Id => "anthropic";
    public string DisplayName => _config?.DisplayName ?? "Anthropic Claude";
    public bool IsConfigured => _client != null && !string.IsNullOrEmpty(_config?.ApiKey);
    public string CurrentModel => _config?.Model ?? "claude-sonnet-4-20250514";
    
    public void SetModel(string modelId)
    {
        if (_config != null) _config.Model = modelId;
    }
    
    public Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> models = new List<ModelInfo>
        {
            new() { Id = "claude-sonnet-4-20250514", DisplayName = "Claude Sonnet 4" },
            new() { Id = "claude-opus-4-20250514", DisplayName = "Claude Opus 4" },
            new() { Id = "claude-haiku-3-20250307", DisplayName = "Claude Haiku 3" },
        };
        return Task.FromResult(models);
    }
    
    public void Configure(LlmProviderConfig config)
    {
        _config = config;
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _client = new AnthropicClient(config.ApiKey);
        }
    }
    
    public async Task<ChatResponse> ChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Anthropic client is not configured");
        
        var messageList = messages.ToList();
        var systemMessage = messageList.FirstOrDefault(m => m.Role == MessageRole.System)?.Content;
        var chatMessages = messageList
            .Where(m => m.Role != MessageRole.System)
            .Select(ConvertMessage)
            .ToList();
        
        var parameters = new MessageParameters
        {
            Model = _config?.Model ?? "claude-sonnet-4-20250514",
            MaxTokens = 4096,
            Messages = chatMessages
        };
        
        if (!string.IsNullOrEmpty(systemMessage))
        {
            parameters.System = new List<SystemMessage> { new SystemMessage(systemMessage) };
        }
        
        // Note: Tool support requires proper schema conversion - simplified for now
        // Full tool support would require mapping to Anthropic's InputSchema format
        
        var result = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        var response = new ChatResponse();
        
        foreach (var content in result.Content)
        {
            if (content is TextContent textContent)
            {
                response.Content += textContent.Text;
            }
            else if (content is ToolUseContent toolUse)
            {
                response.ToolCalls.Add(new ToolCall
                {
                    Id = toolUse.Id,
                    Name = toolUse.Name,
                    Arguments = JsonSerializer.Serialize(toolUse.Input)
                });
            }
        }
        
        return response;
    }
    
    private static Anthropic.SDK.Messaging.Message ConvertMessage(ChatMessage msg)
    {
        var role = msg.Role switch
        {
            MessageRole.User => RoleType.User,
            MessageRole.Tool => RoleType.User,
            _ => RoleType.Assistant
        };
        
        if (msg.Role == MessageRole.Tool && msg.ToolCallId != null)
        {
            return new Anthropic.SDK.Messaging.Message
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new ToolResultContent
                    {
                        ToolUseId = msg.ToolCallId,
                        Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
                    }
                }
            };
        }
        
        return new Anthropic.SDK.Messaging.Message
        {
            Role = role,
            Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
        };
    }
}
