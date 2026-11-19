using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Providers.Http;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Providers
{
    /// <summary>
    /// OpenAI Images wire format: multipart "/v1/images/edits" when a source image is present,
    /// JSON "/v1/images/generations" otherwise.
    /// </summary>
    public sealed class OpenAIImagesAdapter : IProviderAdapter
    {
        // The API takes pixel sizes, not ratios. Ratios without an entry are left to the server ("auto").
        private static readonly Dictionary<string, string> SizeByAspectRatio = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "1:1", "1024x1024" },
            { "3:2", "1536x1024" },
            { "2:3", "1024x1536" }
        };

        /// <inheritdoc />
        public string ApiFormat => ApiFormats.OpenAI;

        /// <inheritdoc />
        public HttpRequestMessage BuildGenerateRequest(AdapterContext context, GenerationRequest request, string promptText)
        {
            string? size = MapSize(request.AspectRatio);
            HttpRequestMessage message;

            if (request.Source == null)
            {
                var payload = new JObject { ["model"] = request.ModelId, ["prompt"] = promptText, ["n"] = 1 };
                if (size != null)
                {
                    payload["size"] = size;
                }

                message = CreateRequest(context, HttpMethod.Post, "/v1/images/generations");
                message.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                return message;
            }

            var form = new MultipartFormDataContent();
            form.Add(ImageContent(request.Source), "image[]", "image." + request.Source.FileExtension);

            int index = 0;
            foreach (ReferenceImage reference in request.GetOrderedReferences())
            {
                index++;
                form.Add(ImageContent(reference.Image), "image[]", "reference" + index + "." + reference.Image.FileExtension);
            }

            if (request.Mask != null)
            {
                form.Add(ImageContent(request.Mask), "mask", "mask." + request.Mask.FileExtension);
            }

            form.Add(new StringContent(promptText, Encoding.UTF8), "prompt");
            form.Add(new StringContent(request.ModelId, Encoding.UTF8), "model");
            form.Add(new StringContent("1", Encoding.UTF8), "n");
            if (size != null)
            {
                form.Add(new StringContent(size, Encoding.UTF8), "size");
            }

            message = CreateRequest(context, HttpMethod.Post, "/v1/images/edits");
            message.Content = form;
            return message;
        }

        /// <inheritdoc />
        public HttpRequestMessage BuildValidateRequest(AdapterContext context, string modelId)
        {
            // Listing models is free and fails with 401 when the key is rejected.
            return CreateRequest(context, HttpMethod.Get, "/v1/models");
        }

        /// <inheritdoc />
        public HttpRequestMessage BuildListModelsRequest(AdapterContext context)
        {
            return CreateRequest(context, HttpMethod.Get, "/v1/models");
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ParseListModelsResponseAsync(
            AdapterContext context,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ParseErrorAsync(context, response, cancellationToken).ConfigureAwait(false);
            }

            JObject? json = await ProviderResponseReader.ReadJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
            if (!(json?["data"] is JArray models))
            {
                throw new RenderException(RenderErrorCode.ProviderError, "The model list has no \"data\" array.", (int)response.StatusCode, null);
            }

            // The Images API is served by the "gpt-image" and "dall-e" families; relays list every
            // chat model too, so anything else is left out.
            var ids = new List<string>();
            foreach (JObject model in models.OfType<JObject>())
            {
                string id = ((string?)model["id"] ?? string.Empty).Trim();
                string lower = id.ToLowerInvariant();
                if ((lower.Contains("image") || lower.StartsWith("dall-e", StringComparison.Ordinal)) && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <inheritdoc />
        public async Task<GenerationResult> ParseGenerateResponseAsync(
            AdapterContext context,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ParseErrorAsync(context, response, cancellationToken).ConfigureAwait(false);
            }

            int status = (int)response.StatusCode;
            JObject? json = await ProviderResponseReader.ReadJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
            if (json == null)
            {
                throw new RenderException(RenderErrorCode.ProviderError, "The provider response is not valid JSON.", status);
            }

            var images = new List<ImageData>();
            if (json["data"] is JArray data)
            {
                foreach (JToken item in data)
                {
                    ImageData? image = await ReadImageAsync(context, item, cancellationToken).ConfigureAwait(false);
                    if (image != null)
                    {
                        images.Add(image);
                    }
                }
            }

            if (images.Count == 0)
            {
                throw new RenderException(
                    RenderErrorCode.NoImageReturned,
                    "The provider response contains no image.",
                    status);
            }

            return new GenerationResult(images, null, null, ProviderResponseReader.GetRequestId(response));
        }

        /// <inheritdoc />
        public async Task<RenderException> ParseErrorAsync(
            AdapterContext context,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            int status = (int)response.StatusCode;
            string body = await ProviderResponseReader.ReadErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
            JObject? json = ProviderResponseReader.TryParseObject(body);

            string? errorCode = (string?)json?.SelectToken("error.code");
            string? errorMessage = (string?)json?.SelectToken("error.message");

            RenderErrorCode code;
            string summary = "The provider returned HTTP " + status + (errorCode != null ? " (" + errorCode + ")." : ".");
            switch (errorCode)
            {
                case "invalid_api_key":
                    code = RenderErrorCode.AuthFailed;
                    break;
                case "rate_limit_exceeded":
                    code = RenderErrorCode.RateLimited;
                    break;
                case "insufficient_quota":
                    // The provider account is out of quota: waiting may help, a different request will not.
                    code = RenderErrorCode.RateLimited;
                    summary = "The provider account has run out of quota.";
                    break;
                case RelayErrorCodes.InsufficientUserQuota:
                case RelayErrorCodes.TokenQuotaExhausted:
                    // A relay account (One API / new-api) is out of balance: the user has to top up.
                    code = RenderErrorCode.InsufficientCredits;
                    summary = "The account at the relay has run out of balance.";
                    break;
                case "content_policy_violation":
                case "moderation_blocked":
                    code = RenderErrorCode.ContentBlocked;
                    break;
                case "model_not_found":
                    code = RenderErrorCode.ModelUnavailable;
                    break;
                default:
                    code = RenderException.CodeFromHttpStatus(status);
                    break;
            }

            var exception = new RenderException(
                code,
                summary,
                status,
                context.Redact(string.IsNullOrEmpty(errorMessage) ? body : errorMessage));
            exception.RetryAfter = ProviderResponseReader.GetRetryAfter(response);
            return exception;
        }

        private static async Task<ImageData?> ReadImageAsync(AdapterContext context, JToken item, CancellationToken cancellationToken)
        {
            string? base64 = (string?)item["b64_json"];
            if (!string.IsNullOrEmpty(base64))
            {
                try
                {
                    return ImageData.TryFromBytes(Convert.FromBase64String(base64));
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            string? url = (string?)item["url"];
            if (string.IsNullOrEmpty(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            // Result URLs are pre-signed: no credential is attached to this download.
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (HttpResponseMessage download = await context.HttpClient
                       .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!download.IsSuccessStatusCode)
                {
                    throw new RenderException(
                        RenderErrorCode.ProviderError,
                        "Downloading the generated image failed with HTTP " + (int)download.StatusCode + ".",
                        (int)download.StatusCode);
                }

                return ImageData.TryFromBytes(await download.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            }
        }

        private static string? MapSize(string? aspectRatio)
        {
            return !string.IsNullOrEmpty(aspectRatio) && SizeByAspectRatio.TryGetValue(aspectRatio!, out string? size)
                ? size
                : null;
        }

        private static ByteArrayContent ImageContent(ImageData image)
        {
            var content = new ByteArrayContent(image.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(image.MimeType);
            return content;
        }

        private static HttpRequestMessage CreateRequest(AdapterContext context, HttpMethod method, string path)
        {
            var message = new HttpRequestMessage(method, context.Profile.BaseUrl.TrimEnd('/') + path);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);
            message.Headers.TryAddWithoutValidation(ProviderResponseReader.RequestIdHeader, Guid.NewGuid().ToString());
            return message;
        }
    }
}
