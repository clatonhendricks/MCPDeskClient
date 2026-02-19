using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPClient.Core.Models;

namespace MCPClient.Core.Services;

public interface IConversationService
{
    Task<IReadOnlyList<Conversation>> GetAllConversationsAsync();
    Task<Conversation?> GetConversationAsync(Guid id);
    Task<Conversation> CreateConversationAsync(string? title = null);
    Task UpdateConversationAsync(Conversation conversation);
    Task DeleteConversationAsync(Guid id);
    Task<Message> AddMessageAsync(Guid conversationId, Message message);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId);
}
