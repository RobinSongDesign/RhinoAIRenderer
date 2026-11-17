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
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace AIRenderer.Views
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private List<ProviderItem> _allProviders;
        private ProviderItem _selectedProvider;
        private string _selectedModel;
        private readonly HttpClient _httpClient;

        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = this;
            _httpClient = new HttpClient();

            var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettingsWithProvider();
            _allProviders = SettingsService.GetAllProviders();

            _selectedProvider = _allProviders.Find(p => p.Id == selectedProvider.Id) ?? _allProviders[0];
            _selectedModel = selectedModel;

            OnPropertyChanged(nameof(AvailableProviders));
            OnPropertyChanged(nameof(SelectedProviderConfig));

            Loaded += (s, e) =>
            {
                for (int i = 0; i < ProviderComboBox.Items.Count; i++)
                {
                    if ((ProviderComboBox.Items[i] as ProviderItem)?.Id == _selectedProvider?.Id)
                    {
                        ProviderComboBox.SelectedIndex = i;
                        break;
                    }
                }
                LoadApiKeyForProvider(_selectedProvider?.Id ?? "");
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<ProviderItem> AvailableProviders => _allProviders;

        public ProviderItem SelectedProviderConfig
        {
            get => _selectedProvider;
            set
            {
                if (value == null) return; // ComboBox 刷新 ItemsSource 时会短暂写入 null，忽略
                _selectedProvider = value;
                OnPropertyChanged();
            }
        }

        public string SelectedModel
        {
            get => _selectedModel;
            set { _selectedModel = value; OnPropertyChanged(); }
        }

        public string ApiKey => ApiKeyBox.Password;

        public string[] LanguageOptions => Loc.LanguageOptions;

        private int _selectedLanguageIndex = SettingsService.LoadLanguageIndex();
        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set { _selectedLanguageIndex = value; OnPropertyChanged(); }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedIndex < 0) return;
            int idx = LanguageComboBox.SelectedIndex;
            Loc.CurrentLanguage = Loc.GetLanguageFromIndex(idx);
            SettingsService.SaveLanguage(idx);
            SelectedLanguageIndex = idx;
        }

        private void LoadApiKeyForProvider(string providerId)
        {
            ApiKeyBox.Password = SettingsService.GetApiKey(providerId) ?? "";
            TestResultText.Text = "";
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is ProviderItem provider)
            {
                _selectedProvider = provider;
                _selectedModel = provider.DefaultModel;
                ApiUrlBox.Text = provider.BaseUrl;
                LoadApiKeyForProvider(provider.Id);

                OnPropertyChanged(nameof(SelectedProviderConfig));
                OnPropertyChanged(nameof(SelectedModel));

                // 同步测试模型下拉默认选第一项
                if (TestModelComboBox.Items.Count > 0)
                    TestModelComboBox.SelectedIndex = 0;
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
                bool isValid = await TestApiKeyAsync(_selectedProvider, apiKey);
                TestResultText.Text = isValid ? "API Key is valid!" : "API Key is invalid";
                TestResultText.Foreground = isValid ? Brushes.Green : Brushes.Red;
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

        private async Task<bool> TestApiKeyAsync(ProviderItem provider, string apiKey)
        {
            try
            {
                // 优先使用用户在 TestModelComboBox 中选择的模型
                string selectedTestModel = TestModelComboBox.SelectedItem?.ToString();

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                System.Net.Http.HttpResponseMessage response;

                if (provider.ApiFormat == "openai")
                {
                    // GET /v1/models — 轻量验证，无需发图
                    string url = $"{provider.BaseUrl.TrimEnd('/')}/v1/models";
                    response = await _httpClient.GetAsync(url);
                }
                else
                {
                    // Gemini 格式：POST generateContent
                    _httpClient.DefaultRequestHeaders.Clear();
                    string model = selectedTestModel
                        ?? provider.DefaultModel
                        ?? (provider.Models.Count > 0 ? provider.Models[0] : "gemini-3.1-flash-image-preview");
                    string url = $"{provider.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";

                    if (provider.AuthType == "goog")
                        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
                    else
                        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    var payload = new
                    {
                        contents = new[] { new { parts = new[] { new { text = "Hi" } } } },
                        generationConfig = new { responseModalities = new[] { "TEXT" } }
                    };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(url, content);
                }

                if (response.IsSuccessStatusCode) return true;

                var errorContent = await response.Content.ReadAsStringAsync();
                RhinoApp.WriteLine($"API Test Error: {errorContent}");

                if (errorContent.Contains("API_KEY_INVALID") ||
                    errorContent.Contains("PERMISSION_DENIED") ||
                    errorContent.Contains("unauthorized") ||
                    errorContent.Contains("Unauthorized") ||
                    (int)response.StatusCode == 401 ||
                    (int)response.StatusCode == 403)
                    return false;

                return false;
            }
            catch (System.Exception ex)
            {
                RhinoApp.WriteLine($"API Test Exception: {ex.Message}");
                return false;
            }
        }

        private void EditProviderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProvider == null) return;

            var existing = new AIRenderer.Models.CustomProviderConfig
            {
                Id = _selectedProvider.Id,
                DisplayName = _selectedProvider.DisplayName,
                BaseUrl = _selectedProvider.BaseUrl,
                AuthType = _selectedProvider.AuthType,
                ApiFormat = _selectedProvider.ApiFormat,
                Models = new System.Collections.Generic.List<string>(_selectedProvider.Models),
                DefaultModel = _selectedProvider.DefaultModel
            };

            var dialog = new AddProviderDialog(existing) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                if (_selectedProvider.IsCustom)
                    SettingsService.SaveCustomProvider(dialog.Result);
                else
                    SettingsService.SaveBuiltInOverride(_selectedProvider.Id, dialog.Result);

                _allProviders = SettingsService.GetAllProviders();
                OnPropertyChanged(nameof(AvailableProviders));

                var updated = _allProviders.Find(p => p.Id == _selectedProvider.Id);
                if (updated != null)
                    ProviderComboBox.SelectedItem = updated;
            }
        }

        private void AddProviderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddProviderDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                SettingsService.SaveCustomProvider(dialog.Result);
                _allProviders = SettingsService.GetAllProviders();
                OnPropertyChanged(nameof(AvailableProviders));

                var newItem = _allProviders.Find(p => p.Id == dialog.Result.Id);
                if (newItem != null)
                    ProviderComboBox.SelectedItem = newItem;
            }
        }

        private void DeleteProviderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProvider?.IsCustom != true) return;

            var result = MessageBox.Show(
                $"Delete provider \"{_selectedProvider.DisplayName}\"?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                SettingsService.DeleteCustomProvider(_selectedProvider.Id);
                _allProviders = SettingsService.GetAllProviders();
                _selectedProvider = _allProviders.Count > 0 ? _allProviders[0] : null;
                OnPropertyChanged(nameof(AvailableProviders));
                OnPropertyChanged(nameof(SelectedProviderConfig));
                if (_selectedProvider != null)
                    ProviderComboBox.SelectedItem = _selectedProvider;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.SaveSettings(
                ApiKeyBox.Password,
                _selectedModel ?? _selectedProvider?.DefaultModel,
                _selectedProvider);

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
