using AIRenderer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace AIRenderer.Views
{
    public partial class AIRenderWindow : Window
    {
        private readonly AIRenderViewModel _viewModel;

        public AIRenderWindow()
        {
            InitializeComponent();
            _viewModel = new AIRenderViewModel();
            DataContext = _viewModel;
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                _viewModel.Settings.ApiKey = passwordBox.Password;
            }
        }
    }
}
