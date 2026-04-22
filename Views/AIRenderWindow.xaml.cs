using AIRenderer.Models;
using AIRenderer.Services;
using AIRenderer.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AIRenderer.Views
{
    public partial class AIRenderWindow : Window
    {
        private AIRenderViewModel _viewModel;

        public AIRenderWindow()
        {
            InitializeComponent();

            var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettingsWithProvider();
            _viewModel = new AIRenderViewModel(apiKey, selectedModel, selectedProvider);
            DataContext = _viewModel;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };

            if (settingsWindow.ShowDialog() == true)
            {
                var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettingsWithProvider();
                _viewModel.Settings.ApiKey = apiKey;
                _viewModel.Settings.SelectedModel = selectedModel;
                _viewModel.Settings.SelectedProviderItem = selectedProvider;
            }
        }
    }
}
