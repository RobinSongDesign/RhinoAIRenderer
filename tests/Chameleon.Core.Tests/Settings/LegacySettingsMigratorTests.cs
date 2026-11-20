using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.Core.Tests.Settings
{
    public class LegacySettingsMigratorTests : IDisposable
    {
        private readonly TempDirectory _dir = new TempDirectory();
        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();

        private string LegacyPath => _dir.File("legacy-settings.json");

        private JsonSettingsStore Store()
        {
            return new JsonSettingsStore(_dir.File("settings.json"), () => AppSettingsDefaults.Create(CatalogLoader.LoadBuiltIn()));
        }

        private LegacyMigrationResult Migrate(string legacyJson)
        {
            File.WriteAllText(LegacyPath, legacyJson);
            return LegacySettingsMigrator.TryMigrate(LegacyPath, Store(), _secrets);
        }

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void BuiltInKeys_MoveWithTheSelection_ExceptForTheRetiredBltAi()
        {
            // Shape written by the WPF version: enum values as numbers, enum dictionary keys as names.
            LegacyMigrationResult result = Migrate(@"{
              ""ApiKeys"": { ""BltAI"": ""test-blt-key"", ""Gemini"": "" test-gemini-key "" },
              ""CustomApiKeys"": {},
              ""CustomProviders"": [],
              ""BuiltInOverrides"": {},
              ""SelectedModel"": ""gemini-3-pro-image"",
              ""SelectedProvider"": 0,
              ""SelectedProviderId"": null,
              ""LanguageIndex"": 1
            }");

            AppSettings settings = Store().Load();
            Assert.True(result.Migrated);
            Assert.Equal(1, result.KeysMigrated);
            // BLT AI shut down: its key would only point at a dead endpoint.
            Assert.False(_secrets.Contains("provider:BltAI"));
            Assert.Equal("test-gemini-key", _secrets.Get("provider:Gemini"));
            Assert.Equal("direct:Gemini", settings.ActiveBackendId);
            Assert.Equal("gemini-3-pro-image", settings.ActiveModelByBackend["direct:Gemini"]);
            Assert.Equal("en", settings.Language);
        }

        [Fact]
        public void NumericEnumKeys_AreMappedToProviderNames_AndASelectedBltAiFallsBackToTheDefault()
        {
            Migrate(@"{ ""ApiKeys"": { ""1"": ""test-blt-key"", ""0"": ""test-gemini-key"" }, ""SelectedProvider"": ""BltAI"", ""SelectedModel"": ""x"", ""LanguageIndex"": 0 }");

            Assert.False(_secrets.Contains("provider:BltAI"));
            Assert.Equal("test-gemini-key", _secrets.Get("provider:Gemini"));
            AppSettings settings = Store().Load();
            Assert.Equal("direct:Chameleon", settings.ActiveBackendId);
            Assert.False(settings.ActiveModelByBackend.ContainsKey("direct:BltAI"));
            Assert.Equal("zh", settings.Language);
        }

        [Fact]
        public void CustomProviderSelected_MigratesProviderKeyAndSelection()
        {
            LegacyMigrationResult result = Migrate(@"{
              ""ApiKeys"": {},
              ""CustomApiKeys"": { ""abc123def456"": ""test-relay-key"" },
              ""CustomProviders"": [ {
                 ""Id"": ""abc123def456"", ""DisplayName"": ""My Relay"", ""BaseUrl"": ""https://relay.example.com"",
                 ""AuthType"": ""bearer"", ""ApiFormat"": ""openai"", ""Models"": [""gpt-image-2"", ""other""], ""DefaultModel"": ""gpt-image-2"" } ],
              ""SelectedModel"": ""gpt-image-2"",
              ""SelectedProvider"": 1,
              ""SelectedProviderId"": ""abc123def456"",
              ""LanguageIndex"": 0
            }");

            AppSettings settings = Store().Load();
            UserProviderConfig provider = Assert.Single(settings.UserProviders);
            Assert.Equal(1, result.KeysMigrated);
            Assert.Equal("abc123def456", provider.Id);
            Assert.Equal("My Relay", provider.DisplayName);
            Assert.Equal("openai", provider.ApiFormat);
            Assert.Equal(new[] { "gpt-image-2", "other" }, provider.Models.ToArray());
            Assert.Equal("test-relay-key", _secrets.Get("provider:abc123def456"));
            Assert.Equal("direct:abc123def456", settings.ActiveBackendId);
            Assert.Equal("gpt-image-2", settings.ActiveModelByBackend["direct:abc123def456"]);
        }

        [Fact]
        public void UserCopiesOfTheRetiredService_AreLeftBehind_WithTheirKeysAndSelection()
        {
            LegacyMigrationResult result = Migrate(@"{
              ""CustomApiKeys"": { ""blt123"": ""test-blt-key"", ""ok456"": ""test-relay-key"" },
              ""CustomProviders"": [
                { ""Id"": ""blt123"", ""DisplayName"": ""柏拉图Gemini"", ""BaseUrl"": ""https://hk-api.gptbest.vip"", ""AuthType"": ""bearer"", ""ApiFormat"": ""gemini"", ""Models"": [""m""], ""DefaultModel"": ""m"" },
                { ""Id"": ""ok456"", ""DisplayName"": ""My Relay"", ""BaseUrl"": ""https://relay.example.com"", ""AuthType"": ""bearer"", ""ApiFormat"": ""gemini"", ""Models"": [""m""], ""DefaultModel"": ""m"" } ],
              ""SelectedProvider"": 1,
              ""SelectedProviderId"": ""blt123"",
              ""SelectedModel"": ""m""
            }");

            AppSettings settings = Store().Load();
            Assert.Equal(1, result.KeysMigrated);
            Assert.Equal("ok456", Assert.Single(settings.UserProviders).Id);
            Assert.False(_secrets.Contains("provider:blt123"));
            Assert.Equal("direct:Chameleon", settings.ActiveBackendId);
        }

        [Fact]
        public void BuiltInOverrides_AreKept_ExceptForTheRetiredBltAi()
        {
            Migrate(@"{
              ""BuiltInOverrides"": {
                ""Gemini"": {
                  ""Id"": ""Gemini"", ""DisplayName"": ""Google (镜像)"", ""BaseUrl"": ""https://mirror.example.com"",
                  ""AuthType"": ""goog"", ""ApiFormat"": ""gemini"", ""Models"": [""gemini-3-pro-image""], ""DefaultModel"": ""gemini-3-pro-image"" },
                ""BltAI"": {
                  ""Id"": ""BltAI"", ""DisplayName"": ""柏拉图 (镜像)"", ""BaseUrl"": ""https://blt-mirror.example.com"",
                  ""AuthType"": ""bearer"", ""ApiFormat"": ""gemini"", ""Models"": [""m""], ""DefaultModel"": ""m"" } },
              ""SelectedProvider"": 0
            }");

            Dictionary<string, ProviderOverride> overrides = Store().Load().BuiltInOverrides;
            Assert.False(overrides.ContainsKey("BltAI"));
            ProviderOverride ov = overrides["Gemini"];
            Assert.Equal("Google (镜像)", ov.DisplayName);
            Assert.Equal("https://mirror.example.com", ov.BaseUrl);
            Assert.Equal(new[] { "gemini-3-pro-image" }, ov.Models!.ToArray());

            // And the migrated override produces a working provider list.
            var registry = new ProviderRegistry(CatalogLoader.LoadBuiltIn());
            ProviderProfile gemini = registry.Build(null, overrides, "zh").Single(p => p.Id == "Gemini");
            Assert.Equal("https://mirror.example.com", gemini.BaseUrl);
            Assert.Equal("gemini-3-pro-image", gemini.DefaultModel);
        }

        [Fact]
        public void MinimalLegacyFile_FallsBackToTheCurrentDefaults()
        {
            Migrate("{}");

            AppSettings settings = Store().Load();
            Assert.Equal("direct:Chameleon", settings.ActiveBackendId);
            Assert.Equal("zh", settings.Language);
            Assert.Empty(settings.UserProviders);
        }

        [Fact]
        public void EmptyKeys_AreNotStored()
        {
            LegacyMigrationResult result = Migrate(@"{ ""ApiKeys"": { ""BltAI"": """", ""Gemini"": ""   "" } }");

            Assert.Equal(0, result.KeysMigrated);
            Assert.False(_secrets.Contains("provider:BltAI"));
        }

        [Fact]
        public void LegacyFile_IsRenamedNotDeleted_AndKeysNeverReachTheNewSettingsFile()
        {
            Migrate(@"{ ""ApiKeys"": { ""Gemini"": ""test-gemini-key"" } }");

            Assert.False(File.Exists(LegacyPath));
            Assert.True(File.Exists(_dir.File("settings.migrated.json")));
            Assert.DoesNotContain("test-gemini-key", File.ReadAllText(_dir.File("settings.json")));
        }

        [Fact]
        public void DoesNothing_WhenNewSettingsAlreadyExist()
        {
            JsonSettingsStore store = Store();
            AppSettings existing = store.Load();
            existing.Language = "en";
            store.Save(existing);

            LegacyMigrationResult result = Migrate(@"{ ""ApiKeys"": { ""Gemini"": ""test-gemini-key"" }, ""LanguageIndex"": 0 }");

            Assert.False(result.Migrated);
            Assert.False(_secrets.Contains("provider:Gemini"));
            Assert.True(File.Exists(LegacyPath));
            Assert.Equal("en", Store().Load().Language);
        }

        [Fact]
        public void DoesNothing_WhenLegacyFileIsMissingOrCorrupt()
        {
            Assert.False(LegacySettingsMigrator.TryMigrate(_dir.File("missing.json"), Store(), _secrets).Migrated);
            Assert.False(Migrate("{ broken").Migrated);
            Assert.False(Store().Exists);
        }

        [Fact]
        public void RunningTwice_OnlyMigratesOnce()
        {
            Migrate(@"{ ""ApiKeys"": { ""Gemini"": ""test-gemini-key"" } }");
            _secrets.Set("provider:Gemini", "changed-later");

            LegacyMigrationResult second = LegacySettingsMigrator.TryMigrate(LegacyPath, Store(), _secrets);

            Assert.False(second.Migrated);
            Assert.Equal("changed-later", _secrets.Get("provider:Gemini"));
        }
    }
}
