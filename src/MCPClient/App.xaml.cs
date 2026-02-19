using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using MCPClient.Core.Services;
using MCPClient.Data;
using MCPClient.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace MCPClient
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        
        public static IServiceProvider Services { get; private set; } = null!;

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
            
            InitializeDatabaseAsync();
        }
        
        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Prevent crash — log the error and mark handled
            System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
            e.Handled = true;
            
            // Show the error to the user
            try
            {
                if (_window?.Content is Microsoft.UI.Xaml.Controls.Frame frame)
                {
                    // Best effort to show error
                }
                
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = "Error",
                    Content = e.Exception.Message + "\n\n" + e.Exception.StackTrace,
                    CloseButtonText = "OK",
                    XamlRoot = _window?.Content?.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
            catch
            {
                // If we can't show the dialog, at least write to debug
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Database
            var dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MCPDesk");
            Directory.CreateDirectory(dbDir);
            var dbPath = Path.Combine(dbDir, "mcpdesk.db");
            
            // Migrate from old MCPClient database if it exists
            var oldDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MCPClient", "mcpclient.db");
            if (!File.Exists(dbPath) && File.Exists(oldDbPath))
            {
                File.Copy(oldDbPath, dbPath);
            }
            
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));
            
            // Services
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<IMcpClientService, McpClientService>();
            services.AddSingleton<ILlmService, LlmService>();
            services.AddScoped<IConversationService, ConversationService>();
            
            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<ChatViewModel>();
            services.AddTransient<SettingsViewModel>();
        }
        
        private static async void InitializeDatabaseAsync()
        {
            try
            {
                var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var context = await factory.CreateDbContextAsync();
                await context.Database.EnsureCreatedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database init failed: {ex}");
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
