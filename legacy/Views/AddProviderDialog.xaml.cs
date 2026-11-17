using AIRenderer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AIRenderer.Views
{
    public partial class AddProviderDialog : Window
    {
        public CustomProviderConfig Result { get; private set; }

        private readonly string _editingId;

        public AddProviderDialog() : this(null) { }

        public AddProviderDialog(CustomProviderConfig existing)
        {
            InitializeComponent();
            ApiFormatCombo.SelectedIndex = 0;
            AuthTypeCombo.SelectedIndex = 0;

            if (existing != null)
            {
                _editingId = existing.Id;
                Title = "Edit Provider";

                NameBox.Text = existing.DisplayName;
                UrlBox.Text = existing.BaseUrl;
                ModelsBox.Text = string.Join(", ", existing.Models ?? new List<string>());

                SelectComboByTag(ApiFormatCombo, existing.ApiFormat ?? "gemini");
                SelectComboByTag(AuthTypeCombo, existing.AuthType ?? "bearer");

                // 同步 AuthType 可用状态
                AuthTypeCombo.IsEnabled = existing.ApiFormat != "openai";
            }
        }

        private void SelectComboByTag(ComboBox combo, string tag)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if ((combo.Items[i] as ComboBoxItem)?.Tag?.ToString() == tag)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void ApiFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiFormatCombo.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "openai")
            {
                if (string.IsNullOrWhiteSpace(UrlBox.Text))
                    UrlBox.Text = "https://api.openai.com";
                if (string.IsNullOrWhiteSpace(ModelsBox.Text))
                    ModelsBox.Text = "gpt-image-2";
                AuthTypeCombo.SelectedIndex = 0;
                AuthTypeCombo.IsEnabled = false;
            }
            else
            {
                AuthTypeCombo.IsEnabled = true;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            string url = UrlBox.Text.Trim();
            string modelsText = ModelsBox.Text.Trim();

            if (string.IsNullOrEmpty(name))   { ErrorText.Text = "Provider name is required."; return; }
            if (string.IsNullOrEmpty(url))     { ErrorText.Text = "Base URL is required."; return; }
            if (string.IsNullOrEmpty(modelsText)) { ErrorText.Text = "At least one model name is required."; return; }

            var models = modelsText.Split(',')
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();

            string authType  = (AuthTypeCombo.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "bearer";
            string apiFormat = (ApiFormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "gemini";

            Result = new CustomProviderConfig
            {
                Id           = _editingId ?? Guid.NewGuid().ToString("N").Substring(0, 12),
                DisplayName  = name,
                BaseUrl      = url.TrimEnd('/'),
                AuthType     = authType,
                ApiFormat    = apiFormat,
                Models       = models,
                DefaultModel = models[0]
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
