using System;
using System.Collections.Generic;
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
    public class OpenAIImagesAdapterTests
    {
        private readonly OpenAIImagesAdapter _adapter = new OpenAIImagesAdapter();

        private static GenerationRequest Request()
        {
            return new GenerationRequest { ModelId = "gpt-image-2", Prompt = "ignored", Source = TestImages.Png() };
        }

        private sealed class FormPart
        {
            public string Name = "";
            public string? FileName;
            public string? ContentType;
            public byte[] Body = Array.Empty<byte>();
            public string Text => System.Text.Encoding.UTF8.GetString(Body);
        }

        private static async Task<List<FormPart>> ReadFormAsync(HttpRequestMessage message)
        {
            var form = Assert.IsType<MultipartFormDataContent>(message.Content);
            var parts = new List<FormPart>();
            foreach (HttpContent content in form)
            {
                parts.Add(new FormPart
                {
                    Name = content.Headers.ContentDisposition!.Name!.Trim('"'),
                    FileName = content.Headers.ContentDisposition.FileName?.Trim('"'),
                    ContentType = content.Headers.ContentType?.MediaType,
                    Body = await content.ReadAsByteArrayAsync()
                });
            }

            return parts;
        }

        // ── Request ────────────────────────────────────────────────────────────

        [Fact]
        public async Task BuildGenerateRequest_WithSource_PostsMultipartToEdits()
        {
            GenerationRequest request = Request();
            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile()), request, "FINAL PROMPT");

            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https://openai.test/v1/images/edits", message.RequestUri!.ToString());
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal(AdapterTestKit.ApiKey, message.Headers.Authorization.Parameter);

            List<FormPart> parts = await ReadFormAsync(message);
            Assert.Equal(new[] { "image[]", "prompt", "model", "n" }, parts.Select(p => p.Name).ToArray());
            Assert.Equal("image.png", parts[0].FileName);
            Assert.Equal("image/png", parts[0].ContentType);
            Assert.Equal(request.Source!.Bytes, parts[0].Body);
            Assert.Equal("FINAL PROMPT", parts[1].Text);
            Assert.Equal("gpt-image-2", parts[2].Text);
            Assert.Equal("1", parts[3].Text);
        }

        [Fact]
        public async Task BuildGenerateRequest_AddsReferencesMaskAndSize()
        {
            GenerationRequest request = Request();
            request.AspectRatio = "3:2";
            request.Mask = TestImages.Png();
            request.References.Add(new ReferenceImage(TestImages.Webp(), ReferenceRole.Style));
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency));

            List<FormPart> parts = await ReadFormAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile()), request, "p"));

            Assert.Equal(
                new[] { "image[]", "image[]", "image[]", "mask", "prompt", "model", "n", "size" },
                parts.Select(p => p.Name).ToArray());
            // Consistency references go first, as for every adapter.
            Assert.Equal(new[] { "image.png", "reference1.jpg", "reference2.webp", "mask.png" }, parts.Take(4).Select(p => p.FileName).ToArray());
            Assert.Equal(new[] { "image/png", "image/jpeg", "image/webp", "image/png" }, parts.Take(4).Select(p => p.ContentType).ToArray());
            Assert.Equal("1536x1024", parts.Last().Text);
        }

        [Theory]
        [InlineData("1:1", "1024x1024")]
        [InlineData("2:3", "1024x1536")]
        [InlineData("16:9", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public async Task BuildGenerateRequest_MapsAspectRatioToSizeOrLeavesItToTheServer(string? aspectRatio, string? expectedSize)
        {
            GenerationRequest request = Request();
            request.AspectRatio = aspectRatio;

            List<FormPart> parts = await ReadFormAsync(_adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile()), request, "p"));

            Assert.Equal(expectedSize, parts.SingleOrDefault(p => p.Name == "size")?.Text);
        }

        [Fact]
        public async Task BuildGenerateRequest_WithoutSource_PostsJsonToGenerations()
        {
            GenerationRequest request = Request();
            request.Source = null;
            request.AspectRatio = "1:1";

            HttpRequestMessage message = _adapter.BuildGenerateRequest(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile()), request, "a house");

            Assert.Equal("https://openai.test/v1/images/generations", message.RequestUri!.ToString());
            JObject body = await AdapterTestKit.ReadJsonBodyAsync(message);
            Assert.True(JToken.DeepEquals(
                new JObject { ["model"] = "gpt-image-2", ["prompt"] = "a house", ["n"] = 1, ["size"] = "1024x1024" },
                body), body.ToString());
        }

        [Fact]
        public void BuildValidateRequest_ListsModels()
        {
            HttpRequestMessage message = _adapter.BuildValidateRequest(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile()), "gpt-image-2");

            Assert.Equal(HttpMethod.Get, message.Method);
            Assert.Equal("https://openai.test/v1/models", message.RequestUri!.ToString());
            Assert.Equal(AdapterTestKit.ApiKey, message.Headers.Authorization!.Parameter);
            Assert.Null(message.Content);
        }

        // ── Response ───────────────────────────────────────────────────────────

        private Task<GenerationResult> ParseAsync(HttpStatusCode status, string body, HttpClient? client = null)
        {
            return _adapter.ParseGenerateResponseAsync(
                AdapterTestKit.Context(AdapterTestKit.OpenAIProfile(), client),
                AdapterTestKit.JsonResponse(status, body),
                CancellationToken.None);
        }

        [Fact]
        public async Task Parse_ReadsBase64Images()
        {
            string body = new JObject
            {
                ["data"] = new JArray
                {
                    new JObject { ["b64_json"] = AdapterTestKit.Base64(TestImages.Png().Bytes) },
                    new JObject { ["b64_json"] = AdapterTestKit.Base64(TestImages.Jpeg().Bytes) }
                }
            }.ToString();

            GenerationResult result = await ParseAsync(HttpStatusCode.OK, body);

            Assert.Equal(new[] { "image/png", "image/jpeg" }, result.Images.Select(i => i.MimeType).ToArray());
        }

        [Fact]
        public async Task Parse_DownloadsUrlResultsWithoutSendingTheKey()
        {
            ImageData expected = TestImages.Webp();
            var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected.Bytes) });
            string body = "{\"data\":[{\"url\":\"https://cdn.test/result.webp?sig=abc\"}]}";

            GenerationResult result = await ParseAsync(HttpStatusCode.OK, body, new HttpClient(handler));

            Assert.Equal(expected.Bytes, Assert.Single(result.Images).Bytes);
            HttpRequestMessage download = Assert.Single(handler.Requests);
            Assert.Equal("https://cdn.test/result.webp?sig=abc", download.RequestUri!.ToString());
            Assert.Null(download.Headers.Authorization);
        }

        [Fact]
        public async Task Parse_FailedDownload_IsProviderError()
        {
            var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync(HttpStatusCode.OK, "{\"data\":[{\"url\":\"https://cdn.test/expired.png\"}]}", new HttpClient(handler)));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
        }

        [Theory]
        [InlineData("{\"data\":[]}")]
        [InlineData("{}")]
        [InlineData("{\"data\":[{\"b64_json\":\"%%%\"}]}")]
        [InlineData("{\"data\":[{\"url\":\"ftp://cdn.test/a.png\"}]}")]
        [InlineData("{\"data\":[{\"revised_prompt\":\"only text\"}]}")]
        public async Task Parse_ResponsesWithoutAUsableImage_AreNoImageReturned(string body)
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => ParseAsync(HttpStatusCode.OK, body));

            Assert.Equal(RenderErrorCode.NoImageReturned, ex.Code);
        }

        [Fact]
        public async Task Parse_NonJsonSuccessBody_IsProviderError()
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => ParseAsync(HttpStatusCode.OK, "<html/>"));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
        }

        [Theory]
        [InlineData(401, "invalid_api_key", RenderErrorCode.AuthFailed)]
        [InlineData(429, "rate_limit_exceeded", RenderErrorCode.RateLimited)]
        [InlineData(429, "insufficient_quota", RenderErrorCode.RateLimited)]
        [InlineData(403, "insufficient_user_quota", RenderErrorCode.InsufficientCredits)]
        [InlineData(403, "pre_consume_token_quota_failed", RenderErrorCode.InsufficientCredits)]
        [InlineData(403, "some_other_code", RenderErrorCode.AuthFailed)]
        [InlineData(400, "content_policy_violation", RenderErrorCode.ContentBlocked)]
        [InlineData(400, "moderation_blocked", RenderErrorCode.ContentBlocked)]
        [InlineData(404, "model_not_found", RenderErrorCode.ModelUnavailable)]
        [InlineData(400, "some_other_code", RenderErrorCode.InvalidRequest)]
        [InlineData(500, null, RenderErrorCode.ProviderError)]
        public async Task Parse_ErrorBodies_MapToCodes(int status, string? errorCode, RenderErrorCode expected)
        {
            var error = new JObject { ["message"] = "something went wrong", ["type"] = "invalid_request_error" };
            if (errorCode != null)
            {
                error["code"] = errorCode;
            }

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync((HttpStatusCode)status, new JObject { ["error"] = error }.ToString()));

            Assert.Equal(expected, ex.Code);
            Assert.Equal(status, ex.HttpStatus);
            Assert.Equal("something went wrong", ex.ProviderMessage);
        }

        [Fact]
        public async Task Parse_InsufficientQuota_SaysSoInTheMessage()
        {
            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => ParseAsync((HttpStatusCode)429, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"You exceeded your current quota\"}}"));

            Assert.Contains("quota", ex.Message);
        }
    }
}
