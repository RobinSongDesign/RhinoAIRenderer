using Newtonsoft.Json;
using Rhino;
using System;
using System.IO;

namespace AIRenderer.Services
{
    public class AppSettings
    {
        public string ApiKey { get; set; } = "";
        public string SelectedModel { get; set; } = "gemini-3-pro-image-preview";
    }

    public static class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIRenderer");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static void SaveSettings(string apiKey, string selectedModel)
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var settings = new AppSettings
                {
                    ApiKey = apiKey,
                    SelectedModel = selectedModel
                };

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);

                RhinoApp.WriteLine($"Settings saved to: {SettingsFile}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public static (string apiKey, string selectedModel) LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);

                    if (settings != null)
                    {
                        RhinoApp.WriteLine("Settings loaded.");
                        return (settings.ApiKey, settings.SelectedModel);
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error loading settings: {ex.Message}");
            }

            return ("", "gemini-3-pro-image-preview");
        }
    }
}
