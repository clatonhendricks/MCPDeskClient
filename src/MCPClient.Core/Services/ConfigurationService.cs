using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MCPClient.Core.Models;

namespace MCPClient.Core.Services;

public class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    
    private readonly string _configFilePath;
    
    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "MCPDesk");
        Directory.CreateDirectory(configDir);
        _configFilePath = Path.Combine(configDir, "config.json");
        
        // Migrate from old MCPClient config if it exists
        var oldConfigDir = Path.Combine(appData, "MCPClient");
        var oldConfigFile = Path.Combine(oldConfigDir, "config.json");
        if (!File.Exists(_configFilePath) && File.Exists(oldConfigFile))
        {
            File.Copy(oldConfigFile, _configFilePath);
        }
    }
    
    public string GetConfigFilePath() => _configFilePath;
    
    public async Task<AppConfig> LoadConfigAsync()
    {
        if (!File.Exists(_configFilePath))
        {
            return CreateDefaultConfig();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefaultConfig();
            
            // Try to detect and fix standard MCP config format embedded in mcpServers
            NormalizeServerConfigs(json, config);
            
            return config;
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }
    
    /// <summary>
    /// Detects standard MCP config formats (e.g. "mcp": { "server": { ... } } or "mcpServers": { "mcp": { "server": {...} } })
    /// and normalizes them into our flat McpServerConfig format.
    /// </summary>
    private static void NormalizeServerConfigs(string rawJson, AppConfig config)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            
            // Check for standard format nested inside mcpServers: { "mcpServers": { "mcp": { "server": { ... } } } }
            if (root.TryGetProperty("mcpServers", out var mcpServers))
            {
                var serversToAdd = new Dictionary<string, McpServerConfig>();
                var keysToRemove = new List<string>();
                
                foreach (var prop in mcpServers.EnumerateObject())
                {
                    // Check if this entry has a nested "server" or "servers" property (standard MCP format)
                    if (prop.Value.TryGetProperty("server", out var serverBlock) || 
                        prop.Value.TryGetProperty("servers", out serverBlock))
                    {
                        keysToRemove.Add(prop.Name);
                        foreach (var serverProp in serverBlock.EnumerateObject())
                        {
                            var serverConfig = ParseServerConfig(serverProp);
                            if (serverConfig != null)
                            {
                                serversToAdd[serverProp.Name] = serverConfig;
                            }
                        }
                    }
                    // Check if entry is missing "command" (badly formed)
                    else if (!prop.Value.TryGetProperty("command", out _))
                    {
                        keysToRemove.Add(prop.Name);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    config.McpServers.Remove(key);
                }
                foreach (var (name, serverConfig) in serversToAdd)
                {
                    config.McpServers[name] = serverConfig;
                }
            }
            
            // Also check for top-level "mcp" property (Claude Desktop format at root)
            if (root.TryGetProperty("mcp", out var mcpRoot))
            {
                if (mcpRoot.TryGetProperty("server", out var serverBlock) || 
                    mcpRoot.TryGetProperty("servers", out serverBlock))
                {
                    foreach (var serverProp in serverBlock.EnumerateObject())
                    {
                        var serverConfig = ParseServerConfig(serverProp);
                        if (serverConfig != null && !config.McpServers.ContainsKey(serverProp.Name))
                        {
                            config.McpServers[serverProp.Name] = serverConfig;
                        }
                    }
                }
            }
        }
        catch
        {
            // If normalization fails, continue with whatever was deserialized
        }
    }
    
    private static McpServerConfig? ParseServerConfig(JsonProperty serverProp)
    {
        try
        {
            var el = serverProp.Value;
            var config = new McpServerConfig
            {
                Name = serverProp.Name,
                Enabled = true
            };
            
            if (el.TryGetProperty("command", out var cmd))
                config.Command = cmd.GetString() ?? string.Empty;
            else
                return null; // No command = invalid
            
            if (el.TryGetProperty("args", out var args))
            {
                foreach (var arg in args.EnumerateArray())
                {
                    config.Args.Add(arg.GetString() ?? string.Empty);
                }
            }
            
            if (el.TryGetProperty("env", out var env))
            {
                foreach (var envProp in env.EnumerateObject())
                {
                    config.Env[envProp.Name] = envProp.Value.GetString() ?? string.Empty;
                }
            }
            
            return config;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task SaveConfigAsync(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(_configFilePath, json);
    }
    
    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            McpServers = new(),
            LlmProviders = new()
            {
                ["github-copilot"] = new LlmProviderConfig
                {
                    Id = "github-copilot",
                    DisplayName = "GitHub Copilot",
                    Type = LlmProviderType.GitHubCopilot,
                    Model = "gpt-4o",
                    Enabled = true
                },
                ["openai"] = new LlmProviderConfig
                {
                    Id = "openai",
                    DisplayName = "OpenAI",
                    Type = LlmProviderType.OpenAI,
                    Model = "gpt-4",
                    Enabled = false
                }
            },
            DefaultProviderId = "github-copilot"
        };
    }
}
