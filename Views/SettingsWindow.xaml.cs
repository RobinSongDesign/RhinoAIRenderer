using AIRenderer.Models;
using AIRenderer.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace AIRenderer.Views
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private ApiProviderConfig _selectedProviderConfig;
        private string _selectedModel;
        private readonly HttpClient _httpClient;

        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = this;
            _httpClient = new HttpClient();

            // Load current settings
            var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettings();

            // Find the provider config - default to BltAI (柏拉图AI)
            var providers = ApiProviderConfig.GetAllProviders();
            _selectedProviderConfig = providers.Find(p => p.Provider == selectedProvider);
            if (_selectedProviderConfig == null)
            {
                // Default to BltAI if not found
                _selectedProviderConfig = providers.Find(p => p.Provider == ApiProvider.BltAI) ?? providers[0];
            }

            // Set model - use loaded model or default
            _selectedModel = selectedModel;

            OnPropertyChanged(nameof(SelectedProviderConfig));
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(AvailableModels));
            OnPropertyChanged(nameof(AvailableProviders));

            // Set combobox selections and load API key after window is loaded
            Loaded += (s, e) =>
            {
                // Set combobox to the selected provider
                for (int i = 0; i < ProviderComboBox.Items.Count; i++)
                {
                    if ((ProviderComboBox.Items[i] as ApiProviderConfig).Provider == _selectedProviderConfig.Provider)
                    {
                        ProviderComboBox.SelectedIndex = i;
                        break;
                    }
                }
                LoadApiKeyForProvider(_selectedProviderConfig.Provider);
            };
        }

        private void UpdateUIText()
        {
            TitleText.Text = "API Settings";
        }

        private void UpdateApiKeyText()
        {
            // Find and update API Key label - need to find the TextBlock
            // This is a simplified approach
        }

        private void UpdateTestButtonText()
        {
            TestApiKeyButton.Content = Loc.Get("Test");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<ApiProviderConfig> AvailableProviders => ApiProviderConfig.GetAllProviders();

        public List<string> AvailableModels => _selectedProviderConfig?.Models ?? new List<string>();

        public ApiProviderConfig SelectedProviderConfig
        {
            get => _selectedProviderConfig;
            set
            {
                _selectedProviderConfig = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailableModels));
            }
        }

        public string SelectedModel
        {
            get => _selectedModel;
            set
            {
                _selectedModel = value;
                OnPropertyChanged();
            }
        }

        public string ApiKey => ApiKeyBox.Password;

        public ApiProvider SelectedProvider => _selectedProviderConfig?.Provider ?? ApiProvider.BltAI;

        private void LoadApiKeyForProvider(ApiProvider provider)
        {
            string apiKey = SettingsService.GetApiKey(provider);
            ApiKeyBox.Password = apiKey ?? "";
            TestResultText.Text = "";
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is ApiProviderConfig config)
            {
                _selectedProviderConfig = config;
                _selectedModel = config.DefaultModel;
                OnPropertyChanged(nameof(SelectedProviderConfig));
                OnPropertyChanged(nameof(SelectedModel));
                ApiUrlBox.Text = config.BaseUrl;

                // Load API key for selected provider
                LoadApiKeyForProvider(config.Provider);
            }
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            TestResultText.Text = "";
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

        private async void TestApiKey_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyBox.Password;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                TestResultText.Text = "Please enter API Key";
                TestResultText.Foreground = Brushes.Orange;
                return;
            }

            TestApiKeyButton.IsEnabled = false;
            TestResultText.Text = "Testing...";
            TestResultText.Foreground = Brushes.Gray;

            try
            {
                bool isValid = await TestApiKeyAsync(_selectedProviderConfig, apiKey);
                if (isValid)
                {
                    TestResultText.Text = "API Key is valid!";
                    TestResultText.Foreground = Brushes.Green;
                }
                else
                {
                    TestResultText.Text = "API Key is invalid";
                    TestResultText.Foreground = Brushes.Red;
                }
            }
            catch
            {
                TestResultText.Text = "Connection failed";
                TestResultText.Foreground = Brushes.Red;
            }
            finally
            {
                TestApiKeyButton.IsEnabled = true;
            }
        }

        private async Task<bool> TestApiKeyAsync(ApiProviderConfig config, string apiKey)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();

                string model = config.DefaultModel;
                string url;

                if (config.Provider == ApiProvider.Gemini)
                {
                    url = $"{config.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";
                    _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
                }
                else
                {
                    url = $"{config.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }

                // Send minimal request to test API key
                var payload = new
                {
                    contents = new[] { new { parts = new[] { new { text = "Hi" } } } },
                    generationConfig = new { responseModalities = new[] { "TEXT" } }
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                RhinoApp.WriteLine($"API Test Error: {errorContent}");

                // Check for common error indicating invalid key
                if (errorContent.Contains("API_KEY_INVALID") ||
                    errorContent.Contains("PERMISSION_DENIED") ||
                    errorContent.Contains("unauthorized"))
                {
                    return false;
                }

                // Some APIs return success even with minimal request
                return response.IsSuccessStatusCode;
            }
            catch (System.Exception ex)
            {
                RhinoApp.WriteLine($"API Test Exception: {ex.Message}");
                return false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save settings - only saves current provider's API key
            SettingsService.SaveSettings(
                ApiKeyBox.Password,
                _selectedModel ?? _selectedProviderConfig?.DefaultModel,
                _selectedProviderConfig?.Provider ?? ApiProvider.BltAI);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
