using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPClient.Core.LlmProviders;
using MCPClient.Core.Models;
using MCPClient.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MCPClient.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IConversationService _conversationService;
    private readonly ILlmService _llmService;
    private readonly IMcpClientService _mcpClientService;
    
    [ObservableProperty]
    private Conversation? _currentConversation;
    
    [ObservableProperty]
    private ObservableCollection<Message> _messages = new();
    
    [ObservableProperty]
    private string _inputText = string.Empty;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string? _errorMessage;
    
    [ObservableProperty]
    private ILlmProvider? _selectedProvider;
    
    public ObservableCollection<ILlmProvider> AvailableProviders { get; } = new();
    
    public ChatViewModel(
        IConversationService conversationService,
        ILlmService llmService,
        IMcpClientService mcpClientService)
    {
        _conversationService = conversationService;
        _llmService = llmService;
        _mcpClientService = mcpClientService;
        
        RefreshProviders();
    }
    
    public void RefreshProviders()
    {
        AvailableProviders.Clear();
        foreach (var provider in _llmService.AvailableProviders)
        {
            AvailableProviders.Add(provider);
        }
        SelectedProvider = _llmService.CurrentProvider;
    }
    
    public async Task LoadConversationAsync(Guid conversationId)
    {
        CurrentConversation = await _conversationService.GetConversationAsync(conversationId);
        Messages.Clear();
        
        if (CurrentConversation != null)
        {
            foreach (var msg in CurrentConversation.Messages)
            {
                Messages.Add(msg);
            }
        }
    }
    
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || CurrentConversation == null)
            return;
        
        if (SelectedProvider == null || !SelectedProvider.IsConfigured)
        {
            ErrorMessage = "Please configure an LLM provider in Settings";
            return;
        }
        
        ErrorMessage = null;
        IsLoading = true;
        
        try
        {
            // Add user message
            var userMessage = new Message
            {
                Role = MessageRole.User,
                Content = InputText
            };
            await _conversationService.AddMessageAsync(CurrentConversation.Id, userMessage);
            Messages.Add(userMessage);
            
            var userInput = InputText;
            InputText = string.Empty;
            
            // Get available tools from MCP servers
            var tools = await _mcpClientService.GetAllToolsAsync();
            
            // Build chat messages from history with proper tool_calls structure
            var chatMessages = BuildChatMessagesFromHistory(Messages);
            
            // Send to LLM
            AddStatusMessage($"⏳ Sending to {SelectedProvider.DisplayName} ({SelectedProvider.CurrentModel})...");
            var response = await SelectedProvider.ChatAsync(chatMessages, tools);
            
            // Handle tool calls if any (max 10 iterations to prevent infinite loops)
            int toolIterations = 0;
            while (response.RequiresToolExecution && toolIterations < 10)
            {
                toolIterations++;
                
                // Add assistant message with tool_calls to chatMessages
                chatMessages.Add(new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = response.Content ?? string.Empty,
                    ToolCalls = response.ToolCalls.ToList()
                });
                
                foreach (var toolCall in response.ToolCalls)
                {
                    // Add assistant message with tool call (for UI display)
                    var toolCallMessage = new Message
                    {
                        Role = MessageRole.Assistant,
                        Content = $"🔧 Calling tool: {toolCall.Name}\nArgs: {toolCall.Arguments}",
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        ToolArguments = toolCall.Arguments
                    };
                    await _conversationService.AddMessageAsync(CurrentConversation.Id, toolCallMessage);
                    Messages.Add(toolCallMessage);
                    ScrollRequested?.Invoke();
                    
                    // Execute tool
                    string toolResult;
                    try
                    {
                        AddStatusMessage($"⏳ Executing tool '{toolCall.Name}'...");
                        toolResult = await _mcpClientService.CallToolAsync(toolCall.Name, toolCall.Arguments);
                        AddStatusMessage($"✅ Tool returned {toolResult.Length} chars");
                    }
                    catch (Exception ex)
                    {
                        toolResult = $"Tool execution error: {ex.Message}";
                        AddStatusMessage($"❌ Tool error: {ex.Message}");
                    }
                    
                    // Truncate very large tool results to avoid API limits
                    if (toolResult.Length > 30000)
                    {
                        toolResult = toolResult[..30000] + "\n... [truncated]";
                    }
                    
                    // Add tool result message
                    var toolResultMessage = new Message
                    {
                        Role = MessageRole.Tool,
                        Content = toolResult.Length > 500 
                            ? toolResult[..500] + $"\n... ({toolResult.Length} chars total)"
                            : toolResult,
                        ToolCallId = toolCall.Id,
                        ToolResult = toolResult
                    };
                    await _conversationService.AddMessageAsync(CurrentConversation.Id, toolResultMessage);
                    Messages.Add(toolResultMessage);
                    ScrollRequested?.Invoke();
                    
                    chatMessages.Add(new ChatMessage
                    {
                        Role = MessageRole.Tool,
                        Content = toolResult,
                        ToolCallId = toolCall.Id
                    });
                }
                
                // Continue conversation with tool results
                AddStatusMessage($"⏳ Sending tool results to model (iteration {toolIterations})...");
                response = await SelectedProvider.ChatAsync(chatMessages, tools);
                AddStatusMessage($"✅ Model responded (finish: {(response.RequiresToolExecution ? "tool_calls" : "stop")})");
            }
            
            // Remove status messages before adding final response
            RemoveStatusMessages();
            
            // Add assistant response
            var assistantMessage = new Message
            {
                Role = MessageRole.Assistant,
                Content = response.Content
            };
            await _conversationService.AddMessageAsync(CurrentConversation.Id, assistantMessage);
            Messages.Add(assistantMessage);
            ScrollRequested?.Invoke();
            
            // Update conversation title if first message
            if (Messages.Count(m => m.Role == MessageRole.User) == 1)
            {
                CurrentConversation.Title = userInput.Length > 50 
                    ? userInput[..50] + "..." 
                    : userInput;
                await _conversationService.UpdateConversationAsync(CurrentConversation);
            }
        }
        catch (Exception ex)
        {
            RemoveStatusMessages();
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public event Action? ScrollRequested;
    
    private void AddStatusMessage(string text)
    {
        // Add or update a status message in the UI
        var existing = Messages.FirstOrDefault(m => m.Role == MessageRole.System && m.Content.StartsWith("⏳"));
        if (existing != null)
        {
            existing.Content = text;
            // Force UI update by removing and re-adding
            var idx = Messages.IndexOf(existing);
            Messages.RemoveAt(idx);
            Messages.Add(existing);
        }
        else
        {
            Messages.Add(new Message { Role = MessageRole.System, Content = text });
        }
        ScrollRequested?.Invoke();
    }
    
    private void RemoveStatusMessages()
    {
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].Role == MessageRole.System && 
                (Messages[i].Content.StartsWith("⏳") || Messages[i].Content.StartsWith("✅") || Messages[i].Content.StartsWith("❌")))
            {
                Messages.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// Rebuilds chat messages from stored Message history with proper tool_calls structure.
    /// Stored Messages have tool calls split into individual messages; the API needs them
    /// combined into a single assistant message with a tool_calls array.
    /// </summary>
    private static System.Collections.Generic.List<ChatMessage> BuildChatMessagesFromHistory(
        ObservableCollection<Message> messages)
    {
        var result = new System.Collections.Generic.List<ChatMessage>();
        int i = 0;
        
        while (i < messages.Count)
        {
            var msg = messages[i];
            
            // Detect assistant tool-call messages (have ToolName set)
            if (msg.Role == MessageRole.Assistant && !string.IsNullOrEmpty(msg.ToolName))
            {
                // Collect consecutive assistant tool-call messages into one ChatMessage
                var toolCalls = new System.Collections.Generic.List<ToolCall>();
                while (i < messages.Count 
                       && messages[i].Role == MessageRole.Assistant 
                       && !string.IsNullOrEmpty(messages[i].ToolName))
                {
                    var tcMsg = messages[i];
                    toolCalls.Add(new ToolCall
                    {
                        Id = tcMsg.ToolCallId ?? $"call_{i}",
                        Name = tcMsg.ToolName!,
                        Arguments = tcMsg.ToolArguments ?? "{}"
                    });
                    i++;
                }
                
                result.Add(new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = string.Empty,
                    ToolCalls = toolCalls
                });
            }
            else if (msg.Role == MessageRole.Tool)
            {
                result.Add(new ChatMessage
                {
                    Role = MessageRole.Tool,
                    Content = msg.Content,
                    ToolCallId = msg.ToolCallId
                });
                i++;
            }
            else
            {
                result.Add(new ChatMessage
                {
                    Role = msg.Role,
                    Content = msg.Content
                });
                i++;
            }
        }
        
        return result;
    }
}
