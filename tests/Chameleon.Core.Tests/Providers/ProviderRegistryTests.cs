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

            Assert.Equal(new[] { "Chameleon", "Gemini" }, providers.Select(p => p.Id).ToArray());
            ProviderProfile gemini = providers[1];
            Assert.True(gemini.IsBuiltIn);
            Assert.False(gemini.IsOverridden);
            Assert.Equal("Google原生", gemini.DisplayName);
            Assert.Equal("goog", gemini.AuthType);
            Assert.Equal("https://aistudio.google.com/app/apikey", gemini.ApiKeyUrl);
            Assert.True(gemini.EnableSearchGrounding);
            Assert.Equal("Nano Banana 2", gemini.FindModel("gemini-3.1-flash-image")!.DisplayName);
        }

        [Fact]
        public void Build_UserProviders_KeepKnownBalanceApisAndOnlyHttpsTopUpPages()
        {
            IReadOnlyList<ProviderProfile> result = Registry.Build(
                new[]
                {
                    new UserProviderConfig { Id = "relay", DisplayName = "Relay", BaseUrl = "https://relay.test", Models = new List<string> { "m" }, BalanceApi = BalanceApis.OneApi, TopUpUrl = "https://relay.test/topup" },
                    new UserProviderConfig { Id = "odd", DisplayName = "Odd", BaseUrl = "https://odd.test", Models = new List<string> { "m" }, BalanceApi = "mystery", TopUpUrl = "javascript:alert(1)" },
                    new UserProviderConfig { Id = "plain", DisplayName = "Plain", BaseUrl = "https://plain.test", Models = new List<string> { "m" }, TopUpUrl = "http://plain.test/topup" }
                },
                null,
                "en");

            ProviderProfile relay = result.Single(p => p.Id == "relay");
            Assert.Equal(BalanceApis.OneApi, relay.BalanceApi);
            Assert.Equal("https://relay.test/topup", relay.TopUpUrl);
            Assert.Null(result.Single(p => p.Id == "odd").BalanceApi);
            Assert.Null(result.Single(p => p.Id == "odd").TopUpUrl);
            Assert.Null(result.Single(p => p.Id == "plain").TopUpUrl);
        }

        [Fact]
        public void Build_UsesRequestedLanguageForBuiltInNames()
        {
            Assert.Equal("Chameleon account", Registry.Build(null, null, "en")[0].DisplayName);
            Assert.Equal("Google (native)", Registry.Build(null, null, "en")[1].DisplayName);
            Assert.Equal("Chameleon 账号", Registry.Build(null, null, "zh")[0].DisplayName);
        }

        [Fact]
        public void Build_OverrideReplacesOnlyNonEmptyFields()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["Chameleon"] = new ProviderOverride { BaseUrl = "https://mirror.example.com/", DisplayName = "  " }
            };

            ProviderProfile account = Registry.Build(null, overrides, "zh").Single(p => p.Id == "Chameleon");

            Assert.True(account.IsOverridden);
            Assert.Equal("https://mirror.example.com", account.BaseUrl);
            Assert.Equal("Chameleon 账号", account.DisplayName);
            Assert.Equal("bearer", account.AuthType);
            Assert.Equal(4, account.Models.Count);
            Assert.Equal("gemini-nano-banana-2.1", account.DefaultModel);
            // An override cannot redirect the balance query or the top-up page.
            Assert.Equal(BalanceApis.OneApi, account.BalanceApi);
            Assert.StartsWith("https://api.rhchameleon.top", account.TopUpUrl);
        }

        [Fact]
        public void Build_OverrideCanReplaceModelsAndDefault()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["Chameleon"] = new ProviderOverride
                {
                    Models = new List<string> { "custom-a", " gemini-3-pro-image ", "custom-a" },
                    DefaultModel = "gemini-3-pro-image",
                    EnableSearchGrounding = false
                }
            };

            ProviderProfile blt = Registry.Build(null, overrides, "zh").Single(p => p.Id == "Chameleon");

            Assert.Equal(new[] { "custom-a", "gemini-3-pro-image" }, blt.Models.Select(m => m.Id).ToArray());
            Assert.Equal("gemini-3-pro-image", blt.DefaultModel);
            Assert.Equal("custom-a", blt.Models[0].DisplayName);
            Assert.Equal("Nano Banana Pro", blt.Models[1].DisplayName);
            Assert.False(blt.EnableSearchGrounding);
        }

        [Fact]
        public void Build_OverrideWithoutValidDefaultFallsBackToFirstModel()
        {
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["Chameleon"] = new ProviderOverride { Models = new List<string> { "only-model" }, DefaultModel = "missing" }
            };

            Assert.Equal("only-model", Registry.Build(null, overrides, "zh").Single(p => p.Id == "Chameleon").DefaultModel);
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

            Assert.Equal(new[] { "Chameleon", "Gemini", "dup" }, providers.Select(p => p.Id).ToArray());
            Assert.Equal("Google (native)", providers[1].DisplayName);
            Assert.Equal("First", providers[2].DisplayName);
        }
    }
}
