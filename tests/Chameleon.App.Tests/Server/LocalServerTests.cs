using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Server;
using Chameleon.Core.Imaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Server
{
    public class LocalServerTests : IDisposable
    {
        private readonly TempDirectory _site = new TempDirectory();
        private readonly RpcRouter _router = new RpcRouter();
        private readonly EventBus _events = new EventBus();
        private readonly InMemoryAssetStore _assets = new InMemoryAssetStore();
        private readonly HttpClient _client = new HttpClient();
        private LocalServer _server;

        public LocalServerTests()
        {
            File.WriteAllText(_site.File("index.html"), "<html>shell</html>");
            Directory.CreateDirectory(Path.Combine(_site.Path, "assets"));
            File.WriteAllText(Path.Combine(_site.Path, "assets", "app.js"), "console.log(1)");
            File.WriteAllText(Path.Combine(_site.Path, "..", Path.GetFileName(_site.Path) + "-secret.txt"), "outside the site root");

            _router.Register("test.ping", ct => Task.FromResult("pong"));
            _router.Register<JObject, JObject>("test.echo", (p, ct) => Task.FromResult(p));
            _server = StartServer();
        }

        public void Dispose()
        {
            _server.Dispose();
            _client.Dispose();
            File.Delete(Path.Combine(_site.Path, "..", Path.GetFileName(_site.Path) + "-secret.txt"));
            _site.Dispose();
        }

        private LocalServer StartServer(Action<LocalServerOptions>? configure = null)
        {
            var options = new LocalServerOptions(new DirectoryFileProvider(_site.Path), _router, _events, _assets);
            configure?.Invoke(options);
            var server = new LocalServer(options);
            server.Start();
            return server;
        }

        private string Url(string route) => _server.BaseUrl + route;

        private async Task<JObject> RpcAsync(string method, object? parameters = null)
        {
            var body = new JObject { ["id"] = "1", ["method"] = method };
            if (parameters != null)
            {
                body["params"] = JToken.FromObject(parameters);
            }

            HttpResponseMessage response = await _client.PostAsync(Url("rpc"), new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        // ── Static files ───────────────────────────────────────────────────────

        [Fact]
        public async Task Root_ServesTheShellWithoutCaching()
        {
            HttpResponseMessage response = await _client.GetAsync(Url(""));

            Assert.Equal("<html>shell</html>", await response.Content.ReadAsStringAsync());
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.NoCache);
        }

        [Fact]
        public async Task BuildAssets_AreServedWithLongCaching()
        {
            HttpResponseMessage response = await _client.GetAsync(Url("assets/app.js"));

            Assert.Equal("console.log(1)", await response.Content.ReadAsStringAsync());
            Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl!.MaxAge);
        }

        [Fact]
        public async Task UnknownRouteWithoutExtension_FallsBackToTheShell_ButMissingFilesAre404()
        {
            Assert.Equal("<html>shell</html>", await _client.GetStringAsync(Url("settings/providers")));
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Url("assets/missing.js"))).StatusCode);
        }

        [Theory]
        [InlineData("assets/..%2F..%2F{secret}")]
        [InlineData("..%2F{secret}")]
        [InlineData("assets%5C..%5Cindex.html")]
        public async Task PathTraversal_IsRejected(string route)
        {
            string secret = Path.GetFileName(_site.Path) + "-secret.txt";

            HttpResponseMessage response = await _client.GetAsync(Url(route.Replace("{secret}", secret)));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ── Guards ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task WrongToken_Is404ForEveryEndpoint()
        {
            string wrong = "http://127.0.0.1:" + _server.Port + "/not-the-token/";

            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(wrong)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync(wrong + "rpc", new StringContent("{}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("http://127.0.0.1:" + _server.Port + "/")).StatusCode);
        }

        [Fact]
        public async Task ForeignHostHeader_IsRejected()
        {
            // What a DNS-rebinding page would send: our address, someone else's host name.
            var request = new HttpRequestMessage(HttpMethod.Get, Url(""));
            request.Headers.Host = "evil.example.com";

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ForeignOrigin_Is403_OwnOriginIsAccepted_AndNoCorsHeadersAreSent()
        {
            var foreign = new HttpRequestMessage(HttpMethod.Post, Url("rpc")) { Content = new StringContent("{\"id\":\"1\",\"method\":\"test.ping\"}") };
            foreign.Headers.Add("Origin", "https://evil.example.com");
            var own = new HttpRequestMessage(HttpMethod.Post, Url("rpc")) { Content = new StringContent("{\"id\":\"1\",\"method\":\"test.ping\"}") };
            own.Headers.Add("Origin", "http://127.0.0.1:" + _server.Port);

            HttpResponseMessage foreignResponse = await _client.SendAsync(foreign);
            HttpResponseMessage ownResponse = await _client.SendAsync(own);

            Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
            Assert.False(ownResponse.Headers.Contains("Access-Control-Allow-Origin"));
        }

        [Fact]
        public async Task DevOrigin_GetsCorsHeadersAndPreflight_OnlyWhenConfigured()
        {
            const string devOrigin = "http://localhost:5173";
            using LocalServer dev = StartServer(o => o.AllowedDevOrigin = devOrigin);

            var preflight = new HttpRequestMessage(HttpMethod.Options, dev.BaseUrl + "rpc");
            preflight.Headers.Add("Origin", devOrigin);
            HttpResponseMessage preflightResponse = await _client.SendAsync(preflight);

            var call = new HttpRequestMessage(HttpMethod.Post, dev.BaseUrl + "rpc") { Content = new StringContent("{\"id\":\"1\",\"method\":\"test.ping\"}") };
            call.Headers.Add("Origin", devOrigin);
            HttpResponseMessage callResponse = await _client.SendAsync(call);

            Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
            Assert.Equal(devOrigin, preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);
            Assert.Equal(devOrigin, callResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

            // The default server (no dev origin) refuses the same origin.
            var refused = new HttpRequestMessage(HttpMethod.Post, Url("rpc")) { Content = new StringContent("{}") };
            refused.Headers.Add("Origin", devOrigin);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(refused)).StatusCode);
        }

        // ── RPC ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Rpc_RoundTripsResultsAndErrors()
        {
            JObject ok = await RpcAsync("test.echo", new { text = "你好", n = 3 });
            JObject failed = await RpcAsync("no.such.method");

            Assert.Equal("1", (string)ok["id"]!);
            Assert.Equal("你好", (string)ok["result"]!["text"]!);
            Assert.Equal("invalid_request", (string)failed["error"]!["code"]!);
        }

        [Fact]
        public async Task Rpc_MalformedJson_Is400_AndOversizedBodyIs413()
        {
            HttpResponseMessage malformed = await _client.PostAsync(Url("rpc"), new StringContent("{ not json"));
            HttpResponseMessage oversized = await _client.PostAsync(Url("rpc"), new StringContent(new string('x', LocalServer.MaxRpcBodyBytes + 1)));

            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        }

        [Fact]
        public async Task Rpc_WrongHttpMethod_Is404()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Url("rpc"))).StatusCode);
        }

        [Fact]
        public async Task Rpc_HandlesConcurrentCalls()
        {
            JObject[] responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => RpcAsync("test.echo", new { i })));

            Assert.Equal(Enumerable.Range(0, 20), responses.Select(r => (int)r["result"]!["i"]!).OrderBy(i => i));
        }

        // ── Events ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Events_StreamPublishedEventsAsServerSentEvents()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            HttpResponseMessage response = await _client.GetAsync(Url("events"), HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            Assert.Equal(": connected", await reader.ReadLineAsync());

            // The subscription exists once the first frame has been written.
            _events.Publish("job.updated", new { id = "j1", state = "running" });
            _events.Publish("settings.changed", new { language = "zh" });

            var lines = new System.Collections.Generic.List<string>();
            while (lines.Count(l => l.StartsWith("data:", StringComparison.Ordinal)) < 2)
            {
                string? line = await reader.ReadLineAsync().WaitAsync(cts.Token);
                if (!string.IsNullOrEmpty(line))
                {
                    lines.Add(line!);
                }
            }

            Assert.Equal(
                new[]
                {
                    "event: job.updated", "data: {\"id\":\"j1\",\"state\":\"running\"}",
                    "event: settings.changed", "data: {\"language\":\"zh\"}"
                },
                lines.ToArray());
        }

        [Fact]
        public async Task Events_ClosingTheServerEndsTheStreamAndReleasesTheSubscription()
        {
            HttpResponseMessage response = await _client.GetAsync(Url("events"), HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            await reader.ReadLineAsync();
            Assert.Equal(1, _events.SubscriberCount);

            _server.Dispose();

            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (_events.SubscriberCount > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Equal(0, _events.SubscriberCount);
            _server = StartServer();
        }

        // ── Assets ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Assets_UploadThenDownload_RoundTripsBytes()
        {
            ImageData image = TestImages.Jpeg();
            var upload = new ByteArrayContent(image.Bytes);
            upload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            JObject asset = JObject.Parse(await (await _client.PostAsync(Url("assets"), upload)).Content.ReadAsStringAsync());
            HttpResponseMessage download = await _client.GetAsync(Url("assets/" + (string)asset["assetId"]!));

            Assert.Equal(image.Sha256Hex, (string)asset["assetId"]!);
            Assert.Equal("image/jpeg", (string)asset["mimeType"]!);
            Assert.Equal(80, (int)asset["width"]!);
            Assert.Equal(60, (int)asset["height"]!);
            Assert.Equal(image.Bytes.Length, (long)asset["bytes"]!);
            Assert.Equal(image.Bytes, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/jpeg", download.Content.Headers.ContentType!.MediaType);
            Assert.Equal("\"" + image.Sha256Hex + "\"", download.Headers.ETag!.Tag);
        }

        [Fact]
        public async Task Assets_ConditionalGet_Is304()
        {
            AssetRef asset = _assets.Put(TestImages.Png());
            var request = new HttpRequestMessage(HttpMethod.Get, Url("assets/" + asset.AssetId));
            request.Headers.TryAddWithoutValidation("If-None-Match", "\"" + asset.AssetId + "\"");

            Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(request)).StatusCode);
        }

        [Fact]
        public async Task Assets_UnknownOrMalformedId_Is404()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Url("assets/" + new string('a', 64)))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Url("assets/not-an-id"))).StatusCode);
        }

        [Fact]
        public async Task Assets_UploadOfNonImage_Is415_RegardlessOfDeclaredType()
        {
            var upload = new ByteArrayContent(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
            upload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await _client.PostAsync(Url("assets"), upload)).StatusCode);
            Assert.Equal(0, _assets.Count);
        }

        [Fact]
        public async Task Assets_OversizedUpload_Is413()
        {
            var upload = new ByteArrayContent(new byte[LocalServer.MaxUploadBytes + 1]);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _client.PostAsync(Url("assets"), upload)).StatusCode);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        [Fact]
        public void BaseUrl_IsLoopbackWithARandomToken()
        {
            using LocalServer other = StartServer();

            Assert.StartsWith("http://127.0.0.1:" + _server.Port + "/", _server.BaseUrl);
            Assert.EndsWith("/", _server.BaseUrl);
            string token = _server.BaseUrl.TrimEnd('/').Split('/').Last();
            string otherToken = other.BaseUrl.TrimEnd('/').Split('/').Last();
            Assert.Equal(64, token.Length);
            Assert.NotEqual(token, otherToken);
        }

        [Fact]
        public async Task FixedPortAndToken_AreHonored()
        {
            using LocalServer probe = StartServer();
            int port = probe.Port;
            probe.Dispose();

            using LocalServer fixedServer = StartServer(o =>
            {
                o.Port = port;
                o.Token = "dev";
            });

            Assert.Equal("http://127.0.0.1:" + port + "/dev/", fixedServer.BaseUrl);
            Assert.Equal("<html>shell</html>", await _client.GetStringAsync(fixedServer.BaseUrl));
        }

        [Fact]
        public async Task Dispose_StopsAcceptingConnections()
        {
            using LocalServer server = StartServer();
            string url = server.BaseUrl;
            server.Dispose();
            server.Dispose();

            await Assert.ThrowsAnyAsync<HttpRequestException>(() => _client.GetAsync(url));
        }
    }
}
