using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Xunit;

namespace Chameleon.Core.Tests.Providers
{
    public class ProviderRegistryTests
    {
        private static readonly ProviderRegistry Registry = new ProviderRegistry(CatalogLoader.LoadBuiltIn());

        [Fact]
        public void Build_WithoutUserData_ReturnsBuiltInsInCatalogOrder()
        {
            IReadOnlyList<ProviderProfile> providers = Registry.Build(null, null, "zh");

            Assert.Equal(new[] { "Gemini", "BltAI" }, providers.Select(p => p.Id).ToArray());
            ProviderProfile gemini = providers[0];
            Assert.True(gemini.IsBuiltIn);
            Assert.False(gemini.IsOverridden);
            Assert.Equal("Google原生", gemini.DisplayName);
            Assert.Equal("goog", gemini.AuthType);
            Assert.Equal("https://aistudio.google.com/app/apikey", gemini.ApiKeyUrl);
            Assert.True(gemini.EnableSearchGrounding);
            Assert.Equal("Nano Banana 2", gemini.FindModel("gemini-3.1-flash-image-preview")!.DisplayName);
        }

        [Fact]
        public void Build_UsesRequestedLanguageForBuiltInNames()
        {
            Assert.Equal("Google (native)", Registry.Build(null, null, "en")[0].DisplayName);
        }

        [Fact]
        public void Build_OverrideReplacesOnlyNonEmptyFields()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["BltAI"] = new ProviderOverride { BaseUrl = "https://mirror.example.com/", DisplayName = "  " }
            };

            ProviderProfile blt = Registry.Build(null, overrides, "zh").Single(p => p.Id == "BltAI");

            Assert.True(blt.IsOverridden);
            Assert.Equal("https://mirror.example.com", blt.BaseUrl);
            Assert.Equal("柏拉图AI", blt.DisplayName);
            Assert.Equal("bearer", blt.AuthType);
            Assert.Equal(2, blt.Models.Count);
            Assert.Equal("gemini-3.1-flash-image-preview", blt.DefaultModel);
        }

        [Fact]
        public void Build_OverrideCanReplaceModelsAndDefault()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["BltAI"] = new ProviderOverride
                {
                    Models = new List<string> { "custom-a", " gemini-3-pro-image-preview ", "custom-a" },
                    DefaultModel = "gemini-3-pro-image-preview",
                    EnableSearchGrounding = false
                }
            };

            ProviderProfile blt = Registry.Build(null, overrides, "zh").Single(p => p.Id == "BltAI");

            Assert.Equal(new[] { "custom-a", "gemini-3-pro-image-preview" }, blt.Models.Select(m => m.Id).ToArray());
            Assert.Equal("gemini-3-pro-image-preview", blt.DefaultModel);
            Assert.Equal("custom-a", blt.Models[0].DisplayName);
            Assert.Equal("Nano Banana Pro", blt.Models[1].DisplayName);
            Assert.False(blt.EnableSearchGrounding);
        }

        [Fact]
        public void Build_OverrideWithoutValidDefaultFallsBackToFirstModel()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["BltAI"] = new ProviderOverride { Models = new List<string> { "only-model" }, DefaultModel = "missing" }
            };

            Assert.Equal("only-model", Registry.Build(null, overrides, "zh").Single(p => p.Id == "BltAI").DefaultModel);
        }

        [Fact]
        public void Build_OverrideNeverChangesTheApiKeyPage()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["Gemini"] = new ProviderOverride { BaseUrl = "https://evil.example.com" }
            };

            Assert.Equal(
                "https://aistudio.google.com/app/apikey",
                Registry.Build(null, overrides, "en").Single(p => p.Id == "Gemini").ApiKeyUrl);
        }

        [Fact]
        public void Build_AppendsUserProviders()
        {
            var user = new[]
            {
                new UserProviderConfig
                {
                    Id = "abc123",
                    DisplayName = "My Relay",
                    BaseUrl = "https://relay.example.com/",
                    ApiFormat = "openai",
                    AuthType = "bearer",
                    Models = new List<string> { "gpt-image-2" }
                }
            };

            IReadOnlyList<ProviderProfile> providers = Registry.Build(user, null, "en");
            ProviderProfile relay = providers.Last();

            Assert.Equal(3, providers.Count);
            Assert.False(relay.IsBuiltIn);
            Assert.Equal("My Relay", relay.DisplayName);
            Assert.Equal("https://relay.example.com", relay.BaseUrl);
            Assert.Equal("gpt-image-2", relay.DefaultModel);
            Assert.Null(relay.ApiKeyUrl);
            Assert.True(relay.Models[0].Capabilities.SupportsMask);
        }

        [Fact]
        public void Build_SkipsUserProvidersThatCollideWithExistingIds()
        {
            var user = new[]
            {
                new UserProviderConfig { Id = "Gemini", DisplayName = "Fake", BaseUrl = "https://x", Models = new List<string> { "m" } },
                new UserProviderConfig { Id = "dup", DisplayName = "First", BaseUrl = "https://x", Models = new List<string> { "m" } },
                new UserProviderConfig { Id = "dup", DisplayName = "Second", BaseUrl = "https://x", Models = new List<string> { "m" } },
                new UserProviderConfig { Id = " ", DisplayName = "NoId", BaseUrl = "https://x", Models = new List<string> { "m" } }
            };

            IReadOnlyList<ProviderProfile> providers = Registry.Build(user, null, "en");

            Assert.Equal(new[] { "Gemini", "BltAI", "dup" }, providers.Select(p => p.Id).ToArray());
            Assert.Equal("Google (native)", providers[0].DisplayName);
            Assert.Equal("First", providers[2].DisplayName);
        }
    }
}
