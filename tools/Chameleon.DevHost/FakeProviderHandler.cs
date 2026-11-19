using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Imaging;

namespace Chameleon.DevHost
{
    /// <summary>
    /// Answers provider HTTP calls locally so bring-your-own-key flows work offline: any request
    /// carrying the well-known development key succeeds, every other key is rejected with 401.
    /// Relay sign-in is served by <see cref="FakeRelayAccounts"/>.
    /// </summary>
    public sealed class FakeProviderHandler : HttpMessageHandler
    {
        /// <summary>The only key the fake providers accept.</summary>
        public const string GoodKey = "dev-good-key";

        private readonly FakeRelayAccounts _accounts = new FakeRelayAccounts();
        private readonly IReadOnlyList<string> _resultPaths;
        private readonly TimeSpan _delay;
        private int _counter;

        /// <summary>Creates the handler.</summary>
        public FakeProviderHandler(SampleImages samples, TimeSpan delay)
        {
            _resultPaths = samples.ResultPaths();
            _delay = delay;
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            // A relay's account API authenticates with a session, not with a key.
            HttpResponseMessage? account = await _accounts.TryHandleAsync(request).ConfigureAwait(false);
            if (account != null)
            {
                return account;
            }

            if (!HasGoodKey(request))
            {
                return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"invalid_api_key\",\"status\":\"UNAUTHENTICATED\",\"message\":\"The development host only accepts the key '" + GoodKey + "'.\"}}");
            }

            // Model listings for discovery: the built-in catalog's Gemini models, and one Images API model.
            if (path.EndsWith("/v1beta/models", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-nano-banana-2.1\"},{\"name\":\"models/gemini-3.1-flash-image\"},{\"name\":\"models/gemini-3-pro-image\"},{\"name\":\"models/gemini-3.1-flash-lite-image\"},{\"name\":\"models/gemini-4-carbon\"}]}");
            }

            if (path.EndsWith("/v1/models", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"data\":[{\"id\":\"gpt-image-2\"},{\"id\":\"gpt-5\"}]}");
            }

            // One API / new-api balance endpoints: $2 granted, 4 cents per generated image.
            if (path.EndsWith("/dashboard/billing/subscription", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"object\":\"billing_subscription\",\"hard_limit_usd\":2}");
            }

            if (path.EndsWith("/dashboard/billing/usage", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"object\":\"list\",\"total_usage\":" + (4 * Volatile.Read(ref _counter)) + "}");
            }

            // "#quota" in a prompt answers like a relay whose account is out of balance.
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("#quota"))
            {
                return Json(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"user quota is not enough\",\"type\":\"new_api_error\",\"code\":\"insufficient_user_quota\"}}");
            }

            bool openAi = path.Contains("/v1/images/");
            // The key check of a Gemini provider is a tiny text-only request: answer without the delay.
            long? length = request.Content?.Headers.ContentLength;
            if (!openAi && length.HasValue && length.Value < 512)
            {
                return Json(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}");
            }

            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            int index = Interlocked.Increment(ref _counter) - 1;
            ImageData image = SampleImages.Load(_resultPaths[index % _resultPaths.Count]);
            string base64 = Convert.ToBase64String(image.Bytes);

            return openAi
                ? Json(HttpStatusCode.OK, "{\"data\":[{\"b64_json\":\"" + base64 + "\"}]}")
                : Json(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"" + image.MimeType + "\",\"data\":\"" + base64 + "\"}}]}}]}");
        }

        private static bool HasGoodKey(HttpRequestMessage request)
        {
            // Keys obtained by signing in to a relay carry new-api's "sk-" prefix.
            string? bearer = request.Headers.Authorization?.Parameter;
            if (bearer == GoodKey || bearer == "sk-" + GoodKey)
            {
                return true;
            }

            return request.Headers.TryGetValues("x-goog-api-key", out IEnumerable<string>? values) && values.Contains(GoodKey);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
