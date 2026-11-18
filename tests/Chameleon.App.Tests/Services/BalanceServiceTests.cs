using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class BalanceServiceTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        private async Task<string> SaveRelayAsync(object? extra = null)
        {
            var provider = JObject.FromObject(new
            {
                displayName = "My relay",
                baseUrl = "https://relay.example.com",
                models = new[] { "gemini-3.1-flash-image-preview" },
                balanceApi = "oneapi",
                topUpUrl = "https://relay.example.com/topup"
            });
            if (extra != null)
            {
                provider.Merge(JObject.FromObject(extra), new JsonMergeSettings { MergeNullValueHandling = MergeNullValueHandling.Merge });
            }

            return (string)(await _app.CallAsync("providers.save", provider))["id"]!;
        }

        [Fact]
        public async Task UserProviders_KeepBalanceAndTopUpSettings_AndBackendsAdvertiseThem()
        {
            string id = await SaveRelayAsync();

            JToken provider = (await _app.CallAsync("providers.list")).Single(p => (string)p["id"]! == id);
            JToken backend = (await _app.CallAsync("app.getState"))["backends"]!.Single(b => (string)b["id"]! == "direct:" + id);

            Assert.Equal("oneapi", (string)provider["balanceApi"]!);
            Assert.Equal("https://relay.example.com/topup", (string)provider["topUpUrl"]!);
            Assert.True((bool)backend["supportsBalance"]!);
            Assert.Equal("https://relay.example.com/topup", (string)backend["topUpUrl"]!);
        }

        [Fact]
        public async Task Balance_IsQueriedWithTheStoredKey()
        {
            string id = await SaveRelayAsync();
            await _app.CallAsync("providers.setKey", new { id, key = TestApp.GoodKey });

            JToken balance = await _app.CallAsync("providers.balance", new { id });

            Assert.Equal(1.172m, (decimal)balance["remaining"]!);
            Assert.Equal(0.828m, (decimal)balance["used"]!);
            Assert.Equal("USD", (string)balance["currency"]!);
            Assert.DoesNotContain(TestApp.GoodKey, _app.AllResponses.ToString());
        }

        [Fact]
        public async Task Balance_NeedsAKeyAndAProviderThatReportsIt()
        {
            string id = await SaveRelayAsync();
            string plain = await SaveRelayAsync(new { displayName = "Plain", balanceApi = (string?)null });

            Assert.Equal("not_ready", await _app.ErrorCodeAsync("providers.balance", new { id }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("providers.balance", new { id = plain }));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("providers.balance", new { id = "nope" }));
        }

        [Fact]
        public async Task ARejectedKey_IsAnAuthFailure()
        {
            string id = await SaveRelayAsync();
            await _app.CallAsync("providers.setKey", new { id, key = "test-wrong-key" });

            Assert.Equal("auth_failed", await _app.ErrorCodeAsync("providers.balance", new { id }));
        }

        [Fact]
        public async Task Save_RejectsUnknownBalanceApisAndNonHttpsTopUpPages()
        {
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("providers.save", new
            {
                displayName = "X", baseUrl = "https://x.example.com", models = new[] { "m" }, balanceApi = "mystery"
            }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("providers.save", new
            {
                displayName = "X", baseUrl = "https://x.example.com", models = new[] { "m" }, topUpUrl = "http://x.example.com/pay"
            }));
        }
    }
}
