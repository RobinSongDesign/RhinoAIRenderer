using System;
using System.Collections.Generic;
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
    /// Gemini generateContent wire format. Used by Google's own endpoint and by relays that
    /// expose the same API with bearer authentication.
    /// </summary>
    public sealed class GeminiAdapter : IProviderAdapter
    {
        private const string GoogApiKeyHeader = "x-goog-api-key";

        // Finish reasons that mean the provider refused to produce the image.
        private static readonly HashSet<string> BlockingFinishReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SAFETY", "PROHIBITED_CONTENT", "IMAGE_SAFETY", "IMAGE_PROHIBITED_CONTENT", "BLOCKLIST", "SPII"
        };

        /// <inheritdoc />
        public string ApiFormat => ApiFormats.Gemini;

        /// <inheritdoc />
        public HttpRequestMessage BuildGenerateRequest(AdapterContext context, GenerationRequest request, string promptText)
        {
            var images = new StreamedImageJsonContent.Builder();
            var parts = new JArray { new JObject { ["text"] = promptText } };

            if (request.Source != null)
            {
                parts.Add(ImagePart(images, request.Source));
            }

            foreach (ReferenceImage reference in request.GetOrderedReferences())
            {
                parts.Add(ImagePart(images, reference.Image));
            }

            var imageConfig = new JObject();
            if (!string.IsNullOrEmpty(request.AspectRatio))
            {
                imageConfig["aspectRatio"] = request.AspectRatio;
            }

            imageConfig["imageSize"] = request.ImageSize;

            var payload = new JObject
            {
                ["contents"] = new JArray { new JObject { ["parts"] = parts } }
            };

            if (context.Profile.EnableSearchGrounding)
            {
                payload["tools"] = new JArray { new JObject { ["google_search"] = new JObject() } };
            }

            payload["generationConfig"] = new JObject
            {
                ["responseModalities"] = new JArray { "TEXT", "IMAGE" },
                ["imageConfig"] = imageConfig
            };

            HttpRequestMessage message = CreateRequest(context, request.ModelId);
            message.Content = StreamedImageJsonContent.Create(images, payload);
            return message;
        }

        /// <inheritdoc />
        public HttpRequestMessage BuildValidateRequest(AdapterContext context, string modelId)
        {
            // Text-only probe: proves the key is accepted without paying for an image.
            var payload = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject { ["parts"] = new JArray { new JObject { ["text"] = "Hi" } } }
                },
                ["generationConfig"] = new JObject { ["responseModalities"] = new JArray { "TEXT" } }
            };

            HttpRequestMessage message = CreateRequest(context, modelId);
            message.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            return message;
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

            string? blockReason = (string?)json.SelectToken("promptFeedback.blockReason");
            if (!string.IsNullOrEmpty(blockReason))
            {
                throw new RenderException(
                    RenderErrorCode.ContentBlocked,
                    "The prompt was blocked by the provider (" + blockReason + ").",
                    status,
                    context.Redact((string?)json.SelectToken("promptFeedback.blockReasonMessage") ?? blockReason));
            }

            var images = new List<ImageData>();
            var text = new StringBuilder();
            JToken? candidate = (json["candidates"] as JArray)?.First;

            if (candidate?.SelectToken("content.parts") is JArray parts)
            {
                foreach (JToken part in parts)
                {
                    // Thought parts are intermediate reasoning, not the answer.
                    if ((bool?)part["thought"] == true)
                    {
                        continue;
                    }

                    string? data = (string?)(part.SelectToken("inlineData.data") ?? part.SelectToken("inline_data.data"));
                    if (!string.IsNullOrEmpty(data))
                    {
                        ImageData? image = DecodeImage(data!);
                        if (image != null)
                        {
                            images.Add(image);
                        }

                        continue;
                    }

                    string? partText = (string?)part["text"];
                    if (!string.IsNullOrEmpty(partText))
                    {
                        text.Append(partText);
                    }
                }
            }

            if (images.Count == 0)
            {
                string? finishReason = (string?)candidate?["finishReason"];
                string? detail = context.Redact(text.Length > 0 ? text.ToString() : finishReason);

                if (finishReason != null && BlockingFinishReasons.Contains(finishReason))
                {
                    throw new RenderException(
                        RenderErrorCode.ContentBlocked,
                        "The provider refused to generate the image (" + finishReason + ").",
                        status,
                        detail);
                }

                throw new RenderException(
                    RenderErrorCode.NoImageReturned,
                    "The provider response contains no image.",
                    status,
                    detail);
            }

            return new GenerationResult(
                images,
                text.Length > 0 ? text.ToString() : null,
                null,
                ProviderResponseReader.GetRequestId(response));
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

            string? errorStatus = (string?)json?.SelectToken("error.status");
            string? errorMessage = (string?)json?.SelectToken("error.message");

            RenderErrorCode code = ClassifyError(status, errorStatus, body);
            var exception = new RenderException(
                code,
                "The provider returned HTTP " + status + (errorStatus != null ? " (" + errorStatus + ")." : "."),
                status,
                context.Redact(string.IsNullOrEmpty(errorMessage) ? body : errorMessage));
            exception.RetryAfter = ProviderResponseReader.GetRetryAfter(response);
            return exception;
        }

        private static RenderErrorCode ClassifyError(int status, string? errorStatus, string body)
        {
            // Google answers a bad key with 400 INVALID_ARGUMENT, so the reason must win over the status.
            if (body.IndexOf("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RenderErrorCode.AuthFailed;
            }

            switch (errorStatus)
            {
                case "PERMISSION_DENIED":
                case "UNAUTHENTICATED":
                    return RenderErrorCode.AuthFailed;
                case "RESOURCE_EXHAUSTED":
                    return RenderErrorCode.RateLimited;
                case "NOT_FOUND":
                    return RenderErrorCode.ModelUnavailable;
                case "DEADLINE_EXCEEDED":
                    return RenderErrorCode.Timeout;
                case "UNAVAILABLE":
                case "INTERNAL":
                    return RenderErrorCode.ProviderError;
                default:
                    return RenderException.CodeFromHttpStatus(status);
            }
        }

        private static HttpRequestMessage CreateRequest(AdapterContext context, string modelId)
        {
            string url = context.Profile.BaseUrl.TrimEnd('/') + "/v1beta/models/" +
                         Uri.EscapeDataString(modelId ?? string.Empty) + ":generateContent";

            var message = new HttpRequestMessage(HttpMethod.Post, url);
            if (string.Equals(context.Profile.AuthType, AuthTypes.Goog, StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.TryAddWithoutValidation(GoogApiKeyHeader, context.ApiKey);
            }
            else
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);
            }

            message.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            message.Headers.TryAddWithoutValidation(ProviderResponseReader.RequestIdHeader, Guid.NewGuid().ToString());
            return message;
        }

        private static JObject ImagePart(StreamedImageJsonContent.Builder images, ImageData image)
        {
            return new JObject
            {
                ["inline_data"] = new JObject
                {
                    // Always the real format of the bytes; the WPF version mislabeled JPEG as PNG.
                    ["mime_type"] = image.MimeType,
                    ["data"] = images.Add(image)
                }
            };
        }

        private static ImageData? DecodeImage(string base64)
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
    }
}
