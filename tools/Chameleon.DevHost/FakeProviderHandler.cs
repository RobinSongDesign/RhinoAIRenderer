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
    /// </summary>
    public sealed class FakeProviderHandler : HttpMessageHandler
    {
        /// <summary>The only key the fake providers accept.</summary>
        public const string GoodKey = "dev-good-key";

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
            if (!HasGoodKey(request))
            {
                return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"invalid_api_key\",\"status\":\"UNAUTHENTICATED\",\"message\":\"The development host only accepts the key '" + GoodKey + "'.\"}}");
            }

            if (path.EndsWith("/v1/models", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"data\":[]}");
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
            if (request.Headers.Authorization?.Parameter == GoodKey)
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
