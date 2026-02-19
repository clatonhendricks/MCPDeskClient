using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPClient.Core.Models;
using MCPClient.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MCPClient.Data;

public class ConversationService : IConversationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    
    public ConversationService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }
    
    public async Task<IReadOnlyList<Conversation>> GetAllConversationsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Conversations
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }
    
    public async Task<Conversation?> GetConversationAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == id);
    }
    
    public async Task<Conversation> CreateConversationAsync(string? title = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var conversation = new Conversation
        {
            Title = title ?? "New Conversation"
        };
        
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();
        return conversation;
    }
    
    public async Task UpdateConversationAsync(Conversation conversation)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        conversation.UpdatedAt = DateTime.UtcNow;
        context.Conversations.Update(conversation);
        await context.SaveChangesAsync();
    }
    
    public async Task DeleteConversationAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var conversation = await context.Conversations.FindAsync(id);
        if (conversation != null)
        {
            context.Conversations.Remove(conversation);
            await context.SaveChangesAsync();
        }
    }
    
    public async Task<Message> AddMessageAsync(Guid conversationId, Message message)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        message.ConversationId = conversationId;
        
        context.Messages.Add(message);
        
        var conversation = await context.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.UpdatedAt = DateTime.UtcNow;
        }
        
        await context.SaveChangesAsync();
        return message;
    }
    
    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }
}
