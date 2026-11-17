using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.App.Server;
using Chameleon.Core.Backends;
using Chameleon.Core.Imaging;
using Chameleon.Core.Settings;
using Newtonsoft.Json.Linq;

namespace Chameleon.App.Tests.Services
{
    /// <summary>
    /// Starts the real application (real server, real services, real core) with a scripted host,
    /// an in-memory secret store, a scripted extra backend and a stubbed provider endpoint.
    /// </summary>
    internal sealed class TestApp : IDisposable
    {
        public const string GoodKey = "test-key-123";

        private readonly TempDirectory _directory = new TempDirectory();
        private readonly HttpClient _client = new HttpClient();
        private readonly IEventSubscription _subscription;

        public TestApp(Action<ChameleonAppOptions>? configure = null, IAppPaths? paths = null)
        {
            var options = new ChameleonAppOptions(Host, paths ?? AppPaths.UnderRoot(_directory.Path), new PlaceholderFiles())
            {
                SecretStore = Secrets,
                HttpMessageHandler = new ProviderStub(this),
                RetryPolicy = RetryPolicy.None
            };
            options.ExtraBackends.Add(Backend);
            configure?.Invoke(options);

            App = ChameleonApp.Start(options);
            _subscription = App.Events.Subscribe();
        }

        public TestHost Host { get; } = new TestHost();

        public InMemorySecretStore Secrets { get; } = new InMemorySecretStore();

        public ScriptedBackend Backend { get; } = new ScriptedBackend();

        public ChameleonApp App { get; }

        public string DataDirectory => _directory.Path;

        /// <summary>Every provider HTTP request the app made.</summary>
        public List<HttpRequestMessage> ProviderRequests { get; } = new List<HttpRequestMessage>();

        /// <summary>Raw text of every RPC response, for "the key never leaks" assertions.</summary>
        public StringBuilder AllResponses { get; } = new StringBuilder();

        public async Task<JObject> RpcAsync(string method, object? parameters = null)
        {
            var body = new JObject { ["id"] = "1", ["method"] = method };
            if (parameters != null)
            {
                body["params"] = parameters as JToken ?? JToken.FromObject(parameters);
            }

            HttpResponseMessage response = await _client.PostAsync(App.BaseUrl + "rpc", new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();
            AllResponses.AppendLine(text);
            return JObject.Parse(text);
        }

        /// <summary>Calls a method and returns its result, failing the test when it reports an error.</summary>
        public async Task<JToken> CallAsync(string method, object? parameters = null)
        {
            JObject response = await RpcAsync(method, parameters);
            if (response["error"] != null)
            {
                throw new Xunit.Sdk.XunitException(method + " failed: " + response["error"]);
            }

            return response["result"]!;
        }

        /// <summary>Calls a method that is expected to fail and returns its error code.</summary>
        public async Task<string> ErrorCodeAsync(string method, object? parameters = null)
        {
            JObject response = await RpcAsync(method, parameters);
            return (string?)response["error"]?["code"] ?? "(no error)";
        }

        public async Task<string> UploadAsync(ImageData image)
        {
            HttpResponseMessage response = await _client.PostAsync(App.BaseUrl + "assets", new ByteArrayContent(image.Bytes));
            return (string)JObject.Parse(await response.Content.ReadAsStringAsync())["assetId"]!;
        }

        public Task<byte[]> DownloadAsync(string assetId)
        {
            return _client.GetByteArrayAsync(App.BaseUrl + "assets/" + assetId);
        }

        /// <summary>Waits for the next event with the given name, skipping others.</summary>
        public async Task<JToken> WaitForEventAsync(string name, Func<JToken, bool>? predicate = null)
        {
            return await TryWaitForEventAsync(name, predicate, TimeSpan.FromSeconds(10))
                ?? throw new TimeoutException("Event '" + name + "' did not arrive.");
        }

        /// <summary>Like <see cref="WaitForEventAsync"/>, but returns null when nothing matching arrives in time.</summary>
        public async Task<JToken?> TryWaitForEventAsync(string name, Func<JToken, bool>? predicate, TimeSpan wait)
        {
            using var timeout = new CancellationTokenSource(wait);
            while (true)
            {
                BridgeEvent? next;
                try
                {
                    next = await _subscription.ReadAsync(wait, timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                if (next == null)
                {
                    return null;
                }

                if (next.Name == name)
                {
                    JToken data = JToken.Parse(next.Json);
                    if (predicate == null || predicate(data))
                    {
                        return data;
                    }
                }
            }
        }

        public void Dispose()
        {
            _subscription.Dispose();
            App.Dispose();
            _client.Dispose();
            _directory.Dispose();
        }

        private sealed class PlaceholderFiles : IStaticFileProvider
        {
            public System.IO.Stream? Open(string relativePath) => null;
        }

        /// <summary>Provider endpoint that accepts exactly one key.</summary>
        private sealed class ProviderStub : HttpMessageHandler
        {
            private readonly TestApp _owner;

            public ProviderStub(TestApp owner)
            {
                _owner = owner;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (_owner.ProviderRequests)
                {
                    _owner.ProviderRequests.Add(request);
                }

                bool authorized = request.Headers.Authorization?.Parameter == GoodKey ||
                                  (request.Headers.TryGetValues("x-goog-api-key", out var values) && string.Join(",", values) == GoodKey);
                if (!authorized)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("{\"error\":{\"status\":\"UNAUTHENTICATED\",\"message\":\"bad key\"}}")
                    });
                }

                string image = Convert.ToBase64String(TestImages.Webp().Bytes);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/webp\",\"data\":\"" + image + "\"}}]}}]}")
                });
            }
        }
    }
}
