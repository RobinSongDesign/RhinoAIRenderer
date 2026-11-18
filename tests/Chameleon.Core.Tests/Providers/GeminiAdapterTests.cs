using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Providers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.Core.Tests.Providers
{
    public class GeminiAdapterTests
    {
        private readonly GeminiAdapter _adapter = new GeminiAdapter();

        private static GenerationRequest Request()
        {
            return new GenerationRequest
            {
                ModelId = "gemini-3-pro-image-preview",
                Prompt = "ignored: the adapter sends the composed text",
                Source = TestImages.Png(),
                ImageSize = "2K",
                AspectRatio = "16:9"
            };
        }

        private static JObject InlineData(ImageData image)
        {
            return new JObject
            {
                ["inline_data"] = new JObject
                {
                    ["mime_type"] = image.MimeType,
                    ["data"] = AdapterTestKit.Base64(image.Bytes)
                }
            };
        }

        // ── Request ────────────────────────────────────────────────────────────

        [Fact]
        public async Task BuildGenerateRequest_ProducesTheLegacyWireShape()
        {
            GenerationRequest request = Request();
            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "FINAL PROMPT");

            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal(
                "https://gemini.test/v1beta/models/gemini-3-pro-image-preview:generateContent",
                message.RequestUri!.ToString());

            var expected = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["parts"] = new JArray { new JObject { ["text"] = "FINAL PROMPT" }, InlineData(request.Source!) }
                    }
                },
                ["tools"] = new JArray { new JObject { ["google_search"] = new JObject() } },
                ["generationConfig"] = new JObject
                {
                    ["responseModalities"] = new JArray { "TEXT", "IMAGE" },
                    ["imageConfig"] = new JObject { ["aspectRatio"] = "16:9", ["imageSize"] = "2K" }
                }
            };

            JObject actual = await AdapterTestKit.ReadJsonBodyAsync(message);
            Assert.True(JToken.DeepEquals(expected, actual), actual.ToString());
        }

        [Fact]
        public async Task BuildGenerateRequest_DeclaresTheRealMimeTypeOfEachImage()
        {
            // Regression for legacy defect L1: JPEG bytes were labeled image/png.
            GenerationRequest request = Request();
            request.Source = TestImages.Jpeg();
            request.References.Add(new ReferenceImage(TestImages.Webp(), ReferenceRole.Style));

            JObject body = await AdapterTestKit.ReadJsonBodyAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "p"));

            string[] mimeTypes = body.SelectTokens("contents[0].parts[*].inline_data.mime_type").Select(t => (string)t!).ToArray();
            Assert.Equal(new[] { "image/jpeg", "image/webp" }, mimeTypes);
        }

        [Fact]
        public async Task BuildGenerateRequest_SendsSourceThenConsistencyThenOtherReferences()
        {
            GenerationRequest request = Request();
            ImageData style = TestImages.Webp();
            ImageData previous = TestImages.Jpeg();
            request.References.Add(new ReferenceImage(style, ReferenceRole.Style));
            request.References.Add(new ReferenceImage(previous, ReferenceRole.Consistency));

            JObject body = await AdapterTestKit.ReadJsonBodyAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "p"));

            string[] data = body.SelectTokens("contents[0].parts[*].inline_data.data").Select(t => (string)t!).ToArray();
            Assert.Equal(
                new[]
                {
                    AdapterTestKit.Base64(request.Source!.Bytes),
                    AdapterTestKit.Base64(previous.Bytes),
                    AdapterTestKit.Base64(style.Bytes)
                },
                data);
        }

        [Fact]
        public async Task BuildGenerateRequest_OmitsAspectRatioWhenKeepingSourceRatio()
        {
            GenerationRequest request = Request();
            request.AspectRatio = "";

            JObject body = await AdapterTestKit.ReadJsonBodyAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "p"));

            var imageConfig = (JObject)body.SelectToken("generationConfig.imageConfig")!;
            Assert.Null(imageConfig["aspectRatio"]);
            Assert.Equal("2K", (string)imageConfig["imageSize"]!);
        }

        [Fact]
        public async Task BuildGenerateRequest_OmitsSearchToolWhenDisabled()
        {
            JObject body = await AdapterTestKit.ReadJsonBodyAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile(searchGrounding: false)), Request(), "p"));

            Assert.Null(body["tools"]);
        }

        [Fact]
        public void BuildGenerateRequest_GoogAuthUsesApiKeyHeader()
        {
            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile(AuthTypes.Goog)), Request(), "p");

            Assert.Equal(AdapterTestKit.ApiKey, message.Headers.GetValues("x-goog-api-key").Single());
            Assert.Null(message.Headers.Authorization);
            Assert.True(Guid.TryParse(message.Headers.GetValues("X-Request-ID").Single(), out _));
            Assert.True(message.Headers.CacheControl!.NoCache);
            Assert.True(message.Headers.CacheControl.NoStore);
        }

        [Fact]
        public void BuildGenerateRequest_BearerAuthUsesAuthorizationHeader()
        {
            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile(AuthTypes.Bearer)), Request(), "p");

            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal(AdapterTestKit.ApiKey, message.Headers.Authorization.Parameter);
            Assert.False(message.Headers.Contains("x-goog-api-key"));
        }

        [Fact]
        public async Task BuildGenerateRequest_ContentLengthMatchesTheStreamedBody()
        {
            GenerationRequest request = Request();
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency));
            // Non-ASCII and JSON-special characters must not break the placeholder splitting.
            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "提示词 \"quoted\" \\ @@chameleon-image:0@@");

            byte[] bytes = await message.Content!.ReadAsByteArrayAsync();

            Assert.Equal(bytes.Length, message.Content.Headers.ContentLength);
            Assert.Equal("application/json", message.Content.Headers.ContentType!.MediaType);
            JObject body = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            Assert.Equal("提示词 \"quoted\" \\ @@chameleon-image:0@@", (string)body.SelectToken("contents[0].parts[0].text")!);
        }

        [Fact]
        public async Task BuildGenerateRequest_WithoutSource_SendsTextOnly()
        {
            GenerationRequest request = Request();
            request.Source = null;

            JObject body = await AdapterTestKit.ReadJsonBodyAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), request, "p"));

            Assert.Single((JArray)body.SelectToken("contents[0].parts")!);
        }

        [Fact]
        public async Task BuildValidateRequest_IsATextOnlyProbe()
        {
            HttpRequestMessage message = _adapter.BuildValidateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), "gemini-3-pro-image-preview");

            JObject body = await AdapterTestKit.ReadJsonBodyAsync(message);

            Assert.EndsWith("/v1beta/models/gemini-3-pro-image-preview:generateContent", message.RequestUri!.ToString());
            Assert.Equal("Hi", (string)body.SelectToken("contents[0].parts[0].text")!);
            Assert.Equal(new[] { "TEXT" }, body.SelectToken("generationConfig.responseModalities")!.Select(t => (string)t!).ToArray());
        }

        // ── Response ───────────────────────────────────────────────────────────

        private static string SuccessBody(params JObject[] parts)
        {
            return new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject { ["role"] = "model", ["parts"] = new JArray(parts.Cast<object>().ToArray()) },
                        ["finishReason"] = "STOP"
                    }
                }
            }.ToString();
        }

        private static JObject ResponseImage(ImageData image, bool snakeCase = false)
        {
            var data = new JObject { [snakeCase ? "mime_type" : "mimeType"] = image.MimeType, ["data"] = AdapterTestKit.Base64(image.Bytes) };
            return new JObject { [snakeCase ? "inline_data" : "inlineData"] = data };
        }

        private Task<GenerationResult> ParseAsync(HttpStatusCode status, string body, HttpRequestMessage? request = null)
        {
            return _adapter.ParseGenerateResponseAsync(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()),
                AdapterTestKit.JsonResponse(status, body, request),
                CancellationToken.None);
        }

        [Fact]
        public async Task Parse_ReturnsImageAndText()
        {
            HttpRequestMessage sent = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), Request(), "p");
            string body = SuccessBody(new JObject { ["text"] = "Here you go. " }, ResponseImage(TestImages.Jpeg()), new JObject { ["text"] = "Done." });

            GenerationResult result = await ParseAsync(HttpStatusCode.OK, body, sent);

            ImageData image = Assert.Single(result.Images);
            Assert.Equal("image/jpeg", image.MimeType);
            Assert.Equal(80, image.Width);
            Assert.Equal("Here you go. Done.", result.Text);
            Assert.Null(result.Usage);
            Assert.Equal(sent.Headers.GetValues("X-Request-ID").Single(), result.RequestId);
        }

        [Fact]
        public async Task Parse_CollectsEveryImageAndAcceptsSnakeCase()
        {
            string body = SuccessBody(ResponseImage(TestImages.Png()), ResponseImage(TestImages.Webp(), snakeCase: true));

            GenerationResult result = await ParseAsync(HttpStatusCode.OK, body);

            Assert.Equal(new[] { "image/png", "image/webp" }, result.Images.Select(i => i.MimeType).ToArray());
            Assert.Null(result.Text);
        }

        [Fact]
        public async Task Parse_SkipsThoughtParts()
        {
            JObject thoughtImage = ResponseImage(TestImages.Png());
            thoughtImage["thought"] = true;
            string body = SuccessBody(
                new JObject { ["text"] = "thinking...", ["thought"] = true },
                thoughtImage,
                ResponseImage(TestImages.Jpeg()));

            GenerationResult result = await ParseAsync(HttpStatusCode.OK, body);

            Assert.Equal("image/jpeg", Assert.Single(result.Images).MimeType);
            Assert.Null(result.Text);
        }

        [Fact]
        public async Task Parse_TextOnlyResponse_IsNoImageReturnedWithTheModelText()
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync(HttpStatusCode.OK, TestFixtures.ReadText("gemini/text_only.json")));

            Assert.Equal(RenderErrorCode.NoImageReturned, ex.Code);
            Assert.Contains("cannot edit this image", ex.ProviderMessage);
        }

        [Theory]
        [InlineData("gemini/blocked_prompt.json")]
        [InlineData("gemini/blocked_finish.json")]
        public async Task Parse_SafetyBlocks_AreContentBlocked(string fixture)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync(HttpStatusCode.OK, TestFixtures.ReadText(fixture)));

            Assert.Equal(RenderErrorCode.ContentBlocked, ex.Code);
            Assert.False(ex.IsRetryable);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"candidates\":[]}")]
        [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"data\":\"!!!not-base64!!!\"}}]}}]}")]
        [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"data\":\"aGVsbG8gd29ybGQ=\"}}]}}]}")]
        public async Task Parse_ResponsesWithoutAUsableImage_AreNoImageReturned(string body)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => ParseAsync(HttpStatusCode.OK, body));

            Assert.Equal(RenderErrorCode.NoImageReturned, ex.Code);
        }

        [Theory]
        [InlineData("<html><body>Bad gateway</body></html>")]
        [InlineData("")]
        [InlineData("[1,2,3]")]
        public async Task Parse_NonJsonSuccessBody_IsProviderError(string body)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => ParseAsync(HttpStatusCode.OK, body));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "gemini/error_invalid_key.json", RenderErrorCode.AuthFailed)]
        [InlineData((HttpStatusCode)429, "gemini/error_quota.json", RenderErrorCode.RateLimited)]
        [InlineData(HttpStatusCode.NotFound, "gemini/error_model_not_found.json", RenderErrorCode.ModelUnavailable)]
        [InlineData(HttpStatusCode.Unauthorized, "gemini/error_relay_unauthorized.json", RenderErrorCode.AuthFailed)]
        [InlineData(HttpStatusCode.Forbidden, "gemini/error_relay_token_quota.json", RenderErrorCode.InsufficientCredits)]
        [InlineData(HttpStatusCode.Forbidden, "gemini/error_relay_user_quota.json", RenderErrorCode.InsufficientCredits)]
        public async Task Parse_ErrorBodies_MapToCodes(HttpStatusCode status, string fixture, RenderErrorCode expected)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync(status, TestFixtures.ReadText(fixture)));

            Assert.Equal(expected, ex.Code);
            Assert.Equal((int)status, ex.HttpStatus);
            Assert.False(string.IsNullOrEmpty(ex.ProviderMessage));
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, RenderErrorCode.ProviderError)]
        [InlineData(HttpStatusCode.BadGateway, RenderErrorCode.ProviderError)]
        [InlineData(HttpStatusCode.Forbidden, RenderErrorCode.AuthFailed)]
        [InlineData(HttpStatusCode.BadRequest, RenderErrorCode.InvalidRequest)]
        public async Task Parse_NonJsonErrorBody_FallsBackToStatusMapping(HttpStatusCode status, RenderErrorCode expected)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => ParseAsync(status, "upstream exploded"));

            Assert.Equal(expected, ex.Code);
            Assert.Equal("upstream exploded", ex.ProviderMessage);
        }

        [Fact]
        public async Task ParseError_CarriesRetryAfterAndRedactsTheKey()
        {
            HttpResponseMessage response = AdapterTestKit.JsonResponse(
                (HttpStatusCode)429,
                "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"quota for key " + AdapterTestKit.ApiKey + " exceeded\"}}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

            RenderException ex = await _adapter.ParseErrorAsync(
                AdapterTestKit.Context(AdapterTestKit.GeminiProfile()), response, CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
            Assert.DoesNotContain(AdapterTestKit.ApiKey, ex.ProviderMessage);
            Assert.Contains("***", ex.ProviderMessage);
        }
    }
}
