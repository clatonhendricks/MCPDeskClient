using System.Collections.Generic;

namespace MCPClient.Core.Models;

public class AppConfig
{
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new();
    public Dictionary<string, LlmProviderConfig> LlmProviders { get; set; } = new();
    public string? DefaultProviderId { get; set; }
}
