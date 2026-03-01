using AIRenderer.Models;
using Newtonsoft.Json;
using Rhino;
using System;
using System.Collections.Generic;
using System.IO;

namespace AIRenderer.Services
{
    public class AppSettings
    {
        public Dictionary<ApiProvider, string> ApiKeys { get; set; } = new Dictionary<ApiProvider, string>();
        public string SelectedModel { get; set; } = "gemini-3.1-flash-image-preview";
        public ApiProvider SelectedProvider { get; set; } = ApiProvider.Gemini;
        public int LanguageIndex { get; set; } = 0; // 0 = Chinese, 1 = English
    }

    public static class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIRenderer");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static void SaveSettings(string apiKey, string selectedModel, ApiProvider selectedProvider)
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                // Load existing settings to preserve API keys for other providers
                var existingSettings = LoadSettingsInternal();

                // Update only the current provider's API key and model
                if (existingSettings.ApiKeys == null)
                {
                    existingSettings.ApiKeys = new Dictionary<ApiProvider, string>();
                }
                existingSettings.ApiKeys[selectedProvider] = apiKey;
                existingSettings.SelectedModel = selectedModel;
                existingSettings.SelectedProvider = selectedProvider;

                string json = JsonConvert.SerializeObject(existingSettings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);

                RhinoApp.WriteLine($"Settings saved to: {SettingsFile}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public static string GetApiKey(ApiProvider provider)
        {
            var settings = LoadSettingsInternal();
            if (settings.ApiKeys != null && settings.ApiKeys.ContainsKey(provider))
            {
                return settings.ApiKeys[provider];
            }
            return "";
        }

        public static (string apiKey, string selectedModel, ApiProvider selectedProvider) LoadSettings()
        {
            var settings = LoadSettingsInternal();
            string apiKey = "";
            if (settings.ApiKeys != null && settings.ApiKeys.ContainsKey(settings.SelectedProvider))
            {
                apiKey = settings.ApiKeys[settings.SelectedProvider];
            }
            // Load language setting
            Loc.CurrentLanguage = Loc.GetLanguageFromIndex(settings.LanguageIndex);
            return (apiKey, settings.SelectedModel, settings.SelectedProvider);
        }

        public static int LoadLanguageIndex()
        {
            var settings = LoadSettingsInternal();
            return settings.LanguageIndex;
        }

        public static void SaveLanguage(int languageIndex)
        {
            try
            {
                var settings = LoadSettingsInternal();
                settings.LanguageIndex = languageIndex;
                Loc.CurrentLanguage = Loc.GetLanguageFromIndex(languageIndex);
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error saving language: {ex.Message}");
            }
        }

        private static AppSettings LoadSettingsInternal()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error loading settings: {ex.Message}");
            }
            return new AppSettings();
        }
    }
}
