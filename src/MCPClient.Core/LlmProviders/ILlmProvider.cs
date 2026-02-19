using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.Models;

namespace MCPClient.Core.LlmProviders;

public class ChatMessage
{
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public string? ToolArguments { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParametersJsonSchema { get; set; } = "{}";
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}

public class ChatResponse
{
    public string Content { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool RequiresToolExecution => ToolCalls.Count > 0;
}

public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

public interface ILlmProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool IsConfigured { get; }
    bool IsAuthenticated => IsConfigured;
    string CurrentModel { get; }
    
    Task<ChatResponse> ChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);
    
    void Configure(LlmProviderConfig config);
    void SetModel(string modelId);
    Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
}
