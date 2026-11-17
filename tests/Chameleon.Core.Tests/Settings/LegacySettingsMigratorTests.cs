using System;
using System.IO;
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
        public void BuiltInKeysOnly_MovesKeysAndSelection()
        {
            // Shape written by the WPF version: enum values as numbers, enum dictionary keys as names.
            LegacyMigrationResult result = Migrate(@"{
              ""ApiKeys"": { ""BltAI"": ""test-blt-key"", ""Gemini"": "" test-gemini-key "" },
              ""CustomApiKeys"": {},
              ""CustomProviders"": [],
              ""BuiltInOverrides"": {},
              ""SelectedModel"": ""gemini-3-pro-image-preview"",
              ""SelectedProvider"": 0,
              ""SelectedProviderId"": null,
              ""LanguageIndex"": 1
            }");

            AppSettings settings = Store().Load();
            Assert.True(result.Migrated);
            Assert.Equal(2, result.KeysMigrated);
            Assert.Equal("test-blt-key", _secrets.Get("provider:BltAI"));
            Assert.Equal("test-gemini-key", _secrets.Get("provider:Gemini"));
            Assert.Equal("direct:Gemini", settings.ActiveBackendId);
            Assert.Equal("gemini-3-pro-image-preview", settings.ActiveModelByBackend["direct:Gemini"]);
            Assert.Equal("en", settings.Language);
        }

        [Fact]
        public void NumericEnumKeys_AreMappedToProviderNames()
        {
            Migrate(@"{ ""ApiKeys"": { ""1"": ""test-blt-key"", ""0"": ""test-gemini-key"" }, ""SelectedProvider"": ""BltAI"", ""LanguageIndex"": 0 }");

            Assert.Equal("test-blt-key", _secrets.Get("provider:BltAI"));
            Assert.Equal("test-gemini-key", _secrets.Get("provider:Gemini"));
            Assert.Equal("direct:BltAI", Store().Load().ActiveBackendId);
            Assert.Equal("zh", Store().Load().Language);
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
        public void BuiltInOverrides_AreKept()
        {
            Migrate(@"{
              ""BuiltInOverrides"": { ""BltAI"": {
                 ""Id"": ""BltAI"", ""DisplayName"": ""柏拉图 (镜像)"", ""BaseUrl"": ""https://mirror.example.com"",
                 ""AuthType"": ""bearer"", ""ApiFormat"": ""gemini"", ""Models"": [""gemini-3-pro-image-preview""], ""DefaultModel"": ""gemini-3-pro-image-preview"" } },
              ""SelectedProvider"": 1
            }");

            ProviderOverride ov = Store().Load().BuiltInOverrides["BltAI"];
            Assert.Equal("柏拉图 (镜像)", ov.DisplayName);
            Assert.Equal("https://mirror.example.com", ov.BaseUrl);
            Assert.Equal(new[] { "gemini-3-pro-image-preview" }, ov.Models!.ToArray());

            // And the migrated override produces a working provider list.
            var registry = new ProviderRegistry(CatalogLoader.LoadBuiltIn());
            ProviderProfile blt = registry.Build(null, Store().Load().BuiltInOverrides, "zh")[1];
            Assert.Equal("https://mirror.example.com", blt.BaseUrl);
            Assert.Equal("gemini-3-pro-image-preview", blt.DefaultModel);
        }

        [Fact]
        public void MinimalLegacyFile_FallsBackToTheLegacyDefaults()
        {
            Migrate("{}");

            AppSettings settings = Store().Load();
            Assert.Equal("direct:BltAI", settings.ActiveBackendId);
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
            Migrate(@"{ ""ApiKeys"": { ""BltAI"": ""test-blt-key"" } }");

            Assert.False(File.Exists(LegacyPath));
            Assert.True(File.Exists(_dir.File("settings.migrated.json")));
            Assert.DoesNotContain("test-blt-key", File.ReadAllText(_dir.File("settings.json")));
        }

        [Fact]
        public void DoesNothing_WhenNewSettingsAlreadyExist()
        {
            JsonSettingsStore store = Store();
            AppSettings existing = store.Load();
            existing.Language = "en";
            store.Save(existing);

            LegacyMigrationResult result = Migrate(@"{ ""ApiKeys"": { ""BltAI"": ""test-blt-key"" }, ""LanguageIndex"": 0 }");

            Assert.False(result.Migrated);
            Assert.False(_secrets.Contains("provider:BltAI"));
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
            Migrate(@"{ ""ApiKeys"": { ""BltAI"": ""test-blt-key"" } }");
            _secrets.Set("provider:BltAI", "changed-later");

            LegacyMigrationResult second = LegacySettingsMigrator.TryMigrate(LegacyPath, Store(), _secrets);

            Assert.False(second.Migrated);
            Assert.Equal("changed-later", _secrets.Get("provider:BltAI"));
        }
    }
}
