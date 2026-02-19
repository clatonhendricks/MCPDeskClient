using System;

namespace MCPClient.Core.Models;

public enum MessageRole
{
    User,
    Assistant,
    System,
    Tool
}

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // For tool calls/responses
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public string? ToolArguments { get; set; }
    public string? ToolResult { get; set; }
    
    public Conversation? Conversation { get; set; }
}
