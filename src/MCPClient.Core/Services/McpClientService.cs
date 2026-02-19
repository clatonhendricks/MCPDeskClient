using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.LlmProviders;
using MCPClient.Core.Models;
using ModelContextProtocol.Client;

namespace MCPClient.Core.Services;

public class McpClientService : IMcpClientService, IAsyncDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, List<ToolDefinition>> _serverTools = new();
    
    public IReadOnlyList<string> ConnectedServers => _clients.Keys.ToList();
    
    public async Task ConnectServerAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new InvalidOperationException($"Server '{config.Name}' has no command specified.");
        }
        
        if (_clients.ContainsKey(config.Name))
        {
            await DisconnectServerAsync(config.Name, cancellationToken);
        }
        
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = config.Args.ToArray(),
            EnvironmentVariables = config.Env
        });
        
        var client = await McpClient.CreateAsync(clientTransport);
        _clients[config.Name] = client;
        
        // Discover tools from this server
        try
        {
            var tools = await client.ListToolsAsync();
            _serverTools[config.Name] = tools.Select(t => new ToolDefinition
            {
                Name = $"{config.Name}__{t.Name}",
                Description = t.Description ?? string.Empty,
                ParametersJsonSchema = t.JsonSchema.ValueKind != System.Text.Json.JsonValueKind.Undefined 
                    ? t.JsonSchema.GetRawText() : "{}"
            }).ToList();
        }
        catch (Exception)
        {
            // Server connected but tool discovery failed — store empty tool list
            _serverTools[config.Name] = new List<ToolDefinition>();
            throw;
        }
    }
    
    public async Task DisconnectServerAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (_clients.TryGetValue(serverName, out var client))
        {
            await client.DisposeAsync();
            _clients.Remove(serverName);
            _serverTools.Remove(serverName);
        }
    }
    
    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var serverName in _clients.Keys.ToList())
        {
            await DisconnectServerAsync(serverName, cancellationToken);
        }
    }
    
    public Task<IReadOnlyList<ToolDefinition>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        var allTools = _serverTools.Values.SelectMany(t => t).ToList();
        return Task.FromResult<IReadOnlyList<ToolDefinition>>(allTools);
    }
    
    public async Task<string> CallToolAsync(string toolName, string arguments, CancellationToken cancellationToken = default)
    {
        // Tool name format: "serverName__toolName"
        var separatorIndex = toolName.IndexOf("__", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw new ArgumentException($"Invalid tool name format: {toolName}. Expected 'serverName__toolName'");
        }
        
        var serverName = toolName[..separatorIndex];
        var actualToolName = toolName[(separatorIndex + 2)..];
        
        if (!_clients.TryGetValue(serverName, out var client))
        {
            throw new InvalidOperationException($"Server '{serverName}' is not connected");
        }
        
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments) ?? new();
        var result = await client.CallToolAsync(actualToolName, args);
        
        // Convert result to string - the content is typically TextContent or similar
        var resultStr = string.Join("\n", result.Content.Select(c => c.ToString()));
        return string.IsNullOrEmpty(resultStr) ? JsonSerializer.Serialize(result) : resultStr;
    }
    
    public async ValueTask DisposeAsync()
    {
        await DisconnectAllAsync();
        GC.SuppressFinalize(this);
    }
}
