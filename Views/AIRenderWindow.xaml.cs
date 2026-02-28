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

            // Load settings first
            var (apiKey, selectedModel) = SettingsService.LoadSettings();

            // Create ViewModel
            _viewModel = new AIRenderViewModel(apiKey, selectedModel);
            DataContext = _viewModel;

            // Set API key in password box after binding is complete
            Loaded += (s, e) =>
            {
                if (!string.IsNullOrEmpty(apiKey))
                {
                    ApiKeyBox.Password = apiKey;
                }
            };
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && _viewModel != null)
            {
                _viewModel.Settings.ApiKey = passwordBox.Password;
            }
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
    }
}
