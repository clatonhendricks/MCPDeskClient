using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MCPClient.Core.LlmProviders;
using MCPClient.Core.Models;
using MCPClient.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace MCPClient.Views;

public sealed partial class ChatPage : Page
{
    private readonly ChatViewModel _viewModel;
    private readonly MainViewModel _mainViewModel;
    private bool _isLoaded;
    
    public ChatPage()
    {
        this.InitializeComponent();
        
        _viewModel = App.Services.GetRequiredService<ChatViewModel>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        
        _viewModel.ScrollRequested += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                MessagesScrollViewer.UpdateLayout();
                MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
            });
        };
        
        this.Loaded += ChatPage_Loaded;
    }

    private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.LoadConversationsAsync();
        ConversationList.ItemsSource = _mainViewModel.Conversations;
        
        // Ensure providers are configured from saved config
        var configService = App.Services.GetRequiredService<Core.Services.IConfigurationService>();
        var llmService = App.Services.GetRequiredService<Core.Services.ILlmService>();
        var config = await configService.LoadConfigAsync();
        llmService.ConfigureProviders(config);
        
        _viewModel.RefreshProviders();
        ProviderComboBox.ItemsSource = _viewModel.AvailableProviders;
        _isLoaded = true;
        
        if (_viewModel.AvailableProviders.Count > 0)
        {
            ProviderComboBox.SelectedItem = _viewModel.SelectedProvider ?? _viewModel.AvailableProviders[0];
        }
        
        MessagesList.ItemsSource = _viewModel.Messages;
        
        if (_mainViewModel.Conversations.Count == 0)
        {
            await _mainViewModel.CreateNewConversationCommand.ExecuteAsync(null);
        }
        
        if (_mainViewModel.Conversations.Count > 0)
        {
            ConversationList.SelectedItem = _mainViewModel.Conversations[0];
        }
        
        // Auto-connect enabled MCP servers
        var mcpService = App.Services.GetRequiredService<Core.Services.IMcpClientService>();
        var failedServers = new System.Collections.Generic.List<string>();
        foreach (var (name, serverConfig) in config.McpServers)
        {
            if (serverConfig.Enabled && !string.IsNullOrWhiteSpace(serverConfig.Command) 
                && !mcpService.ConnectedServers.Contains(name))
            {
                try
                {
                    serverConfig.Name = name;
                    await mcpService.ConnectServerAsync(serverConfig);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-connect failed for {name}: {ex.Message}");
                    failedServers.Add($"{name}: {ex.Message}");
                }
            }
        }
        
        // Show connection status
        if (failedServers.Count > 0)
        {
            ErrorInfoBar.Message = $"Failed to connect MCP servers:\n{string.Join("\n", failedServers)}";
            ErrorInfoBar.IsOpen = true;
        }
        
        // Show connected server count in status
        var connectedCount = mcpService.ConnectedServers.Count;
        var totalEnabled = 0;
        foreach (var (_, sc) in config.McpServers) { if (sc.Enabled) totalEnabled++; }
        if (connectedCount > 0)
        {
            var tools2 = await mcpService.GetAllToolsAsync();
            McpStatusText.Text = $"🔌 {connectedCount}/{totalEnabled} MCP servers connected • {tools2.Count} tools available";
            System.Diagnostics.Debug.WriteLine($"MCP: {connectedCount}/{totalEnabled} servers connected, {tools2.Count} tools available");
        }
        else
        {
            McpStatusText.Text = "⚠ No MCP servers connected";
        }
    }
    
    private async void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (ProviderComboBox.SelectedItem is not ILlmProvider provider) return;
        
        _viewModel.SelectedProvider = provider;
        
        // Load models for this provider
        ModelComboBox.ItemsSource = null;
        ModelLoadingRing.IsActive = true;
        ModelLoadingRing.Visibility = Visibility.Visible;
        
        try
        {
            var models = await provider.GetAvailableModelsAsync();
            ModelComboBox.ItemsSource = models;
            
            // Select the current model
            var currentModel = provider.CurrentModel;
            foreach (var m in models)
            {
                if (m.Id == currentModel)
                {
                    ModelComboBox.SelectedItem = m;
                    break;
                }
            }
            if (ModelComboBox.SelectedItem == null && models.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }
        }
        catch
        {
            // Fallback: show current model as only option
            var fallback = new[] { new ModelInfo { Id = provider.CurrentModel, DisplayName = provider.CurrentModel } };
            ModelComboBox.ItemsSource = fallback;
            ModelComboBox.SelectedIndex = 0;
        }
        finally
        {
            ModelLoadingRing.IsActive = false;
            ModelLoadingRing.Visibility = Visibility.Collapsed;
        }
    }
    
    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (ModelComboBox.SelectedItem is ModelInfo model && _viewModel.SelectedProvider != null)
        {
            _viewModel.SelectedProvider.SetModel(model.Id);
        }
    }
    
    private async void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationList.SelectedItem is Conversation conversation)
        {
            _mainViewModel.SelectedConversation = conversation;
            await _viewModel.LoadConversationAsync(conversation.Id);
        }
    }
    
    private async void DeleteConversation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is Conversation conversation)
        {
            await _mainViewModel.DeleteConversationCommand.ExecuteAsync(conversation);
            if (_mainViewModel.SelectedConversation != null)
            {
                ConversationList.SelectedItem = _mainViewModel.SelectedConversation;
                await _viewModel.LoadConversationAsync(_mainViewModel.SelectedConversation.Id);
            }
            else
            {
                _viewModel.Messages.Clear();
            }
        }
    }
    
    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.CreateNewConversationCommand.ExecuteAsync(null);
        if (_mainViewModel.SelectedConversation != null)
        {
            ConversationList.SelectedItem = _mainViewModel.SelectedConversation;
        }
    }
    
    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }
    
    private async void InputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && 
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }
    
    private async System.Threading.Tasks.Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            return;
        
        _viewModel.InputText = InputTextBox.Text;
        _viewModel.SelectedProvider = ProviderComboBox.SelectedItem as Core.LlmProviders.ILlmProvider;
        
        SendButton.IsEnabled = false;
        SendButton.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        InputTextBox.Text = string.Empty;
        ErrorInfoBar.IsOpen = false;
        
        try
        {
            await _viewModel.SendMessageCommand.ExecuteAsync(null);
            
            if (!string.IsNullOrEmpty(_viewModel.ErrorMessage))
            {
                ErrorInfoBar.Message = _viewModel.ErrorMessage;
                ErrorInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = $"Error: {ex.Message}";
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            SendButton.Visibility = Visibility.Visible;
            SendButton.IsEnabled = true;
            LoadingRing.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
            
            // Scroll to bottom to show latest message
            MessagesScrollViewer.UpdateLayout();
            MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
        }
    }
}
