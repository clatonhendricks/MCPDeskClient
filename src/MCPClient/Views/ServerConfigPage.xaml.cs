using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MCPClient.Core.Models;
using MCPClient.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MCPClient.Views;

public sealed partial class ServerConfigPage : Page
{
    private readonly IConfigurationService _configService;
    private readonly IMcpClientService _mcpService;
    private readonly ObservableCollection<McpServerConfig> _servers = new();
    private McpServerConfig? _editingServer;
    
    public ServerConfigPage()
    {
        this.InitializeComponent();
        
        _configService = App.Services.GetRequiredService<IConfigurationService>();
        _mcpService = App.Services.GetRequiredService<IMcpClientService>();
        
        this.Loaded += ServerConfigPage_Loaded;
    }

    private async void ServerConfigPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            _servers.Clear();
            
            foreach (var (name, serverConfig) in config.McpServers)
            {
                serverConfig.Name = name;
                _servers.Add(serverConfig);
            }
            
            ServersListView.ItemsSource = _servers;
            RefreshConnectedServers();
        }
        catch (Exception ex)
        {
            StatusInfoBar.Message = $"Failed to load config: {ex.Message}";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
    }
    
    private void RefreshConnectedServers()
    {
        ConnectedServersListView.ItemsSource = _mcpService.ConnectedServers;
    }
    
    private void AddServer_Click(object sender, RoutedEventArgs e)
    {
        _editingServer = new McpServerConfig
        {
            Name = $"server-{_servers.Count + 1}",
            Command = "npx",
            Enabled = true
        };
        
        ShowServerForm(_editingServer);
    }
    
    private void ShowServerForm(McpServerConfig server)
    {
        ServerFormPanel.Visibility = Visibility.Visible;
        ServerNameTextBox.Text = server.Name;
        CommandTextBox.Text = server.Command;
        ArgsTextBox.Text = string.Join("\n", server.Args);
        EnvTextBox.Text = string.Join("\n", server.Env.Select(kv => $"{kv.Key}={kv.Value}"));
    }
    
    private void SaveServer_Click(object sender, RoutedEventArgs e)
    {
        if (_editingServer == null) return;
        
        _editingServer.Name = ServerNameTextBox.Text;
        _editingServer.Command = CommandTextBox.Text;
        _editingServer.Args = ArgsTextBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
        _editingServer.Env = EnvTextBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
        
        if (!_servers.Contains(_editingServer))
        {
            _servers.Add(_editingServer);
        }
        
        ServerFormPanel.Visibility = Visibility.Collapsed;
        _editingServer = null;
    }
    
    private void CancelServer_Click(object sender, RoutedEventArgs e)
    {
        ServerFormPanel.Visibility = Visibility.Collapsed;
        _editingServer = null;
    }
    
    private void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is McpServerConfig server)
        {
            _servers.Remove(server);
        }
    }
    
    private async void ConnectServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is McpServerConfig server)
        {
            try
            {
                StatusInfoBar.Message = $"Connecting to {server.Name}...";
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.IsOpen = true;
                
                await _mcpService.ConnectServerAsync(server);
                
                StatusInfoBar.Message = $"Connected to {server.Name}!";
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                RefreshConnectedServers();
            }
            catch (Exception ex)
            {
                StatusInfoBar.Message = $"Failed to connect: {ex.Message}";
                StatusInfoBar.Severity = InfoBarSeverity.Error;
            }
        }
    }
    
    private async void ConnectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var server in _servers.Where(s => s.Enabled))
        {
            try
            {
                await _mcpService.ConnectServerAsync(server);
            }
            catch
            {
                // Continue with other servers
            }
        }
        
        RefreshConnectedServers();
        StatusInfoBar.Message = $"Connected to {_mcpService.ConnectedServers.Count} servers";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
    
    private async void DisconnectAll_Click(object sender, RoutedEventArgs e)
    {
        await _mcpService.DisconnectAllAsync();
        RefreshConnectedServers();
        
        StatusInfoBar.Message = "All servers disconnected";
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;
    }
    
    private async void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var config = await _configService.LoadConfigAsync();
        
        config.McpServers.Clear();
        foreach (var server in _servers)
        {
            config.McpServers[server.Name] = server;
        }
        
        await _configService.SaveConfigAsync(config);
        
        StatusInfoBar.Message = "Configuration saved!";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }
}
