using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Tests
{
    /// <summary>Shared helpers for adapter and backend tests.</summary>
    internal static class AdapterTestKit
    {
        public const string ApiKey = "test-key-123";

        public static ProviderProfile GeminiProfile(string authType = AuthTypes.Goog, bool searchGrounding = true)
        {
            return new ProviderProfile
            {
                Id = "TestGemini",
                DisplayName = "Test Gemini",
                BaseUrl = "https://gemini.test",
                ApiFormat = ApiFormats.Gemini,
                AuthType = authType,
                DefaultModel = "gemini-3-pro-image",
                Models = { CatalogLoader.LoadBuiltIn().ResolveModel(ApiFormats.Gemini, "gemini-3-pro-image") },
                EnableSearchGrounding = searchGrounding
            };
        }

        public static ProviderProfile OpenAIProfile()
        {
            return new ProviderProfile
            {
                Id = "TestOpenAI",
                DisplayName = "Test OpenAI",
                BaseUrl = "https://openai.test",
                ApiFormat = ApiFormats.OpenAI,
                AuthType = AuthTypes.Bearer,
                DefaultModel = "gpt-image-2",
                Models = { CatalogLoader.LoadBuiltIn().ResolveModel(ApiFormats.OpenAI, "gpt-image-2") }
            };
        }

        public static AdapterContext Context(ProviderProfile profile, HttpClient? client = null)
        {
            return new AdapterContext(profile, ApiKey, client ?? new HttpClient(new ThrowingHandler()));
        }

        public static async Task<JObject> ReadJsonBodyAsync(HttpRequestMessage request)
        {
            return JObject.Parse(await request.Content!.ReadAsStringAsync());
        }

        public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json, HttpRequestMessage? request = null)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }

        public static string Base64(byte[] bytes) => Convert.ToBase64String(bytes);

        /// <summary>Fails the test if an adapter unexpectedly performs network I/O.</summary>
        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Unexpected HTTP call to " + request.RequestUri);
            }
        }
    }
}
