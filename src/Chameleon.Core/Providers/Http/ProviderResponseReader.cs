using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Providers.Http
{
    /// <summary>Shared helpers for reading provider responses.</summary>
    public static class ProviderResponseReader
    {
        // Error bodies are short; anything longer is noise (or an HTML error page).
        private const int MaxErrorBodyChars = 16 * 1024;

        /// <summary>Reads at most the first 16K characters of a response body as text.</summary>
        public static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content == null)
            {
                return string.Empty;
            }

            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var buffer = new char[MaxErrorBodyChars];
                int total = 0;
                while (total < buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await reader.ReadAsync(buffer, total, buffer.Length - total).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                }

                return new string(buffer, 0, total);
            }
        }

        /// <summary>Parses a response body as a JSON object without building one big string first.</summary>
        /// <returns>The object, or null when the body is not a JSON object.</returns>
        public static async Task<JObject?> ReadJsonObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content == null)
            {
                return null;
            }

            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None })
            {
                try
                {
                    JToken token = await JToken.LoadAsync(json, cancellationToken).ConfigureAwait(false);
                    return token as JObject;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>Tries to parse text as a JSON object.</summary>
        public static JObject? TryParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JToken.Parse(text) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Reads the Retry-After header as a delay, when present.</summary>
        public static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                TimeSpan delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }

            return null;
        }

        /// <summary>Returns the X-Request-ID that was sent with the request, or an empty string.</summary>
        public static string GetRequestId(HttpResponseMessage response)
        {
            if (response.RequestMessage != null &&
                response.RequestMessage.Headers.TryGetValues(RequestIdHeader, out var values))
            {
                foreach (string value in values)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        /// <summary>Header carrying the client-generated request id.</summary>
        public const string RequestIdHeader = "X-Request-ID";
    }
}
