using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

// ---- Config ----
var accessToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") 
    ?? throw new Exception("Set GITHUB_TOKEN environment variable");
var model = Environment.GetEnvironmentVariable("COPILOT_MODEL") ?? "gpt-4o-mini";
var prompt = "Use the Kusto mcp and I want to query two databases WindowCoreEvents for this telemetry \"Microsoft.Windows.Fundamentals.HealthAndExperience.InputDelayClusterDetected\" and Feedback_UIF_Raw table and match all the deviceID in both tables. Show me the output for PrimaryFeedbackTitle, PromotedBugId and PromptedBugLink for the last 30 days";

// ---- Step 1: Exchange OAuth token for Copilot token ----
Console.WriteLine("=== Step 1: Exchanging OAuth token for Copilot token ===");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var tokenReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/v2/token");
tokenReq.Headers.Authorization = new AuthenticationHeaderValue("token", accessToken);
tokenReq.Headers.Add("User-Agent", "MCPClient-Test");
var tokenResp = await http.SendAsync(tokenReq);
var tokenJson = await tokenResp.Content.ReadAsStringAsync();
Console.WriteLine($"Token exchange status: {tokenResp.StatusCode}");
if (!tokenResp.IsSuccessStatusCode) { Console.WriteLine(tokenJson); return; }

using var tokenDoc = JsonDocument.Parse(tokenJson);
var copilotToken = tokenDoc.RootElement.GetProperty("token").GetString()!;
var copilotEndpoint = tokenDoc.RootElement.GetProperty("endpoints").GetProperty("api").GetString()!.TrimEnd('/');
Console.WriteLine($"Endpoint: {copilotEndpoint}");
Console.WriteLine($"Token length: {copilotToken.Length}");

// ---- Step 2: Connect to Kusto MCP ----
Console.WriteLine("\n=== Step 2: Connecting to Kusto MCP server ===");
McpClient? mcpClient = null;
var tools = new List<Dictionary<string, object>>();
var toolNames = new Dictionary<string, string>(); // apiName -> mcpName

try
{
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "kusto-mcp",
        Command = "uvx",
        Arguments = ["azure-kusto-mcp"],
        EnvironmentVariables = new Dictionary<string, string?>
        {
            ["KUSTO_SERVICE_URI"] = "https://wdgeventstore.kusto.windows.net/"
        }
    });
    mcpClient = await McpClient.CreateAsync(transport);
    Console.WriteLine("Connected to kusto-mcp");

    var mcpTools = await mcpClient.ListToolsAsync();
    Console.WriteLine($"Found {mcpTools.Count} tools");
    foreach (var t in mcpTools)
    {
        var apiName = $"kusto-mcp__{t.Name}";
        Console.WriteLine($"  - {apiName}: {t.Description}");
        toolNames[apiName] = t.Name;
        tools.Add(new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object>
            {
                ["name"] = apiName,
                ["description"] = t.Description ?? "",
                ["parameters"] = t.JsonSchema.ValueKind != JsonValueKind.Undefined
                    ? JsonSerializer.Deserialize<JsonElement>(t.JsonSchema.GetRawText())
                    : JsonSerializer.Deserialize<JsonElement>("{}")
            }
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"MCP connection failed: {ex.Message}");
    Console.WriteLine("Continuing without tools...");
}

// ---- Step 3: Chat loop with tool calls ----
Console.WriteLine("\n=== Step 3: Sending chat request ===");
var messages = new List<object>
{
    new Dictionary<string, object?> { ["role"] = "user", ["content"] = prompt }
};

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

int iteration = 0;
while (iteration < 10)
{
    iteration++;
    Console.WriteLine($"\n--- Iteration {iteration} ---");

    var body = new Dictionary<string, object> { ["model"] = model, ["messages"] = messages };
    if (tools.Count > 0) body["tools"] = tools;

    var requestJson = JsonSerializer.Serialize(body, jsonOpts);
    Console.WriteLine($"Request body length: {requestJson.Length}");

    var req = new HttpRequestMessage(HttpMethod.Post, $"{copilotEndpoint}/chat/completions");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", copilotToken);
    req.Headers.Add("editor-version", "vscode/1.96.0");
    req.Headers.Add("editor-plugin-version", "copilot-chat/0.24.0");
    req.Headers.Add("copilot-integration-id", "vscode-chat");
    req.Headers.Add("openai-intent", "conversation-panel");
    req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    Console.WriteLine("Sending request...");
    HttpResponseMessage chatResp;
    try
    {
        chatResp = await http.SendAsync(req);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"HTTP ERROR: {ex.GetType().Name}: {ex.Message}");
        break;
    }

    var respJson = await chatResp.Content.ReadAsStringAsync();
    Console.WriteLine($"Response status: {chatResp.StatusCode}");

    if (!chatResp.IsSuccessStatusCode)
    {
        Console.WriteLine($"ERROR RESPONSE: {respJson}");
        break;
    }

    // Parse response
    using var respDoc = JsonDocument.Parse(respJson);
    var choices = respDoc.RootElement.GetProperty("choices");
    if (choices.GetArrayLength() == 0) { Console.WriteLine("No choices returned!"); break; }

    var choice = choices[0];
    var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : "unknown";
    Console.WriteLine($"Finish reason: {finishReason}");

    var message = choice.GetProperty("message");
    var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
        ? c.GetString() : null;

    if (!string.IsNullOrEmpty(content))
        Console.WriteLine($"Content: {content[..Math.Min(500, content.Length)]}...");

    // Check for tool calls
    if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.GetArrayLength() > 0)
    {
        Console.WriteLine($"Tool calls: {toolCallsEl.GetArrayLength()}");

        // Add assistant message with tool_calls to history
        var assistantMsg = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = content,
            ["tool_calls"] = JsonSerializer.Deserialize<JsonElement>(toolCallsEl.GetRawText())
        };
        messages.Add(assistantMsg);

        foreach (var tc in toolCallsEl.EnumerateArray())
        {
            var tcId = tc.GetProperty("id").GetString()!;
            var fnName = tc.GetProperty("function").GetProperty("name").GetString()!;
            var fnArgs = tc.GetProperty("function").GetProperty("arguments").GetString()!;
            Console.WriteLine($"  Tool: {fnName}, Args: {fnArgs[..Math.Min(200, fnArgs.Length)]}");

            // Execute tool via MCP
            string toolResult;
            if (mcpClient != null && toolNames.TryGetValue(fnName, out var mcpToolName))
            {
                try
                {
                    Console.WriteLine($"  Executing MCP tool '{mcpToolName}'...");
                    var toolArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(fnArgs) ?? new();
                    var result = await mcpClient.CallToolAsync(mcpToolName, toolArgs);
                    toolResult = string.Join("\n", result.Content.Select(x => x.ToString()));
                    if (string.IsNullOrEmpty(toolResult)) toolResult = JsonSerializer.Serialize(result);
                    Console.WriteLine($"  Result length: {toolResult.Length}");
                    Console.WriteLine($"  Result preview: {toolResult[..Math.Min(300, toolResult.Length)]}");
                }
                catch (Exception ex)
                {
                    toolResult = $"Error: {ex.Message}";
                    Console.WriteLine($"  Tool error: {ex.Message}");
                }
            }
            else
            {
                toolResult = "Tool not available";
                Console.WriteLine($"  Tool not found in MCP");
            }

            // Truncate large results
            if (toolResult.Length > 30000)
            {
                Console.WriteLine($"  Truncating from {toolResult.Length} to 30000 chars");
                toolResult = toolResult[..30000] + "\n...[truncated]";
            }

            // Add tool result
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "tool",
                ["content"] = toolResult,
                ["tool_call_id"] = tcId
            });
        }

        // Continue loop to get next response
        continue;
    }

    // No tool calls - final response
    Console.WriteLine($"\n=== FINAL RESPONSE ===");
    Console.WriteLine(content ?? "(empty)");
    break;
}

if (mcpClient != null) await mcpClient.DisposeAsync();
Console.WriteLine("\n=== Done ===");
