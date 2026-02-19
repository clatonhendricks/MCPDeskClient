using System.Collections.Generic;

namespace MCPClient.Core.Models;

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
