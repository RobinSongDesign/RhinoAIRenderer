using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.Core.Tests.Settings
{
    public class JsonSettingsStoreTests : IDisposable
    {
        private readonly TempDirectory _dir = new TempDirectory();

        private JsonSettingsStore Store(string name = "settings.json")
        {
            return new JsonSettingsStore(
                _dir.File(name),
                () => AppSettingsDefaults.Create(CatalogLoader.LoadBuiltIn(), new CultureInfo("en-US")));
        }

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void Load_WithoutFile_ReturnsDefaults()
        {
            JsonSettingsStore store = Store();

            AppSettings settings = store.Load();

            Assert.False(store.Exists);
            Assert.Equal(2, settings.SchemaVersion);
            Assert.Equal("en", settings.Language);
            Assert.Equal("direct:BltAI", settings.ActiveBackendId);
            Assert.Equal("gemini-3.1-flash-image-preview", settings.ActiveModelByBackend["direct:BltAI"]);
            Assert.Null(settings.SystemPrompt);
            Assert.Equal("serial", settings.Batch.Mode);
            Assert.Equal(3, settings.Batch.MaxConsistencyRefs);
        }

        [Theory]
        [InlineData("zh-CN", "zh")]
        [InlineData("zh-TW", "zh")]
        [InlineData("en-GB", "en")]
        [InlineData("ja-JP", "en")]
        public void Defaults_PickLanguageFromUiCulture(string culture, string expected)
        {
            Assert.Equal(expected, AppSettingsDefaults.Create(CatalogLoader.LoadBuiltIn(), new CultureInfo(culture)).Language);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            JsonSettingsStore store = Store();
            AppSettings settings = store.Load();
            settings.Language = "zh";
            settings.ActiveBackendId = "direct:relay1";
            settings.ActiveModelByBackend["direct:relay1"] = "gpt-image-2";
            settings.SystemPrompt = "my system prompt";
            settings.Batch.Mode = BatchSettings.ModeAnchorParallel;
            settings.Batch.MaxConsistencyRefs = 5;
            settings.Cloud.BaseUrl = "https://api.example.com";
            settings.UserProviders.Add(new UserProviderConfig
            {
                Id = "relay1", DisplayName = "Relay", BaseUrl = "https://relay.example.com",
                ApiFormat = "openai", AuthType = "bearer", Models = { "gpt-image-2" }, DefaultModel = "gpt-image-2",
                EnableSearchGrounding = false
            });
            settings.BuiltInOverrides["BltAI"] = new ProviderOverride { BaseUrl = "https://mirror.example.com" };

            store.Save(settings);
            AppSettings loaded = Store().Load();

            Assert.True(store.Exists);
            Assert.Equal("zh", loaded.Language);
            Assert.Equal("direct:relay1", loaded.ActiveBackendId);
            Assert.Equal("gpt-image-2", loaded.ActiveModelByBackend["direct:relay1"]);
            Assert.Equal("my system prompt", loaded.SystemPrompt);
            Assert.Equal("anchorParallel", loaded.Batch.Mode);
            Assert.Equal(5, loaded.Batch.MaxConsistencyRefs);
            Assert.Equal("https://api.example.com", loaded.Cloud.BaseUrl);
            UserProviderConfig relay = Assert.Single(loaded.UserProviders);
            Assert.Equal("openai", relay.ApiFormat);
            Assert.False(relay.EnableSearchGrounding);
            Assert.Equal("https://mirror.example.com", loaded.BuiltInOverrides["BltAI"].BaseUrl);
            Assert.Null(loaded.BuiltInOverrides["BltAI"].DisplayName);
        }

        [Fact]
        public void Save_KeepsDictionaryKeysVerbatimInTheFile()
        {
            JsonSettingsStore store = Store();
            AppSettings settings = store.Load();
            settings.BuiltInOverrides["BltAI"] = new ProviderOverride { BaseUrl = "https://x" };
            store.Save(settings);

            string json = File.ReadAllText(_dir.File("settings.json"));

            // camelCase applies to properties only; provider ids are identifiers.
            Assert.Contains("\"BltAI\"", json);
            Assert.Contains("\"direct:BltAI\"", json);
            Assert.Contains("\"schemaVersion\": 2", json);
        }

        [Fact]
        public void Load_ReturnsIndependentCopies()
        {
            JsonSettingsStore store = Store();
            AppSettings first = store.Load();
            first.Language = "zh";
            first.UserProviders.Add(new UserProviderConfig { Id = "x" });

            AppSettings second = store.Load();

            Assert.Equal("en", second.Language);
            Assert.Empty(second.UserProviders);
        }

        [Fact]
        public void Load_CorruptFile_ReturnsDefaultsAndKeepsTheDamagedFile()
        {
            File.WriteAllText(_dir.File("settings.json"), "{ this is not json");

            AppSettings settings = Store().Load();

            Assert.Equal("direct:BltAI", settings.ActiveBackendId);
            Assert.False(File.Exists(_dir.File("settings.json")));
            string quarantined = Directory.GetFiles(_dir.Path, "settings.corrupt-*.json").Single();
            Assert.Equal("{ this is not json", File.ReadAllText(quarantined));
        }

        [Fact]
        public void Load_NullDocument_ReturnsDefaults()
        {
            File.WriteAllText(_dir.File("settings.json"), "null");

            Assert.Equal("direct:BltAI", Store().Load().ActiveBackendId);
        }

        [Fact]
        public void Save_RaisesChangedAndLeavesNoTempFile()
        {
            JsonSettingsStore store = Store();
            int raised = 0;
            store.Changed += (s, e) => raised++;

            store.Save(store.Load());
            store.Save(store.Load());

            Assert.Equal(2, raised);
            Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void Save_ConcurrentWriters_NeverCorruptTheFile()
        {
            JsonSettingsStore store = Store();

            Parallel.For(0, 40, i =>
            {
                AppSettings settings = store.Load();
                settings.SystemPrompt = "prompt " + i;
                store.Save(settings);
            });

            AppSettings loaded = Store().Load();
            Assert.StartsWith("prompt ", loaded.SystemPrompt);
            Assert.Empty(Directory.GetFiles(_dir.Path, "*.corrupt-*"));
        }

        [Fact]
        public void Save_CreatesMissingDirectories()
        {
            var store = new JsonSettingsStore(
                Path.Combine(_dir.Path, "a", "b", "settings.json"),
                () => new AppSettings());

            store.Save(new AppSettings());

            Assert.True(store.Exists);
        }
    }
}
