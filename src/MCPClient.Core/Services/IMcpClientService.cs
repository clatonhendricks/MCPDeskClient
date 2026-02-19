using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.LlmProviders;
using MCPClient.Core.Models;

namespace MCPClient.Core.Services;

public interface IMcpClientService
{
    IReadOnlyList<string> ConnectedServers { get; }
    Task ConnectServerAsync(McpServerConfig config, CancellationToken cancellationToken = default);
    Task DisconnectServerAsync(string serverName, CancellationToken cancellationToken = default);
    Task DisconnectAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolDefinition>> GetAllToolsAsync(CancellationToken cancellationToken = default);
    Task<string> CallToolAsync(string toolName, string arguments, CancellationToken cancellationToken = default);
}
