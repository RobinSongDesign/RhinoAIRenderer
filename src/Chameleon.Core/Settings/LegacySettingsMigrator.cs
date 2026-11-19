using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Settings
{
    /// <summary>
    /// One-time import of the WPF version's settings.json (plain-text API keys included) into the
    /// current settings and secret stores.
    /// </summary>
    public static class LegacySettingsMigrator
    {
        // Names of the legacy ApiProvider enum, by numeric value.
        private static readonly string[] LegacyProviderNames = { "Gemini", "BltAI" };

        // Built-in providers of the WPF version that no longer exist (the service shut down):
        // their keys, overrides and selection are left behind instead of pointing at a dead endpoint.
        private static readonly HashSet<string> RetiredProviderIds = new HashSet<string>(StringComparer.Ordinal) { "BltAI" };

        /// <summary>Migrates legacy settings when they exist and no current settings were saved yet.</summary>
        /// <param name="legacyFilePath">Path of the WPF version's settings.json.</param>
        /// <param name="settingsStore">Destination for non-secret settings.</param>
        /// <param name="secretStore">Destination for API keys.</param>
        /// <returns>What was migrated; <see cref="LegacyMigrationResult.Migrated"/> is false when nothing was done.</returns>
        public static LegacyMigrationResult TryMigrate(string legacyFilePath, ISettingsStore settingsStore, ISecretStore secretStore)
        {
            if (settingsStore == null)
            {
                throw new ArgumentNullException(nameof(settingsStore));
            }

            if (secretStore == null)
            {
                throw new ArgumentNullException(nameof(secretStore));
            }

            if (settingsStore.Exists || string.IsNullOrEmpty(legacyFilePath) || !File.Exists(legacyFilePath))
            {
                return LegacyMigrationResult.NotMigrated;
            }

            JObject legacy;
            try
            {
                legacy = JObject.Parse(File.ReadAllText(legacyFilePath, Encoding.UTF8));
            }
            catch (JsonException)
            {
                return LegacyMigrationResult.NotMigrated;
            }
            catch (IOException)
            {
                return LegacyMigrationResult.NotMigrated;
            }

            AppSettings settings = settingsStore.Load();
            int keys = 0;

            foreach (KeyValuePair<string, string> entry in ReadStringMap(legacy["ApiKeys"]))
            {
                string id = NormalizeProviderName(entry.Key);
                if (!RetiredProviderIds.Contains(id))
                {
                    keys += StoreKey(secretStore, id, entry.Value);
                }
            }

            foreach (KeyValuePair<string, string> entry in ReadStringMap(legacy["CustomApiKeys"]))
            {
                keys += StoreKey(secretStore, entry.Key, entry.Value);
            }

            settings.UserProviders = ReadCustomProviders(legacy["CustomProviders"]);
            settings.BuiltInOverrides = ReadOverrides(legacy["BuiltInOverrides"]);
            foreach (string retired in RetiredProviderIds)
            {
                settings.BuiltInOverrides.Remove(retired);
            }
            settings.Language = ReadInt(legacy["LanguageIndex"]) == 1 ? Languages.English : Languages.Chinese;

            string providerId = (string?)legacy["SelectedProviderId"] ?? string.Empty;
            if (providerId.Length == 0)
            {
                providerId = NormalizeProviderName(TokenToString(legacy["SelectedProvider"]) ?? "BltAI");
            }

            // A retired provider keeps the default selection of a fresh install.
            if (!RetiredProviderIds.Contains(providerId))
            {
                settings.ActiveBackendId = BackendIds.Direct(providerId);
                string? model = (string?)legacy["SelectedModel"];
                if (!string.IsNullOrEmpty(model))
                {
                    settings.ActiveModelByBackend[settings.ActiveBackendId] = model!;
                }
            }

            settingsStore.Save(settings);
            RenameLegacyFile(legacyFilePath);
            return new LegacyMigrationResult(true, keys);
        }

        private static int StoreKey(ISecretStore secretStore, string providerId, string key)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            secretStore.Set(SecretNames.ForProvider(providerId), key.Trim());
            return 1;
        }

        private static string NormalizeProviderName(string nameOrNumber)
        {
            // Newtonsoft wrote enum dictionary keys by name, but enum values as numbers.
            return int.TryParse(nameOrNumber, out int index) && index >= 0 && index < LegacyProviderNames.Length
                ? LegacyProviderNames[index]
                : nameOrNumber;
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadStringMap(JToken? token)
        {
            if (token is JObject map)
            {
                foreach (JProperty property in map.Properties())
                {
                    string? value = TokenToString(property.Value);
                    if (value != null)
                    {
                        yield return new KeyValuePair<string, string>(property.Name, value);
                    }
                }
            }
        }

        private static List<UserProviderConfig> ReadCustomProviders(JToken? token)
        {
            var result = new List<UserProviderConfig>();
            if (token is JArray array)
            {
                foreach (JObject item in array.OfType<JObject>())
                {
                    string id = (string?)item["Id"] ?? string.Empty;
                    if (id.Length == 0)
                    {
                        continue;
                    }

                    result.Add(new UserProviderConfig
                    {
                        Id = id,
                        DisplayName = (string?)item["DisplayName"] ?? id,
                        BaseUrl = (string?)item["BaseUrl"] ?? string.Empty,
                        ApiFormat = (string?)item["ApiFormat"] ?? ApiFormats.Gemini,
                        AuthType = (string?)item["AuthType"] ?? AuthTypes.Bearer,
                        Models = ReadStringList(item["Models"]),
                        DefaultModel = (string?)item["DefaultModel"] ?? string.Empty
                    });
                }
            }

            return result;
        }

        private static Dictionary<string, ProviderOverride> ReadOverrides(JToken? token)
        {
            var result = new Dictionary<string, ProviderOverride>(StringComparer.Ordinal);
            if (token is JObject map)
            {
                foreach (JProperty property in map.Properties())
                {
                    if (property.Value is JObject item)
                    {
                        List<string> models = ReadStringList(item["Models"]);
                        result[NormalizeProviderName(property.Name)] = new ProviderOverride
                        {
                            DisplayName = (string?)item["DisplayName"],
                            BaseUrl = (string?)item["BaseUrl"],
                            ApiFormat = (string?)item["ApiFormat"],
                            AuthType = (string?)item["AuthType"],
                            Models = models.Count > 0 ? models : null,
                            DefaultModel = (string?)item["DefaultModel"]
                        };
                    }
                }
            }

            return result;
        }

        private static List<string> ReadStringList(JToken? token)
        {
            return token is JArray array
                ? array.Select(TokenToString).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList()
                : new List<string>();
        }

        private static string? TokenToString(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Type == JTokenType.String ? (string?)token : token.ToString(Formatting.None);
        }

        private static int ReadInt(JToken? token)
        {
            return token != null && int.TryParse(TokenToString(token), out int value) ? value : 0;
        }

        private static void RenameLegacyFile(string legacyFilePath)
        {
            try
            {
                string target = Path.Combine(Path.GetDirectoryName(legacyFilePath) ?? ".", "settings.migrated.json");
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(legacyFilePath, target);
            }
            catch (IOException)
            {
                // Leaving the old file in place is harmless: migration never runs once new settings exist.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
