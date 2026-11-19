using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Accounts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.Core.Tests.Providers
{
    public class NewApiAccountClientTests
    {
        private const string KeyName = "Chameleon for Rhino (TEST-PC)";
        private const string Password = "test-password-123";
        private const string Session = "test-session-123";

        private static ProviderProfile Relay(string? signInApi = SignInApis.NewApi)
        {
            return new ProviderProfile { Id = "relay", DisplayName = "Relay", BaseUrl = "https://relay.test/", SignInApi = signInApi };
        }

        /// <summary>A scripted gateway that records each request with its body (bodies are disposed after sending).</summary>
        private sealed class Gateway
        {
            public Gateway()
            {
                Handler = new StubHttpHandler(async (request, ct) =>
                {
                    string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
                    string route = request.Method + " " + request.RequestUri!.PathAndQuery;
                    Calls.Add((route, body, request));
                    return AdapterTestKit.JsonResponse(Status, Routes.TryGetValue(route, out string? json) ? json : "{\"success\":false,\"message\":\"no route " + route + "\"}", request);
                });
                Client = new NewApiAccountClient(new HttpClient(Handler));
            }

            public StubHttpHandler Handler { get; }

            public NewApiAccountClient Client { get; }

            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

            public Dictionary<string, string> Routes { get; } = new Dictionary<string, string>
            {
                ["POST /api/user/login"] = TestFixtures.ReadText("relay/login_success.json"),
                ["POST /api/user/login/2fa"] = TestFixtures.ReadText("relay/login_success.json"),
                ["GET /api/token/?p=1&page_size=100"] = TestFixtures.ReadText("relay/token_list.json"),
                ["POST /api/token/9/key"] = "{\"success\":true,\"message\":\"\",\"data\":{\"key\":\"test-key-123\"}}"
            };

            public List<(string Route, string Body, HttpRequestMessage Request)> Calls { get; } = new List<(string, string, HttpRequestMessage)>();
        }

        [Fact]
        public async Task SignIn_ReusesTheNewestKeyOfThisInstallation_AndAddsTheSkPrefix()
        {
            var gateway = new Gateway();

            RelaySignInResult result = await gateway.Client.SignInAsync(Relay(), " alice ", Password, KeyName, CancellationToken.None);

            Assert.False(result.NeedsTwoFactor);
            Assert.Equal("sk-test-key-123", result.ApiKey);
            Assert.Equal("alice", result.Username);
            Assert.Equal(
                new[] { "POST /api/user/login", "GET /api/token/?p=1&page_size=100", "POST /api/token/9/key" },
                gateway.Calls.Select(c => c.Route));
            JObject login = JObject.Parse(gateway.Calls[0].Body);
            Assert.Equal("alice", (string?)login["username"]);
            Assert.Equal(Password, (string?)login["password"]);
            // The session authenticates the key calls; the login itself carries none.
            Assert.Null(gateway.Calls[0].Request.Headers.Authorization);
            Assert.All(gateway.Calls.Skip(1), c =>
            {
                Assert.Equal("Bearer " + Session, c.Request.Headers.Authorization!.ToString());
                Assert.Equal("7", c.Request.Headers.GetValues("New-Api-User").Single());
            });
        }

        [Fact]
        public async Task SignIn_CreatesAnUnlimitedKey_WhenThisInstallationHasNone()
        {
            var gateway = new Gateway();
            string empty = "{\"success\":true,\"message\":\"\",\"data\":{\"page\":1,\"page_size\":100,\"total\":0,\"items\":[]}}";
            string created = TestFixtures.ReadText("relay/token_list.json");
            int lists = 0;
            gateway.Routes["POST /api/token/"] = "{\"success\":true,\"message\":\"\"}";
            // First listing: nothing yet; after the create: the new key is there.
            var handler = new StubHttpHandler(async (request, ct) =>
            {
                string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
                string route = request.Method + " " + request.RequestUri!.PathAndQuery;
                gateway.Calls.Add((route, body, request));
                string json = route.StartsWith("GET /api/token/", StringComparison.Ordinal)
                    ? (lists++ == 0 ? empty : created)
                    : gateway.Routes[route];
                return AdapterTestKit.JsonResponse(HttpStatusCode.OK, json, request);
            });
            var client = new NewApiAccountClient(new HttpClient(handler));

            RelaySignInResult result = await client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None);

            Assert.Equal("sk-test-key-123", result.ApiKey);
            (string _, string createBody, HttpRequestMessage _) = gateway.Calls.Single(c => c.Route == "POST /api/token/");
            JObject create = JObject.Parse(createBody);
            Assert.Equal(KeyName, (string?)create["name"]);
            Assert.True((bool?)create["unlimited_quota"]);
            Assert.Equal(-1, (int?)create["expired_time"]);
        }

        [Fact]
        public async Task SignIn_KeepsAKeyThatAlreadyHasThePrefix()
        {
            var gateway = new Gateway();
            gateway.Routes["POST /api/token/9/key"] = "{\"success\":true,\"message\":\"\",\"data\":{\"key\":\"sk-test-key-123\"}}";

            RelaySignInResult result = await gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None);

            Assert.Equal("sk-test-key-123", result.ApiKey);
        }

        [Fact]
        public async Task TwoFactorAccounts_AskForACode_ThenFinishWithIt()
        {
            var gateway = new Gateway();
            gateway.Routes["POST /api/user/login"] = TestFixtures.ReadText("relay/login_2fa.json");

            RelaySignInResult first = await gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None);
            Assert.True(first.NeedsTwoFactor);
            Assert.Equal("test-flow-123", first.FlowToken);
            Assert.Null(first.ApiKey);
            Assert.Single(gateway.Calls);

            RelaySignInResult second = await gateway.Client.VerifyTwoFactorAsync(Relay(), "test-flow-123", " 654321 ", KeyName, CancellationToken.None);

            Assert.Equal("sk-test-key-123", second.ApiKey);
            JObject verify = JObject.Parse(gateway.Calls[1].Body);
            Assert.Equal("test-flow-123", (string?)verify["flow_token"]);
            Assert.Equal("2fa", (string?)verify["method"]);
            Assert.Equal("654321", (string?)verify["code"]);
        }

        [Fact]
        public async Task AccountsThatOnlyOfferPasskeys_AreSentToTheWebsite()
        {
            var gateway = new Gateway();
            gateway.Routes["POST /api/user/login"] = TestFixtures.ReadText("relay/login_passkey_only.json");

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
        }

        [Fact]
        public async Task WrongCredentials_AreAnAuthFailure_WithTheGatewaysMessage()
        {
            var gateway = new Gateway();
            gateway.Routes["POST /api/user/login"] = "{\"success\":false,\"message\":\"用户名或密码错误，或用户已被封禁\"}";

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.Equal("用户名或密码错误，或用户已被封禁", ex.Message);
            Assert.Single(gateway.Calls);
        }

        [Fact]
        public async Task HttpFailures_NeverEchoThePassword()
        {
            var gateway = new Gateway { Status = HttpStatusCode.TooManyRequests };
            gateway.Routes["POST /api/user/login"] = "{\"message\":\"slow down, got " + Password + "\"}";

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None));

            Assert.Equal(RenderErrorCode.RateLimited, ex.Code);
            Assert.DoesNotContain(Password, ex.ProviderMessage ?? string.Empty);
            Assert.DoesNotContain(Password, ex.Message);
        }

        [Fact]
        public async Task KeyCallFailures_NeverEchoTheSession()
        {
            var gateway = new Gateway();
            gateway.Routes["POST /api/token/9/key"] = "{\"success\":false,\"message\":\"bad session " + Session + "\"}";

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(), "alice", Password, KeyName, CancellationToken.None));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
            Assert.DoesNotContain(Session, ex.ProviderMessage ?? string.Empty);
        }

        [Fact]
        public async Task EmptyCredentials_AndProvidersWithoutSignIn_AreRejectedLocally()
        {
            var gateway = new Gateway();

            RenderException empty = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(), " ", Password, KeyName, CancellationToken.None));
            RenderException unsupported = await Assert.ThrowsAsync<RenderException>(() =>
                gateway.Client.SignInAsync(Relay(signInApi: null), "alice", Password, KeyName, CancellationToken.None));

            Assert.Equal(RenderErrorCode.InvalidRequest, empty.Code);
            Assert.Equal(RenderErrorCode.InvalidRequest, unsupported.Code);
            Assert.Empty(gateway.Calls);
        }

        [Fact]
        public void CatalogRelay_OffersSignIn_AndUnknownSignInApisAreDropped()
        {
            var catalog = Chameleon.Core.Catalog.CatalogLoader.LoadBuiltIn();
            Assert.Equal(SignInApis.NewApi, catalog.FindProvider("Chameleon")!.SignInApi);

            catalog.FindProvider("Gemini")!.SignInApi = "something-new";
            var registry = new ProviderRegistry(catalog);
            List<ProviderProfile> profiles = registry.Build(null, null, "zh").ToList();
            Assert.Equal(SignInApis.NewApi, profiles.Single(p => p.Id == "Chameleon").SignInApi);
            Assert.Null(profiles.Single(p => p.Id == "Gemini").SignInApi);
        }
    }
}
