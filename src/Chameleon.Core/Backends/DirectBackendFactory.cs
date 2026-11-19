using System;
using System.Net.Http;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.Core.Backends
{
    /// <summary>Creates <see cref="DirectBackend"/> instances that share one HTTP client.</summary>
    public sealed class DirectBackendFactory
    {
        private readonly AdapterRegistry _adapters;
        private readonly HttpClient _httpClient;
        private readonly PromptComposer _composer;
        private readonly RetryPolicy? _retryPolicy;

        /// <summary>Creates a factory.</summary>
        /// <param name="adapters">Available wire formats.</param>
        /// <param name="httpClient">Shared client; see <see cref="CreateSharedHttpClient"/>.</param>
        /// <param name="composer">Prompt composer shared by all backends.</param>
        /// <param name="retryPolicy">Retry behavior; null for the default.</param>
        public DirectBackendFactory(
            AdapterRegistry adapters,
            HttpClient httpClient,
            PromptComposer composer,
            RetryPolicy? retryPolicy = null)
        {
            _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _retryPolicy = retryPolicy;
        }

        /// <summary>
        /// Creates the HTTP client all backends should share. Its own timeout is disabled because
        /// <see cref="DirectBackend"/> enforces a per-attempt timeout it can tell apart from a
        /// caller's cancellation.
        /// </summary>
        public static HttpClient CreateSharedHttpClient(HttpMessageHandler? handler = null)
        {
            // No cookie jar: every API here authenticates per request, and a gateway's login cookie
            // must not ride along on later calls.
            HttpClient client = handler != null ? new HttpClient(handler) : new HttpClient(new HttpClientHandler { UseCookies = false });
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            return client;
        }

        /// <summary>Creates a backend for a provider, reading its key from <paramref name="secrets"/>.</summary>
        public DirectBackend Create(ProviderProfile profile, ISecretStore secrets)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return new DirectBackend(profile, _adapters.Get(profile.ApiFormat), secrets, _httpClient, _composer, _retryPolicy);
        }
    }
}
