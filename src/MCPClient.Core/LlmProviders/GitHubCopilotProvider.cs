using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MCPClient.Core.Models;
using OpenAI;
using OpenAI.Chat;

namespace MCPClient.Core.LlmProviders;

public class GitHubCopilotProvider : ILlmProvider
{
    private const string GitHubClientId = "Iv1.b507a08c87ecfe98";
    private const string CopilotTokenUrl = "https://api.github.com/copilot_internal/v2/token";
    private const string GitHubModelsEndpoint = "https://models.github.ai/v1";
    
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    private ChatClient? _client; // Used only for PAT + GitHub Models path
    private LlmProviderConfig? _config;
    private string? _accessToken;
    private string? _copilotToken;
    private string? _copilotEndpoint;
    private DateTime _copilotTokenExpiry = DateTime.MinValue;
    private Task? _initTask;
    private bool _useCopilotApi;
    
    public string Id => "github-copilot";
    public string DisplayName => _config?.DisplayName ?? "GitHub Copilot";
    public bool IsConfigured => _client != null || (_useCopilotApi && !string.IsNullOrEmpty(_copilotToken));
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
    public string CurrentModel => _config?.Model ?? "gpt-4o";
    
    public event Action<DeviceFlowInfo>? DeviceFlowAuthRequired;
    public event Action? AuthenticationCompleted;
    
    public void SetModel(string modelId)
    {
        if (_config != null) _config.Model = modelId;
    }
    
    public async Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_initTask != null) { await _initTask; _initTask = null; }
        
        // Refresh token if needed
        if (_useCopilotApi && DateTime.UtcNow >= _copilotTokenExpiry.AddMinutes(-2))
            await RefreshCopilotTokenAsync();
        
        if (_useCopilotApi && !string.IsNullOrEmpty(_copilotToken) && !string.IsNullOrEmpty(_copilotEndpoint))
        {
            return await FetchCopilotModelsAsync(cancellationToken);
        }
        
        // Fallback: static list for GitHub Models
        return new List<ModelInfo>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o" },
            new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o mini" },
            new() { Id = "o3-mini", DisplayName = "o3-mini" },
        };
    }
    
    private async Task<IReadOnlyList<ModelInfo>> FetchCopilotModelsAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_copilotEndpoint}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _copilotToken);
        request.Headers.Add("editor-version", "vscode/1.96.0");
        request.Headers.Add("copilot-integration-id", "vscode-chat");
        
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new List<ModelInfo> { new() { Id = "gpt-4o", DisplayName = "GPT-4o" } };
        
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var models = new List<ModelInfo>();
        
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var m in data.EnumerateArray())
            {
                var id = m.GetProperty("id").GetString() ?? "";
                var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                // Skip embedding models
                if (id.Contains("embedding", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Contains("goldeneye", StringComparison.OrdinalIgnoreCase)) continue;
                models.Add(new ModelInfo { Id = id, DisplayName = name });
            }
        }
        
        // Deduplicate by id, prefer entries with nicer names
        var deduped = models.GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.DisplayName).ToList();
        return deduped;
    }
    
    public void Configure(LlmProviderConfig config)
    {
        _config = config;
        
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _accessToken = config.ApiKey;
            
            if (config.ApiKey.StartsWith("ghp_") || config.ApiKey.StartsWith("github_pat_"))
            {
                _useCopilotApi = false;
                InitializeOpenAiClient(config.Model, config.ApiKey, GitHubModelsEndpoint);
            }
            else
            {
                _useCopilotApi = true;
                _initTask = RefreshCopilotTokenAsync();
            }
        }
    }
    
    private async Task RefreshCopilotTokenAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, CopilotTokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("token", _accessToken);
            request.Headers.Add("User-Agent", "MCPDesk");
            
            var response = await httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var copilotToken = await response.Content.ReadFromJsonAsync<CopilotTokenResponse>();
                if (copilotToken?.Token != null)
                {
                    _copilotToken = copilotToken.Token;
                    _copilotEndpoint = copilotToken.Endpoints?.Api?.TrimEnd('/') 
                                      ?? "https://api.individual.githubcopilot.com";
                    _copilotTokenExpiry = DateTimeOffset.FromUnixTimeSeconds(copilotToken.ExpiresAt).UtcDateTime;
                    return;
                }
            }
            
            // Fallback to GitHub Models
            _useCopilotApi = false;
            InitializeOpenAiClient(_config?.Model, _accessToken!, GitHubModelsEndpoint);
        }
        catch
        {
            _useCopilotApi = false;
            if (!string.IsNullOrEmpty(_accessToken))
                InitializeOpenAiClient(_config?.Model, _accessToken, GitHubModelsEndpoint);
        }
    }
    
    public async Task AuthenticateWithDeviceFlowAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var deviceCodeResponse = await httpClient.PostAsync(
            "https://github.com/login/device/code",
            JsonContent.Create(new { client_id = GitHubClientId, scope = "read:user" }),
            cancellationToken);
        deviceCodeResponse.EnsureSuccessStatusCode();
        
        var deviceCode = await deviceCodeResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);
        if (deviceCode == null)
            throw new InvalidOperationException("Failed to get device code from GitHub");
        
        DeviceFlowAuthRequired?.Invoke(new DeviceFlowInfo
        {
            VerificationUri = deviceCode.VerificationUri,
            UserCode = deviceCode.UserCode
        });
        
        var interval = deviceCode.Interval > 0 ? deviceCode.Interval : 5;
        string? accessToken = null;
        
        while (accessToken == null && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            
            var tokenResponse = await httpClient.PostAsync(
                "https://github.com/login/oauth/access_token",
                JsonContent.Create(new
                {
                    client_id = GitHubClientId,
                    device_code = deviceCode.DeviceCode,
                    grant_type = "urn:ietf:params:oauth:grant-type:device_code"
                }),
                cancellationToken);
            
            var tokenResult = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            
            if (tokenResult?.AccessToken != null)
                accessToken = tokenResult.AccessToken;
            else if (tokenResult?.Error == "slow_down")
                interval += 5;
            else if (tokenResult?.Error != "authorization_pending")
                throw new InvalidOperationException($"GitHub auth failed: {tokenResult?.Error} - {tokenResult?.ErrorDescription}");
        }
        
        _accessToken = accessToken ?? throw new OperationCanceledException("Authentication was cancelled");
        _useCopilotApi = true;
        await RefreshCopilotTokenAsync();
        
        if (_config != null)
            _config.ApiKey = _accessToken;
        
        AuthenticationCompleted?.Invoke();
    }
    
    private void InitializeOpenAiClient(string? model, string token, string endpoint)
    {
        if (string.IsNullOrEmpty(token)) return;
        var resolvedModel = string.IsNullOrEmpty(model) ? "gpt-4o" : model;
        var credential = new System.ClientModel.ApiKeyCredential(token);
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var openAiClient = new OpenAIClient(credential, options);
        _client = openAiClient.GetChatClient(resolvedModel);
    }
    
    public async Task<ChatResponse> ChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        if (_initTask != null)
        {
            await _initTask;
            _initTask = null;
        }
        
        if (_useCopilotApi)
            return await CopilotChatAsync(messages, tools, cancellationToken);
        
        return await OpenAiSdkChatAsync(messages, tools, cancellationToken);
    }
    
    /// <summary>
    /// Chat via Copilot API (direct HTTP, no OpenAI SDK — avoids /v1/ path issue).
    /// </summary>
    private async Task<ChatResponse> CopilotChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        // Refresh token if expired
        if (DateTime.UtcNow >= _copilotTokenExpiry.AddMinutes(-2))
            await RefreshCopilotTokenAsync();
        
        if (string.IsNullOrEmpty(_copilotToken) || string.IsNullOrEmpty(_copilotEndpoint))
            throw new InvalidOperationException("Copilot is not authenticated. Please sign in.");
        
        var model = string.IsNullOrEmpty(_config?.Model) ? "gpt-4o" : _config.Model;
        
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m =>
            {
                var msg = new Dictionary<string, object?>
                {
                    ["role"] = m.Role switch
                    {
                        MessageRole.User => "user",
                        MessageRole.Assistant => "assistant",
                        MessageRole.System => "system",
                        MessageRole.Tool => "tool",
                        _ => "user"
                    }
                };
                
                // Assistant messages with tool_calls need special handling
                if (m.Role == MessageRole.Assistant && m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    msg["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content;
                    msg["tool_calls"] = m.ToolCalls.Select(tc => new Dictionary<string, object>
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, string>
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = tc.Arguments
                        }
                    }).ToList();
                }
                else if (m.Role == MessageRole.Tool)
                {
                    msg["content"] = m.Content;
                    msg["tool_call_id"] = m.ToolCallId ?? "";
                }
                else
                {
                    msg["content"] = m.Content;
                }
                
                return msg;
            }).ToList()
        };
        
        if (tools != null && tools.Any())
        {
            requestBody["tools"] = tools.Select(t => new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
                }
            }).ToList();
        }
        
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_copilotEndpoint}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _copilotToken);
        request.Headers.Add("editor-version", "vscode/1.96.0");
        request.Headers.Add("editor-plugin-version", "copilot-chat/0.24.0");
        request.Headers.Add("copilot-integration-id", "vscode-chat");
        request.Headers.Add("openai-intent", "conversation-panel");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOpts), 
            Encoding.UTF8, "application/json");
        
        var httpResponse = await httpClient.SendAsync(request, cancellationToken);
        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        
        if (!httpResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Copilot API error {httpResponse.StatusCode}: {responseJson}");
        
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var response = new ChatResponse();
        
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    response.Content = content.GetString() ?? string.Empty;
                
                if (message.TryGetProperty("tool_calls", out var toolCalls))
                {
                    foreach (var tc in toolCalls.EnumerateArray())
                    {
                        var fn = tc.GetProperty("function");
                        response.ToolCalls.Add(new ToolCall
                        {
                            Id = tc.GetProperty("id").GetString() ?? "",
                            Name = fn.GetProperty("name").GetString() ?? "",
                            Arguments = fn.GetProperty("arguments").GetString() ?? "{}"
                        });
                    }
                }
            }
        }
        
        return response;
    }
    
    /// <summary>
    /// Chat via OpenAI SDK (for PAT + GitHub Models path).
    /// </summary>
    private async Task<ChatResponse> OpenAiSdkChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        if (_client == null)
            throw new InvalidOperationException("GitHub Copilot is not configured. Please sign in first.");
        
        var chatMessages = messages.Select(ConvertMessage).ToList();
        var options = new ChatCompletionOptions();
        
        if (tools != null)
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.Name, tool.Description,
                    BinaryData.FromString(tool.ParametersJsonSchema)));
            }
        }
        
        var completion = await _client.CompleteChatAsync(chatMessages, options, cancellationToken);
        var response = new ChatResponse();
        
        foreach (var part in completion.Value.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text)
                response.Content += part.Text;
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
    
    #region JSON Response Models
    
    private class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = string.Empty;
        [JsonPropertyName("user_code")] public string UserCode { get; set; } = string.Empty;
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }
    
    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
    
    private class CopilotTokenResponse
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
        [JsonPropertyName("endpoints")] public CopilotEndpoints? Endpoints { get; set; }
    }
    
    private class CopilotEndpoints
    {
        [JsonPropertyName("api")] public string? Api { get; set; }
    }
    
    #endregion
}

public class DeviceFlowInfo
{
    public string VerificationUri { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
}
