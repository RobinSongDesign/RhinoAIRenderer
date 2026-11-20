using System.Collections.Generic;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.Core.Tests.Settings
{
    public class RetiredProvidersTests
    {
        private static UserProviderConfig Provider(string id, string name, string url)
        {
            return new UserProviderConfig { Id = id, DisplayName = name, BaseUrl = url, ApiFormat = "gemini", AuthType = "bearer", Models = new List<string> { "m" }, DefaultModel = "m" };
        }

        [Theory]
        [InlineData("https://hk-api.gptbest.vip", true)]
        [InlineData("https://api.bltcy.ai/v1", true)]
        [InlineData("https://GPTBEST.VIP", true)]
        [InlineData("https://notgptbest.vip", false)]
        [InlineData("https://api.robinsong.top", false)]
        [InlineData("not a url", false)]
        [InlineData(null, false)]
        public void RecognizesTheRetiredServiceByItsDomains(string? url, bool retired)
        {
            Assert.Equal(retired, RetiredProviders.IsRetiredEndpoint(url));
        }

        [Fact]
        public void UserCopiesCountByNameOrAddress()
        {
            Assert.True(RetiredProviders.IsRetired(Provider("a", "柏拉图Gemini", "https://mirror.example.com")));
            Assert.True(RetiredProviders.IsRetired(Provider("b", "My relay", "https://hk-api.gptbest.vip")));
            Assert.False(RetiredProviders.IsRetired(Provider("c", "My relay", "https://relay.example.com")));
        }

        [Fact]
        public void Prune_RemovesCopiesAndTheOldBuiltIn_WithKeysSelectionAndCaches()
        {
            var secrets = new InMemorySecretStore();
            secrets.Set("provider:blt1", "test-key-1");
            secrets.Set("provider:BltAI", "test-key-2");
            secrets.Set("provider:mine", "test-key-3");
            var settings = new AppSettings
            {
                ActiveBackendId = "direct:blt1",
                UserProviders = new List<UserProviderConfig> { Provider("blt1", "柏拉图Gemini", "https://hk-api.gptbest.vip"), Provider("mine", "Mine", "https://relay.example.com") },
            };
            settings.ActiveModelByBackend["direct:blt1"] = "m";
            settings.DiscoveredModels["blt1"] = new DiscoveredModels();

            Assert.True(RetiredProviders.HasAny(settings, secrets));
            IReadOnlyList<string> removed = RetiredProviders.Prune(settings, secrets, "direct:Chameleon");

            Assert.Equal(new[] { "blt1", "BltAI" }, removed);
            Assert.Equal("mine", Assert.Single(settings.UserProviders).Id);
            Assert.False(secrets.Contains("provider:blt1"));
            Assert.False(secrets.Contains("provider:BltAI"));
            Assert.True(secrets.Contains("provider:mine"));
            Assert.Equal("direct:Chameleon", settings.ActiveBackendId);
            Assert.Empty(settings.ActiveModelByBackend);
            Assert.Empty(settings.DiscoveredModels);
            Assert.False(RetiredProviders.HasAny(settings, secrets));
            Assert.Empty(RetiredProviders.Prune(settings, secrets, "direct:Chameleon"));
        }
    }
}
