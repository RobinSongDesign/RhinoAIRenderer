using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class ModelDiscoveryTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        private static readonly string[] Offered = { "gemini-nano-banana-2.1", "gemini-3-pro-image", "gemini-nano-banana-3" };

        /// <summary>Stores a key and waits for the discovery it starts in the background to land.</summary>
        private async Task SetKeyAndWaitForDiscoveryAsync(string providerId)
        {
            await _app.CallAsync("providers.setKey", new { id = providerId, key = TestApp.GoodKey });
            await _app.WaitForEventAsync("backends.changed", b =>
                b["backends"]!.Single(x => (string)x["id"]! == "direct:" + providerId)["models"]!.Select(m => (string)m["id"]!).SequenceEqual(Offered));
        }

        private async Task<string[]> ModelsOfAsync(string backendId)
        {
            JToken state = await _app.CallAsync("app.getState");
            return state["backends"]!.Single(b => (string)b["id"]! == backendId)["models"]!.Select(m => (string)m["id"]!).ToArray();
        }

        [Fact]
        public async Task SavingAKey_ForABuiltIn_PullsTheModelsTheEndpointOffers()
        {
            await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = TestApp.GoodKey });

            JToken announced = await _app.WaitForEventAsync("backends.changed", b =>
                b["backends"]!.Single(x => (string)x["id"]! == "direct:Chameleon")["models"]!.Count() == 3);

            string[] models = announced["backends"]!.Single(x => (string)x["id"]! == "direct:Chameleon")["models"]!.Select(m => (string)m["id"]!).ToArray();
            Assert.Equal(Offered, models);
            // Google direct has no key and keeps the catalog's list.
            Assert.Equal(4, (await ModelsOfAsync("direct:Gemini")).Length);
        }

        [Fact]
        public async Task Discover_ReportsWhatWasAddedAndRemoved()
        {
            await SetKeyAndWaitForDiscoveryAsync("Gemini");
            _app.OfferedModels.Clear();
            _app.OfferedModels.Add("gemini-3-pro-image");
            _app.OfferedModels.Add("gemini-nano-banana-4");

            JToken result = await _app.CallAsync("providers.discoverModels", new { id = "Gemini" });

            Assert.Equal(new[] { "gemini-3-pro-image", "gemini-nano-banana-4" }, result["models"]!.Select(m => (string)m!).ToArray());
            Assert.Equal(new[] { "gemini-nano-banana-4" }, result["added"]!.Select(m => (string)m!).ToArray());
            Assert.Equal(new[] { "gemini-nano-banana-2.1", "gemini-nano-banana-3" }, result["removed"]!.Select(m => (string)m!).ToArray());
            Assert.Equal(new[] { "gemini-3-pro-image", "gemini-nano-banana-4" }, await ModelsOfAsync("direct:Gemini"));
        }

        [Fact]
        public async Task Discover_OnAUserProvider_ReplacesItsList()
        {
            string id = (string)(await _app.CallAsync("providers.save", new
            {
                displayName = "Relay", baseUrl = "https://relay.example.com", models = new[] { "old-model" }
            }))["id"]!;
            await _app.CallAsync("providers.setKey", new { id, key = TestApp.GoodKey });

            await _app.CallAsync("providers.discoverModels", new { id });

            JToken provider = (await _app.CallAsync("providers.list")).Single(p => (string)p["id"]! == id);
            Assert.Equal(Offered, provider["models"]!.Select(m => (string)m!).ToArray());
            Assert.Equal("gemini-nano-banana-2.1", (string)provider["defaultModel"]!);
        }

        [Fact]
        public async Task Discover_NeedsAKey_AndAnEmptyAnswerChangesNothing()
        {
            Assert.Equal("not_ready", await _app.ErrorCodeAsync("providers.discoverModels", new { id = "Gemini" }));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("providers.discoverModels", new { id = "nope" }));

            await SetKeyAndWaitForDiscoveryAsync("Gemini");
            _app.OfferedModels.Clear();
            await _app.CallAsync("providers.discoverModels", new { id = "Gemini" });

            Assert.Equal(Offered, await ModelsOfAsync("direct:Gemini"));
        }
    }
}
