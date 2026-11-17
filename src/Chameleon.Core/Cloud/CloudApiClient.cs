using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Providers.Http;
using Chameleon.Core.Serialization;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Cloud
{
    /// <summary>
    /// Typed calls of contracts/cloud-api.openapi.yaml. Stateless: authenticated calls take the
    /// access token as an argument (see <see cref="AuthSession.RunAuthorizedAsync{T}"/>).
    /// </summary>
    public sealed class CloudApiClient
    {
        /// <summary>Longest time a single call may take (long polls wait up to 25 s on the server).</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

        private readonly HttpClient _http;

        /// <summary>Creates a client.</summary>
        /// <param name="http">Shared client with an infinite timeout.</param>
        /// <param name="baseUrl">Gateway address ending in "/" (see <see cref="CloudEndpoint.TryParse"/>).</param>
        public CloudApiClient(HttpClient http, Uri baseUrl)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>Gateway address.</summary>
        public Uri BaseUrl { get; }

        // ── Authentication (no token) ──────────────────────────────────────────

        /// <summary>Starts the device authorization flow.</summary>
        public Task<DeviceAuthorization> StartDeviceAuthorizationAsync(string clientName, string clientVersion, string platform, CancellationToken ct)
        {
            return SendJsonAsync<DeviceAuthorization>(HttpMethod.Post, "v1/auth/device", null,
                new { clientName, clientVersion, platform }, ct);
        }

        /// <summary>Polls for tokens; throws a <see cref="CloudApiException"/> with the flow's code while not confirmed.</summary>
        public Task<TokenResponse> PollDeviceTokenAsync(string deviceCode, CancellationToken ct)
        {
            return SendJsonAsync<TokenResponse>(HttpMethod.Post, "v1/auth/token", null, new { deviceCode }, ct);
        }

        /// <summary>Rotates the tokens.</summary>
        public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            return SendJsonAsync<TokenResponse>(HttpMethod.Post, "v1/auth/refresh", null, new { refreshToken }, ct);
        }

        /// <summary>Revokes the session of the given access token.</summary>
        public Task LogoutAsync(string accessToken, CancellationToken ct)
        {
            return SendJsonAsync<JToken>(HttpMethod.Post, "v1/auth/logout", accessToken, null, ct);
        }

        // ── Account ────────────────────────────────────────────────────────────

        /// <summary>The signed-in user.</summary>
        public Task<CloudUser> GetMeAsync(string accessToken, CancellationToken ct)
        {
            return SendJsonAsync<CloudUser>(HttpMethod.Get, "v1/me", accessToken, null, ct);
        }

        /// <summary>Redeems a code.</summary>
        public Task<RedeemResult> RedeemAsync(string accessToken, string code, string idempotencyKey, CancellationToken ct)
        {
            return SendJsonAsync<RedeemResult>(HttpMethod.Post, "v1/redeem", accessToken, new { code }, ct, idempotencyKey);
        }

        /// <summary>One page of the credit ledger, newest first.</summary>
        public Task<Page<LedgerEntry>> GetLedgerAsync(string accessToken, string? cursor, CancellationToken ct)
        {
            string path = "v1/ledger" + (string.IsNullOrEmpty(cursor) ? string.Empty : "?cursor=" + Uri.EscapeDataString(cursor!));
            return SendJsonAsync<Page<LedgerEntry>>(HttpMethod.Get, path, accessToken, null, ct);
        }

        // ── Catalog ────────────────────────────────────────────────────────────

        /// <summary>Fetches the catalog; returns null when it has not changed since <paramref name="etag"/>.</summary>
        public async Task<(CloudCatalog? Catalog, string? ETag)> GetCatalogAsync(string accessToken, string? etag, CancellationToken ct)
        {
            using (var request = CreateRequest(HttpMethod.Get, "v1/catalog", accessToken))
            {
                if (!string.IsNullOrEmpty(etag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                }

                using (HttpResponseMessage response = await SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotModified)
                    {
                        return (null, etag);
                    }

                    await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return (Deserialize<CloudCatalog>(json), response.Headers.ETag?.ToString());
                }
            }
        }

        // ── Generations ────────────────────────────────────────────────────────

        /// <summary>Submits a generation. Images are sent as multipart parts named in the request JSON.</summary>
        public async Task<GenerationAccepted> SubmitGenerationAsync(string accessToken, GenerationRequest request, string idempotencyKey, CancellationToken ct)
        {
            var references = request.GetOrderedReferences();
            var json = new JObject
            {
                ["modelId"] = request.ModelId,
                ["prompt"] = request.Prompt,
                ["imageSize"] = request.ImageSize,
                ["references"] = new JArray(references.Select((r, i) => (object)new JObject
                {
                    ["part"] = "ref" + i,
                    ["role"] = r.Role.ToString().ToLowerInvariant(),
                    ["note"] = r.Note
                }).ToArray())
            };
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                json["systemPrompt"] = request.SystemPrompt;
            }

            if (!string.IsNullOrEmpty(request.AspectRatio))
            {
                json["aspectRatio"] = request.AspectRatio;
            }

            if (request.Extras.Count > 0)
            {
                json["extras"] = JObject.FromObject(request.Extras);
            }

            using (var form = new MultipartFormDataContent())
            {
                var requestPart = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                form.Add(requestPart, "request");
                AddImage(form, "source", request.Source);
                AddImage(form, "mask", request.Mask);
                for (int i = 0; i < references.Count; i++)
                {
                    AddImage(form, "ref" + i, references[i].Image);
                }

                using (var message = CreateRequest(HttpMethod.Post, "v1/generations", accessToken))
                {
                    message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                    message.Content = form;
                    using (HttpResponseMessage response = await SendAsync(message, ct).ConfigureAwait(false))
                    {
                        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                        return Deserialize<GenerationAccepted>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    }
                }
            }
        }

        /// <summary>Reads a generation, waiting up to <paramref name="waitSeconds"/> for it to finish.</summary>
        public Task<CloudGeneration> GetGenerationAsync(string accessToken, string id, int waitSeconds, CancellationToken ct)
        {
            string path = "v1/generations/" + Uri.EscapeDataString(id) + (waitSeconds > 0 ? "?wait=" + waitSeconds.ToString(CultureInfo.InvariantCulture) : string.Empty);
            return SendJsonAsync<CloudGeneration>(HttpMethod.Get, path, accessToken, null, ct);
        }

        /// <summary>Asks the gateway to cancel a generation.</summary>
        public Task CancelGenerationAsync(string accessToken, string id, CancellationToken ct)
        {
            return SendJsonAsync<JToken>(HttpMethod.Delete, "v1/generations/" + Uri.EscapeDataString(id), accessToken, null, ct);
        }

        /// <summary>Downloads a result image.</summary>
        public async Task<ImageData> DownloadImageAsync(string accessToken, CloudGenerationImage image, CancellationToken ct)
        {
            // The URL is a path on the gateway; never follow it anywhere else with the token attached.
            string path = image.Url.TrimStart('/');
            // Only http(s) URLs count as absolute: on macOS and Linux .NET also parses "/v1/..." as
            // an absolute file:// URI.
            if ((image.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 image.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                Uri.TryCreate(image.Url, UriKind.Absolute, out Uri? absolute))
            {
                if (absolute.Host != BaseUrl.Host || absolute.Port != BaseUrl.Port)
                {
                    throw new CloudApiException(0, "provider_error", "The gateway returned an image URL on another host.");
                }

                path = absolute.PathAndQuery.TrimStart('/');
            }

            using (var request = CreateRequest(HttpMethod.Get, path, accessToken))
            using (HttpResponseMessage response = await SendAsync(request, ct).ConfigureAwait(false))
            {
                await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return ImageData.TryFromBytes(bytes)
                       ?? throw new CloudApiException((int)response.StatusCode, "no_image", "The gateway returned data that is not an image.");
            }
        }

        // ── Plumbing ───────────────────────────────────────────────────────────

        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, string? accessToken, object? body, CancellationToken ct, string? idempotencyKey = null)
        {
            using (var request = CreateRequest(method, path, accessToken))
            {
                if (body != null)
                {
                    request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body, Newtonsoft.Json.Formatting.None, CoreJson.Settings), Encoding.UTF8, "application/json");
                }

                if (idempotencyKey != null)
                {
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                }

                using (HttpResponseMessage response = await SendAsync(request, ct).ConfigureAwait(false))
                {
                    await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(JToken))
                    {
                        return default!;
                    }

                    return Deserialize<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? accessToken)
        {
            var request = new HttpRequestMessage(method, new Uri(BaseUrl, path));
            if (accessToken != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(RequestTimeout);
                try
                {
                    return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new CloudApiException(0, "timeout", "The Chameleon service did not answer in time.");
                }
                catch (HttpRequestException ex)
                {
                    throw new CloudApiException(0, CloudApiException.NetworkCode, "The Chameleon service could not be reached.", ex.Message, null, ex);
                }
                catch (IOException ex)
                {
                    throw new CloudApiException(0, CloudApiException.NetworkCode, "The Chameleon service could not be reached.", ex.Message, null, ex);
                }
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            {
                return;
            }

            int status = (int)response.StatusCode;
            string body = await ProviderResponseReader.ReadErrorBodyAsync(response, ct).ConfigureAwait(false);
            JObject? json = ProviderResponseReader.TryParseObject(body);
            string code = (string?)json?.SelectToken("error.code") ?? string.Empty;
            string message = (string?)json?.SelectToken("error.message") ?? ("The Chameleon service returned HTTP " + status + ".");
            string? detail = (string?)json?.SelectToken("error.detail");
            throw new CloudApiException(status, code, message, detail, ProviderResponseReader.GetRetryAfter(response));
        }

        private static T Deserialize<T>(string json)
        {
            try
            {
                T? value = CoreJson.Deserialize<T>(json);
                if (value != null)
                {
                    return value;
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Reported below.
            }

            throw new CloudApiException(0, "provider_error", "The Chameleon service sent an unreadable answer.");
        }

        private static void AddImage(MultipartFormDataContent form, string name, ImageData? image)
        {
            if (image == null)
            {
                return;
            }

            var content = new ByteArrayContent(image.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(image.MimeType);
            form.Add(content, name, name + "." + image.FileExtension);
        }
    }
}
