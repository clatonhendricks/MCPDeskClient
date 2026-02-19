using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.Models;
using OpenAI;
using OpenAI.Chat;

namespace MCPClient.Core.LlmProviders;

public class OpenAiProvider : ILlmProvider
{
    private ChatClient? _client;
    private LlmProviderConfig? _config;
    
    public string Id => "openai";
    public string DisplayName => _config?.DisplayName ?? "OpenAI";
    public bool IsConfigured => _client != null && !string.IsNullOrEmpty(_config?.ApiKey);
    public string CurrentModel => _config?.Model ?? "gpt-4";
    
    public void SetModel(string modelId)
    {
        if (_config != null) { _config.Model = modelId; Configure(_config); }
    }
    
    public Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> models = new List<ModelInfo>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o" },
            new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o mini" },
            new() { Id = "gpt-4", DisplayName = "GPT-4" },
            new() { Id = "o3-mini", DisplayName = "o3-mini" },
        };
        return Task.FromResult(models);
    }
    
    public void Configure(LlmProviderConfig config)
    {
        _config = config;
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            var openAiClient = new OpenAIClient(config.ApiKey);
            _client = openAiClient.GetChatClient(config.Model ?? "gpt-4");
        }
    }
    
    public async Task<ChatResponse> ChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
            throw new InvalidOperationException("OpenAI client is not configured");
        
        var chatMessages = messages.Select(ConvertMessage).ToList();
        
        var options = new ChatCompletionOptions();
        
        if (tools != null)
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.ParametersJsonSchema)));
            }
        }
        
        var completion = await _client.CompleteChatAsync(chatMessages, options, cancellationToken);
        var response = new ChatResponse();
        
        foreach (var part in completion.Value.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text)
            {
                response.Content += part.Text;
            }
        }
        
        foreach (var toolCall in completion.Value.ToolCalls)
        {
            response.ToolCalls.Add(new ToolCall
            {
                Id = toolCall.Id,
                Name = toolCall.FunctionName,
                Arguments = toolCall.FunctionArguments.ToString()
            });
        }
        
        return response;
    }
    
    private static OpenAI.Chat.ChatMessage ConvertMessage(ChatMessage msg)
    {
        return msg.Role switch
        {
            MessageRole.User => new UserChatMessage(msg.Content),
            MessageRole.Assistant when msg.ToolCallId != null => 
                new ToolChatMessage(msg.ToolCallId, msg.Content),
            MessageRole.Assistant => new AssistantChatMessage(msg.Content),
            MessageRole.System => new SystemChatMessage(msg.Content),
            MessageRole.Tool => new ToolChatMessage(msg.ToolCallId ?? "", msg.Content),
            _ => new UserChatMessage(msg.Content)
        };
    }
}
