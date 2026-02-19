using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPClient.Core.Models;
using MCPClient.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MCPClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConversationService _conversationService;
    private readonly IConfigurationService _configurationService;
    
    [ObservableProperty]
    private ObservableCollection<Conversation> _conversations = new();
    
    [ObservableProperty]
    private Conversation? _selectedConversation;
    
    public MainViewModel(
        IConversationService conversationService,
        IConfigurationService configurationService)
    {
        _conversationService = conversationService;
        _configurationService = configurationService;
    }
    
    public async Task LoadConversationsAsync()
    {
        var conversations = await _conversationService.GetAllConversationsAsync();
        Conversations.Clear();
        foreach (var conv in conversations)
        {
            Conversations.Add(conv);
        }
    }
    
    [RelayCommand]
    private async Task CreateNewConversationAsync()
    {
        var conversation = await _conversationService.CreateConversationAsync();
        Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }
    
    [RelayCommand]
    private async Task DeleteConversationAsync(Conversation conversation)
    {
        await _conversationService.DeleteConversationAsync(conversation.Id);
        Conversations.Remove(conversation);
        if (SelectedConversation == conversation)
        {
            SelectedConversation = Conversations.Count > 0 ? Conversations[0] : null;
        }
    }
}
