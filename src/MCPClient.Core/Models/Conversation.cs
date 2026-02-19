using System;
using System.Collections.Generic;

namespace MCPClient.Core.Models;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Conversation";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LlmProviderId { get; set; }
    
    public List<Message> Messages { get; set; } = new();
}
