using System;
using System.Net.Http;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Providers
{
    /// <summary>What an adapter needs to talk to one endpoint on behalf of one request.</summary>
    public sealed class AdapterContext
    {
        /// <summary>Creates a context.</summary>
        /// <param name="profile">Endpoint to call.</param>
        /// <param name="apiKey">Secret used to authenticate; never logged.</param>
        /// <param name="httpClient">Shared client for follow-up downloads (e.g. result URLs).</param>
        public AdapterContext(ProviderProfile profile, string apiKey, HttpClient httpClient)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>Endpoint to call.</summary>
        public ProviderProfile Profile { get; }

        /// <summary>Secret used to authenticate.</summary>
        public string ApiKey { get; }

        /// <summary>Shared HTTP client. Adapters must not change its default headers.</summary>
        public HttpClient HttpClient { get; }

        /// <summary>Removes the API key from text that is about to leave the adapter.</summary>
        public string? Redact(string? text)
        {
            return Redactor.Redact(text, ApiKey);
        }
    }
}
