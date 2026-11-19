using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class ProviderSignInTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        private async Task<JToken> ProviderAsync(string id)
        {
            JToken list = await _app.CallAsync("providers.list");
            return list.Single(p => (string)p["id"]! == id);
        }

        private async Task<JToken> BackendAsync(string id)
        {
            JToken state = await _app.CallAsync("app.getState");
            return state["backends"]!.Single(b => (string)b["id"]! == id);
        }

        [Fact]
        public async Task OnlyTheRelay_OffersSignIn()
        {
            Assert.True((bool)(await ProviderAsync("Chameleon"))["supportsSignIn"]!);
            Assert.False((bool)(await ProviderAsync("Gemini"))["supportsSignIn"]!);
            Assert.True((bool)(await BackendAsync("direct:Chameleon"))["supportsSignIn"]!);

            string code = await _app.ErrorCodeAsync("providers.signIn", new { id = "Gemini", username = "alice", password = TestApp.AccountPassword });
            Assert.Equal("invalid_request", code);
        }

        [Fact]
        public async Task SigningIn_StoresTheAccountsKey_AndShowsWhoIsSignedIn()
        {
            JToken result = await _app.CallAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = TestApp.AccountPassword });

            Assert.Equal("signedIn", (string?)result["status"]);
            Assert.Equal("alice", (string?)result["username"]);
            Assert.Equal("sk-" + TestApp.GoodKey, _app.Secrets.Get("provider:Chameleon"));
            JToken provider = await ProviderAsync("Chameleon");
            Assert.True((bool)provider["hasKey"]!);
            Assert.Equal("alice", (string?)provider["signedInAs"]);
            JToken backend = await BackendAsync("direct:Chameleon");
            Assert.True((bool)backend["ready"]!);
            Assert.Equal("alice", (string?)backend["signedInAs"]);
        }

        [Fact]
        public async Task ThePasswordAndKey_NeverLeaveTheSignIn()
        {
            await _app.CallAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = TestApp.AccountPassword });
            await _app.RpcAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = TestApp.AccountPassword + "-wrong" });
            await _app.CallAsync("app.getState");

            string settings = File.ReadAllText(Directory.GetFiles(_app.DataDirectory, "settings.json", SearchOption.AllDirectories).Single());
            foreach (string secret in new[] { TestApp.AccountPassword, TestApp.GoodKey, "test-session-123" })
            {
                Assert.DoesNotContain(secret, _app.AllResponses.ToString());
                Assert.DoesNotContain(secret, settings);
                Assert.DoesNotContain(_app.Host.LogLines, line => line.Contains(secret));
            }
        }

        [Fact]
        public async Task AWrongPassword_IsAnAuthFailure_AndStoresNothing()
        {
            JObject response = await _app.RpcAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = "nope" });

            Assert.Equal("auth_failed", (string?)response["error"]?["code"]);
            Assert.Equal("wrong password", (string?)response["error"]?["message"]);
            Assert.False(_app.Secrets.Contains("provider:Chameleon"));
            Assert.Null((await ProviderAsync("Chameleon"))["signedInAs"]?.Value<string>());
        }

        [Fact]
        public async Task TwoFactorAccounts_FinishWithTheCode()
        {
            JToken first = await _app.CallAsync("providers.signIn", new { id = "Chameleon", username = "bob", password = TestApp.AccountPassword });
            Assert.Equal("needsTwoFactor", (string?)first["status"]);
            Assert.False(_app.Secrets.Contains("provider:Chameleon"));

            string wrong = await _app.ErrorCodeAsync("providers.signInVerify", new { id = "Chameleon", flowToken = (string?)first["flowToken"], code = "000000" });
            Assert.Equal("auth_failed", wrong);

            JToken second = await _app.CallAsync("providers.signInVerify", new { id = "Chameleon", flowToken = (string?)first["flowToken"], code = "654321" });
            Assert.Equal("signedIn", (string?)second["status"]);
            Assert.Equal("bob", (string?)(await ProviderAsync("Chameleon"))["signedInAs"]);
        }

        [Fact]
        public async Task PastingAKey_ReplacesTheSignedInAccount()
        {
            await _app.CallAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = TestApp.AccountPassword });

            await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = TestApp.GoodKey });

            JToken provider = await ProviderAsync("Chameleon");
            Assert.True((bool)provider["hasKey"]!);
            Assert.Null(provider["signedInAs"]?.Value<string>());
        }

        [Fact]
        public async Task SigningOut_ForgetsTheKeyAndTheAccount()
        {
            await _app.CallAsync("providers.signIn", new { id = "Chameleon", username = "alice", password = TestApp.AccountPassword });

            await _app.CallAsync("providers.signOut", new { id = "Chameleon" });

            Assert.False(_app.Secrets.Contains("provider:Chameleon"));
            JToken backend = await BackendAsync("direct:Chameleon");
            Assert.False((bool)backend["ready"]!);
            Assert.Null(backend["signedInAs"]?.Value<string>());
        }
    }
}
