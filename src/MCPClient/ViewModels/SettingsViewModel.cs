using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPClient.Core.Models;
using MCPClient.Core.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MCPClient.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly ILlmService _llmService;
    
    [ObservableProperty]
    private ObservableCollection<LlmProviderConfig> _providers = new();
    
    [ObservableProperty]
    private LlmProviderConfig? _selectedProvider;
    
    [ObservableProperty]
    private string? _statusMessage;
    
    public SettingsViewModel(
        IConfigurationService configurationService,
        ILlmService llmService)
    {
        _configurationService = configurationService;
        _llmService = llmService;
    }
    
    public async Task LoadSettingsAsync()
    {
        var config = await _configurationService.LoadConfigAsync();
        Providers.Clear();
        
        foreach (var (_, provider) in config.LlmProviders)
        {
            Providers.Add(provider);
        }
    }
    
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var config = await _configurationService.LoadConfigAsync();
        
        config.LlmProviders.Clear();
        foreach (var provider in Providers)
        {
            config.LlmProviders[provider.Id] = provider;
        }
        
        await _configurationService.SaveConfigAsync(config);
        _llmService.ConfigureProviders(config);
        
        StatusMessage = "Settings saved successfully!";
    }
    
    [RelayCommand]
    private void AddProvider()
    {
        var newProvider = new LlmProviderConfig
        {
            Id = $"provider_{Providers.Count + 1}",
            DisplayName = "New Provider",
            Type = LlmProviderType.OpenAI,
            Model = "gpt-4",
            Enabled = true
        };
        Providers.Add(newProvider);
        SelectedProvider = newProvider;
    }
    
    [RelayCommand]
    private void RemoveProvider(LlmProviderConfig provider)
    {
        Providers.Remove(provider);
        if (SelectedProvider == provider)
        {
            SelectedProvider = Providers.Count > 0 ? Providers[0] : null;
        }
    }
}
