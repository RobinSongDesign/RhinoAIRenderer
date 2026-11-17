using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class AccountRpcServiceTests
    {
        [Fact]
        public async Task WithoutAGateway_AccountModeIsUnavailable()
        {
            using var app = new TestApp();

            JToken state = await app.CallAsync("app.getState");

            Assert.False((bool)state["account"]!["available"]!);
            Assert.DoesNotContain("cloud", state["backends"]!.Select(b => (string)b["id"]!));
            Assert.Equal("not_ready", await app.ErrorCodeAsync("auth.startLogin"));
            Assert.Equal("not_ready", await app.ErrorCodeAsync("account.redeem", new { code = "X" }));
        }

        [Fact]
        public async Task StartLogin_OpensTheVerificationPageAndReportsPending_CancelClearsIt()
        {
            var gateway = new MiniGateway();
            using var app = new TestApp(o => { o.CloudBaseUrl = "https://gateway.test"; o.HttpMessageHandler = gateway; });

            JToken login = await app.CallAsync("auth.startLogin");
            JToken pending = await app.WaitForEventAsync("account.changed", a => a["login"] != null);
            await app.CallAsync("auth.cancelLogin");
            JToken cleared = await app.WaitForEventAsync("account.changed", a => a["login"] == null);

            Assert.Equal("pending", (string)login["state"]!);
            Assert.Equal("ABCD-EFGH", (string)login["userCode"]!);
            Assert.Equal(new[] { "https://gateway.test/device?code=ABCD-EFGH" }, app.Host.OpenedUrls.ToArray());
            Assert.Equal("ABCD-EFGH", (string)pending["login"]!["userCode"]!);
            Assert.False((bool)cleared["signedIn"]!);
        }

        [Fact]
        public async Task Login_FailsVisiblyWhenTheBrowserRefuses()
        {
            var gateway = new MiniGateway { TokenError = "access_denied" };
            using var app = new TestApp(o => { o.CloudBaseUrl = "https://gateway.test"; o.HttpMessageHandler = gateway; });

            await app.CallAsync("auth.startLogin");
            JToken failed = await app.WaitForEventAsync("account.changed", a => (string?)a["login"]?["state"] == "failed");

            Assert.Equal("auth_failed", (string)failed["login"]!["error"]!["code"]!);
        }

        [Fact]
        public async Task Redeem_MapsGatewayAnswersToDistinctCodes()
        {
            var gateway = new MiniGateway();
            using var app = new TestApp(o => { o.CloudBaseUrl = "https://gateway.test"; o.HttpMessageHandler = gateway; });
            await app.CallAsync("auth.startLogin");
            await app.WaitForEventAsync("account.changed", a => (bool)a["signedIn"]!);

            gateway.RedeemStatus = 409;
            Assert.Equal("conflict", await app.ErrorCodeAsync("account.redeem", new { code = "A" }));
            gateway.RedeemStatus = 410;
            Assert.Equal("gone", await app.ErrorCodeAsync("account.redeem", new { code = "A" }));
            gateway.RedeemStatus = 404;
            Assert.Equal("not_found", await app.ErrorCodeAsync("account.redeem", new { code = "A" }));
        }

        [Fact]
        public async Task Login_IsAnnouncedOnlyOnceTheAccountCatalogIsLoaded()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gateway = new MiniGateway { CatalogRelease = release.Task };
            using var app = new TestApp(o => { o.CloudBaseUrl = "https://gateway.test"; o.HttpMessageHandler = gateway; });
            await app.CallAsync("backend.select", new { backendId = "cloud" });

            await app.CallAsync("auth.startLogin");
            await gateway.CatalogRequested;

            // Tokens are stored, but the catalog is still on its way: no sign-in announcement yet.
            Assert.Null(await app.TryWaitForEventAsync("account.changed", a => (bool)a["signedIn"]!, TimeSpan.FromMilliseconds(300)));

            release.SetResult(true);
            await app.WaitForEventAsync("account.changed", a => (bool)a["signedIn"]!);
            JToken state = await app.CallAsync("app.getState");
            Assert.Contains(state["catalog"]!["styleTemplates"]!, t => (string)t["id"]! == "gateway-style");
        }

        [Fact]
        public async Task CloudBaseUrl_MustBeHttps()
        {
            using var app = new TestApp(o => o.CloudBaseUrl = "http://gateway.test");

            Assert.False((bool)(await app.CallAsync("app.getState"))["account"]!["available"]!);
        }

        /// <summary>Just enough of the gateway for sign-in and redeem.</summary>
        private sealed class MiniGateway : HttpMessageHandler
        {
            public string? TokenError { get; set; }

            public int RedeemStatus { get; set; } = 404;

            /// <summary>When set, signed-in catalog requests wait for it.</summary>
            public Task? CatalogRelease { get; set; }

            /// <summary>Completes when a signed-in catalog request arrives.</summary>
            public Task CatalogRequested => _catalogRequested.Task;

            private readonly TaskCompletionSource<bool> _catalogRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath == "/v1/catalog" && request.Headers.Authorization != null)
                {
                    _catalogRequested.TrySetResult(true);
                    if (CatalogRelease != null)
                    {
                        await CatalogRelease;
                    }
                }

                return await RespondAsync(request);
            }

            private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
            {
                string path = request.RequestUri!.AbsolutePath;
                switch (path)
                {
                    case "/v1/auth/device":
                        return Json(200, "{\"deviceCode\":\"d\",\"userCode\":\"ABCD-EFGH\",\"verificationUrl\":\"https://gateway.test/device\",\"verificationUrlComplete\":\"https://gateway.test/device?code=ABCD-EFGH\",\"expiresIn\":600,\"interval\":1}");
                    case "/v1/auth/token":
                        return TokenError != null
                            ? Json(400, "{\"error\":{\"code\":\"" + TokenError + "\",\"message\":\"no\"}}")
                            : Json(200, "{\"accessToken\":\"a\",\"expiresIn\":900,\"refreshToken\":\"r\",\"user\":{\"id\":\"u\",\"email\":\"e@example.com\",\"credits\":5,\"createdAt\":\"2026-01-01T00:00:00Z\"}}");
                    case "/v1/catalog":
                        return Json(200, "{\"version\":\"1\",\"models\":[],\"styleTemplates\":[{\"id\":\"gateway-style\",\"name\":{\"zh\":\"网关\",\"en\":\"Gateway\"},\"prompt\":\"p\"}],\"defaultSystemPrompt\":\"\",\"imageSizes\":[],\"aspectRatios\":[],\"defaultImageSize\":\"1K\",\"limits\":{\"maxConcurrency\":1,\"maxUploadBytes\":1}}");
                    case "/v1/redeem":
                        return Json(RedeemStatus, "{\"error\":{\"code\":\"x\",\"message\":\"no\"}}");
                    default:
                        return Json(404, "{\"error\":{\"code\":\"not_found\",\"message\":\"no\"}}");
                }
            }

            private static Task<HttpResponseMessage> Json(int status, string body)
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }
    }
}
