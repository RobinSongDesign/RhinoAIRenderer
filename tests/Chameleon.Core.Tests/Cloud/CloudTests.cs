using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Backends;
using Chameleon.Core.Cloud;
using Chameleon.Core.Generation;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.Core.Tests.Cloud
{
    public class CloudTests : IDisposable
    {
        private readonly FakeGateway _gateway = new FakeGateway();
        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly TempDirectory _dir = new TempDirectory();
        private readonly List<TimeSpan> _delays = new List<TimeSpan>();
        private DateTimeOffset _now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        private readonly CloudApiClient _api;
        private readonly AuthSession _session;

        public CloudTests()
        {
            _api = new CloudApiClient(new HttpClient(_gateway) { Timeout = Timeout.InfiniteTimeSpan }, FakeGateway.BaseUrl);
            _session = new AuthSession(_api, _secrets, () => _now);
        }

        public void Dispose() => _dir.Dispose();

        // Records the delay instead of waiting, but still yields so a polling loop cannot spin
        // synchronously and starve the test that is driving it.
        private async Task NoDelay(TimeSpan delay, CancellationToken ct)
        {
            lock (_delays)
            {
                _delays.Add(delay);
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();
        }

        private int DelayCount
        {
            get
            {
                lock (_delays)
                {
                    return _delays.Count;
                }
            }
        }

        private async Task SignInAsync()
        {
            var flow = new DeviceLoginFlow(_api, _session, NoDelay);
            await flow.WaitForApprovalAsync(await flow.StartAsync("Chameleon", "2.0.0", "dev", CancellationToken.None), CancellationToken.None);
        }

        private CloudCatalogCache Catalog(string? file = null) => new CloudCatalogCache(_api, _session, file);

        private CloudBackend Backend(CloudCatalogCache catalog) => new CloudBackend(_api, _session, catalog, () => "Chameleon account", NoDelay);

        private static GenerationRequest Request(string imageSize = "1K") => new GenerationRequest
        {
            ModelId = "cloud-pro",
            Prompt = "render",
            SystemPrompt = "sys",
            Source = TestImages.Png(),
            ImageSize = imageSize,
            AspectRatio = "16:9",
            References = { new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style, "warm") }
        };

        // ── Endpoint ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("https://api.example.com", "https://api.example.com/")]
        [InlineData("https://api.example.com/base/", "https://api.example.com/base/")]
        [InlineData("http://127.0.0.1:5300", "http://127.0.0.1:5300/")]
        [InlineData("http://localhost:8080/x", "http://localhost:8080/x/")]
        [InlineData("http://api.example.com", null)]
        [InlineData("ftp://api.example.com", null)]
        [InlineData("not a url", null)]
        [InlineData("", null)]
        public void Endpoint_RequiresHttpsExceptOnLoopback(string input, string? expected)
        {
            Assert.Equal(expected, CloudEndpoint.TryParse(input)?.ToString());
        }

        // ── Device login ───────────────────────────────────────────────────────

        [Fact]
        public async Task DeviceLogin_PollsUntilApproved_AndStoresOnlyTheRefreshToken()
        {
            _gateway.PendingPolls = 2;
            var flow = new DeviceLoginFlow(_api, _session, NoDelay);
            int changes = 0;
            _session.Changed += (s, e) => changes++;

            DeviceAuthorization authorization = await flow.StartAsync("Chameleon", "2.0.0", "win", CancellationToken.None);
            CloudUser? user = await flow.WaitForApprovalAsync(authorization, CancellationToken.None);

            Assert.Equal("WDJB-MJHT", authorization.UserCode);
            Assert.Equal("user@example.com", user!.Email);
            Assert.Equal(new[] { 3, 3, 3 }, _delays.Select(d => (int)d.TotalSeconds).ToArray());
            Assert.True(_session.IsSignedIn);
            Assert.Equal(_gateway.CurrentRefreshToken, _secrets.Get(SecretNames.CloudRefreshToken));
            Assert.Equal(1, changes);
        }

        [Fact]
        public async Task DeviceLogin_SlowDownLengthensTheInterval()
        {
            var flow = new DeviceLoginFlow(_api, _session, NoDelay);
            DeviceAuthorization authorization = await flow.StartAsync("Chameleon", "2.0.0", "win", CancellationToken.None);
            _gateway.TokenError = "slow_down";
            using var cts = new CancellationTokenSource();
            Task wait = flow.WaitForApprovalAsync(authorization, cts.Token);
            while (DelayCount < 3)
            {
                await Task.Delay(5);
            }

            _gateway.TokenError = null;
            await wait;

            Assert.True(_delays[1] > _delays[0]);
            Assert.Equal(DeviceLoginFlow.SlowDownStep, _delays[1] - _delays[0]);
        }

        [Theory]
        [InlineData("expired_token")]
        [InlineData("access_denied")]
        public async Task DeviceLogin_ExpiredOrRefused_IsAuthFailed(string code)
        {
            _gateway.TokenError = code;
            var flow = new DeviceLoginFlow(_api, _session, NoDelay);
            DeviceAuthorization authorization = await flow.StartAsync("Chameleon", "2.0.0", "win", CancellationToken.None);

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => flow.WaitForApprovalAsync(authorization, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.False(_session.IsSignedIn);
        }

        [Fact]
        public async Task DeviceLogin_CanBeCancelled()
        {
            _gateway.PendingPolls = int.MaxValue;
            var flow = new DeviceLoginFlow(_api, _session, (d, ct) => Task.Delay(1, ct));
            DeviceAuthorization authorization = await flow.StartAsync("Chameleon", "2.0.0", "win", CancellationToken.None);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.WaitForApprovalAsync(authorization, cts.Token));
        }

        // ── Tokens ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task AccessToken_IsReusedUntilNearExpiry_ThenRotated()
        {
            await SignInAsync();
            string first = await _session.GetAccessTokenAsync(CancellationToken.None);

            _now = _now.AddMinutes(10);
            Assert.Equal(first, await _session.GetAccessTokenAsync(CancellationToken.None));

            _now = _now.AddMinutes(4.5);
            string second = await _session.GetAccessTokenAsync(CancellationToken.None);

            Assert.NotEqual(first, second);
            Assert.Equal(1, _gateway.RefreshCount);
            Assert.Equal(_gateway.CurrentRefreshToken, _secrets.Get(SecretNames.CloudRefreshToken));
        }

        [Fact]
        public async Task ConcurrentCallers_ShareOneRefresh()
        {
            await SignInAsync();
            _now = _now.AddHours(1);

            string[] tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _session.GetAccessTokenAsync(CancellationToken.None)));

            Assert.Equal(1, _gateway.RefreshCount);
            Assert.Single(tokens.Distinct());
        }

        [Fact]
        public async Task RejectedRefresh_EndsTheSession()
        {
            await SignInAsync();
            int changes = 0;
            _session.Changed += (s, e) => changes++;
            _gateway.RefreshRejected = true;
            _now = _now.AddHours(1);

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => _session.GetAccessTokenAsync(CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.False(_session.IsSignedIn);
            Assert.Null(_secrets.Get(SecretNames.CloudRefreshToken));
            Assert.Equal(1, changes);
        }

        [Fact]
        public async Task A401_TriggersOneRefreshAndRetry()
        {
            await SignInAsync();
            // The gateway rotated the access token (e.g. it restarted); the cached one is now rejected.
            _gateway.IssueTokens();
            _secrets.Set(SecretNames.CloudRefreshToken, _gateway.CurrentRefreshToken!);

            CloudUser user = await new AccountService(_api, _session, Catalog()).GetMeAsync(CancellationToken.None);

            Assert.Equal("user@example.com", user.Email);
            Assert.Equal(1, _gateway.RefreshCount);
        }

        [Fact]
        public async Task NotSignedIn_IsAuthFailedWithoutNetworkCalls()
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => new AccountService(_api, _session, Catalog()).GetMeAsync(CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.Empty(_gateway.Requests);
        }

        [Fact]
        public async Task SignOut_RevokesAndForgets()
        {
            await SignInAsync();

            await _session.SignOutAsync(CancellationToken.None);

            Assert.False(_session.IsSignedIn);
            Assert.Null(_gateway.CurrentAccessToken);
            Assert.Contains(_gateway.Requests, r => r.RequestUri!.AbsolutePath == "/v1/auth/logout");
        }

        // ── Account ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Redeem_NormalizesTheCode_AndReportsTheNewBalance()
        {
            await SignInAsync();
            var account = new AccountService(_api, _session, Catalog());

            RedeemResult result = await account.RedeemAsync(" test-test test-TEST ", CancellationToken.None);

            Assert.Equal(50, result.CreditsAdded);
            Assert.Equal(70, result.Credits);
            Assert.Equal(70, _session.User!.Credits);
            Assert.Equal("TESTTESTTESTTEST", (string)Newtonsoft.Json.Linq.JObject.Parse(_gateway.RequestBodies.Last())["code"]!);
        }

        [Fact]
        public async Task Redeem_KeepsGatewayReasonsDistinguishable()
        {
            await SignInAsync();
            var account = new AccountService(_api, _session, Catalog());

            CloudApiException used = await Assert.ThrowsAsync<CloudApiException>(() => account.RedeemAsync("USED-USED-USED-USED", CancellationToken.None));
            CloudApiException unknown = await Assert.ThrowsAsync<CloudApiException>(() => account.RedeemAsync("NOPE", CancellationToken.None));
            RenderException empty = await Assert.ThrowsAsync<RenderException>(() => account.RedeemAsync(" - ", CancellationToken.None));

            Assert.Equal(409, used.Status);
            Assert.Equal(404, unknown.Status);
            Assert.Equal(RenderErrorCode.InvalidRequest, empty.Code);
        }

        [Fact]
        public async Task Ledger_ReturnsEntries()
        {
            await SignInAsync();

            Page<LedgerEntry> page = await new AccountService(_api, _session, Catalog()).GetLedgerAsync(null, CancellationToken.None);

            Assert.Equal("redeem", Assert.Single(page.Items).Type);
            Assert.Null(page.NextCursor);
        }

        // ── Catalog ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Catalog_OffersOnlyPricedSizes_AndCapsConcurrencyByLimits()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();

            await catalog.RefreshAsync(CancellationToken.None);
            ModelInfo model = Assert.Single(catalog.GetModels());

            Assert.Equal("Cloud Pro", model.DisplayName);
            Assert.Equal(new[] { "1K", "2K" }, model.Capabilities.ImageSizes.ToArray());
            Assert.Equal(3, model.Capabilities.MaxConcurrency);
            Assert.Equal(8, catalog.GetPrice("cloud-pro", "2K"));
            Assert.Null(catalog.GetPrice("cloud-pro", "4K"));
            Assert.Equal(8, new AccountService(_api, _session, catalog).Estimate("cloud-pro", "2K"));
        }

        [Fact]
        public async Task Catalog_UsesETags_AndSurvivesRestartsOnDisk()
        {
            await SignInAsync();
            string file = _dir.File("cloud-catalog.json");
            CloudCatalogCache first = Catalog(file);
            await first.RefreshAsync(CancellationToken.None);
            await first.RefreshAsync(CancellationToken.None);

            CloudCatalogCache second = Catalog(file);

            Assert.Equal(2, _gateway.CatalogFetches);
            Assert.Contains(_gateway.Requests.Last().Headers, h => h.Key == "If-None-Match");
            Assert.Equal("cloud-pro", Assert.Single(second.GetModels()).Id);
            Assert.Equal("server prompt", second.Current!.DefaultSystemPrompt);
        }

        // ── Backend ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Backend_SubmitsPollsAndDownloads_ReportingCreditsCharged()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.RunningPolls = 2;
            CloudBackend backend = Backend(catalog);

            GenerationResult result = await backend.GenerateAsync(Request(), null, CancellationToken.None);

            Assert.Equal("image/webp", Assert.Single(result.Images).MimeType);
            Assert.Equal(4, result.Usage!.CreditsCharged);
            Assert.Equal("here you go", result.Text);
            Assert.Equal(16, _session.User!.Credits);
            Assert.True(backend.IsReady);
            Assert.Equal("cloud", backend.Id);

            HttpRequestMessage submit = _gateway.Requests.Single(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/v1/generations");
            string body = _gateway.RequestBodies[_gateway.Requests.IndexOf(submit)];
            Assert.Contains("name=request", body);
            Assert.Contains("name=source", body);
            Assert.Contains("name=ref0", body);
            Assert.Contains("\"part\":\"ref0\",\"role\":\"style\",\"note\":\"warm\"", body);
            Assert.Contains("\"aspectRatio\":\"16:9\"", body);
            Assert.Equal(3, _gateway.Requests.Count(r => r.RequestUri!.Query.Contains("wait=20")));
        }

        [Fact]
        public async Task Backend_RetriedSubmit_ReusesTheIdempotencyKey()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.SubmitFailures.Enqueue(503);

            await Backend(catalog).GenerateAsync(Request(), null, CancellationToken.None);

            string[] keys = _gateway.Requests
                .Where(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/v1/generations")
                .Select(r => r.Headers.GetValues("Idempotency-Key").Single())
                .ToArray();
            Assert.Equal(2, keys.Length);
            Assert.Single(keys.Distinct());
            Assert.Equal(16, _gateway.Credits);
        }

        [Fact]
        public async Task Backend_NotEnoughCredits_IsInsufficientCredits()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.Credits = 1;

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => Backend(catalog).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.InsufficientCredits, ex.Code);
        }

        [Fact]
        public async Task Backend_FailedGeneration_CarriesTheGatewayErrorAndDetail()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.GenerationError = "content_blocked";

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => Backend(catalog).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.ContentBlocked, ex.Code);
            Assert.Equal("provider said no", ex.ProviderMessage);
            Assert.Equal(20, _gateway.Credits);
        }

        [Fact]
        public async Task Backend_Cancellation_CancelsOnTheGateway()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.NeverFinish = true;
            using var cts = new CancellationTokenSource();
            var backend = new CloudBackend(_api, _session, catalog, () => "x", (d, ct) => Task.Delay(1, ct));

            Task<GenerationResult> pending = backend.GenerateAsync(Request(), null, cts.Token);
            while (!_gateway.GenerationStatus.Values.Contains("running"))
            {
                await Task.Delay(5);
            }

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Contains(_gateway.Requests, r => r.Method == HttpMethod.Delete);
            Assert.Equal("cancelled", _gateway.GenerationStatus.Values.Single());
        }

        [Fact]
        public async Task Backend_SignedOut_IsNotReadyAndRefuses()
        {
            CloudBackend backend = Backend(Catalog());

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => backend.GenerateAsync(Request(), null, CancellationToken.None));

            Assert.False(backend.IsReady);
            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.Empty(await backend.ListModelsAsync(CancellationToken.None));
            Assert.Empty(_gateway.Requests);
        }

        [Fact]
        public async Task Backend_RejectsSizesThatHaveNoPrice()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => Backend(catalog).GenerateAsync(Request("4K"), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.InvalidRequest, ex.Code);
        }

        [Fact]
        public async Task Backend_DoesNotSpinWhenTheGatewayIgnoresTheLongPoll()
        {
            await SignInAsync();
            CloudCatalogCache catalog = Catalog();
            await catalog.RefreshAsync(CancellationToken.None);
            _gateway.RunningPolls = 3;

            await Backend(catalog).GenerateAsync(Request(), null, CancellationToken.None);

            // The fake answers at once, so every unfinished poll is followed by a pause.
            Assert.Equal(3, _delays.Count(d => d > TimeSpan.Zero && d <= CloudBackend.MinPollInterval));
        }

        [Theory]
        [InlineData("/v1/generations/x/images/0")]
        [InlineData("https://gateway.test/v1/generations/x/images/0")]
        public async Task ImageUrls_RelativeOrOnTheGateway_AreDownloaded(string url)
        {
            await SignInAsync();
            string token = await _session.GetAccessTokenAsync(CancellationToken.None);
            _gateway.GenerationStatus["x"] = "succeeded";

            var image = await _api.DownloadImageAsync(token, new CloudGenerationImage { Url = url }, CancellationToken.None);

            Assert.Equal("image/webp", image.MimeType);
        }

        [Fact]
        public async Task ImageUrlsOnOtherHosts_AreNeverFollowedWithTheToken()
        {
            await SignInAsync();
            string token = await _session.GetAccessTokenAsync(CancellationToken.None);

            CloudApiException ex = await Assert.ThrowsAsync<CloudApiException>(() =>
                _api.DownloadImageAsync(token, new CloudGenerationImage { Url = "https://evil.test/steal" }, CancellationToken.None));

            Assert.Equal("provider_error", ex.Code);
            Assert.DoesNotContain(_gateway.Requests, r => r.RequestUri!.Host == "evil.test");
        }
    }
}
