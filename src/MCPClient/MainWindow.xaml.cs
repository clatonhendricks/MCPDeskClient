using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MCPClient.Views;

namespace MCPClient
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            
            // Enable Mica backdrop
            this.SystemBackdrop = new MicaBackdrop();
            
            // Set window size
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));
            
            // Navigate to Chat by default
            ContentFrame.Navigate(typeof(ChatPage));
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                switch (tag)
                {
                    case "Chat":
                        ContentFrame.Navigate(typeof(ChatPage));
                        break;
                    case "Servers":
                        ContentFrame.Navigate(typeof(ServerConfigPage));
                        break;
                }
            }
        }
    }
}
