using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Backends;
using Chameleon.Core.Generation;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.Core.Tests.Backends
{
    public class DirectBackendTests
    {
        private readonly List<TimeSpan> _waits = new List<TimeSpan>();

        private static string SuccessBody()
        {
            return new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["parts"] = new JArray
                            {
                                new JObject
                                {
                                    ["inlineData"] = new JObject
                                    {
                                        ["mimeType"] = "image/png",
                                        ["data"] = AdapterTestKit.Base64(TestImages.Png().Bytes)
                                    }
                                }
                            }
                        }
                    }
                }
            }.ToString();
        }

        private static HttpResponseMessage Ok() => AdapterTestKit.JsonResponse(HttpStatusCode.OK, SuccessBody());

        private static HttpResponseMessage Error(int status, string body = "{\"error\":{\"message\":\"nope\"}}")
        {
            return AdapterTestKit.JsonResponse((HttpStatusCode)status, body);
        }

        private static GenerationRequest Request()
        {
            return new GenerationRequest
            {
                ModelId = "gemini-3-pro-image-preview",
                Prompt = "make it real",
                SystemPrompt = "SYS",
                Source = TestImages.Png(),
                ImageSize = "1K"
            };
        }

        private DirectBackend Backend(
            StubHttpHandler handler,
            string? apiKey = AdapterTestKit.ApiKey,
            TimeSpan? timeout = null,
            ProviderProfile? profile = null)
        {
            profile = profile ?? AdapterTestKit.GeminiProfile();
            var secrets = new InMemorySecretStore();
            if (apiKey != null)
            {
                secrets.Set(SecretNames.ForProvider(profile.Id), apiKey);
            }

            var retry = new RetryPolicy(
                delayAsync: (delay, ct) =>
                {
                    _waits.Add(delay);
                    return Task.CompletedTask;
                },
                nextRandom: () => 0.5); // 0.5 means no jitter

            return new DirectBackend(
                profile,
                AdapterRegistry.CreateDefault().Get(profile.ApiFormat),
                secrets,
                DirectBackendFactory.CreateSharedHttpClient(handler),
                new PromptComposer(),
                retry,
                timeout);
        }

        [Fact]
        public async Task GenerateAsync_SendsComposedPromptAndReturnsImages()
        {
            var handler = new StubHttpHandler(_ => Ok());
            DirectBackend backend = Backend(handler);

            GenerationResult result = await backend.GenerateAsync(Request(), null, CancellationToken.None);

            Assert.Single(result.Images);
            HttpRequestMessage sent = Assert.Single(handler.Requests);
            Assert.Equal(AdapterTestKit.ApiKey, sent.Headers.GetValues("x-goog-api-key").Single());
            Assert.Equal("direct:TestGemini", backend.Id);
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task GenerateAsync_WithoutKey_FailsBeforeAnyHttpCall()
        {
            var handler = new StubHttpHandler(_ => Ok());

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler, apiKey: null).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task GenerateAsync_InvalidRequest_FailsBeforeAnyHttpCall()
        {
            var handler = new StubHttpHandler(_ => Ok());
            GenerationRequest request = Request();
            request.Prompt = "";

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler).GenerateAsync(request, null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.InvalidRequest, ex.Code);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task GenerateAsync_DoesNotRetryAuthFailures()
        {
            var handler = new StubHttpHandler(_ => Error(401));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task GenerateAsync_RetriesRateLimitThenSucceeds()
        {
            int calls = 0;
            var handler = new StubHttpHandler(_ => Interlocked.Increment(ref calls) == 1 ? Error(429) : Ok());
            var stages = new List<GenerationStage>();

            GenerationResult result = await Backend(handler).GenerateAsync(
                Request(), new SyncProgress(p => stages.Add(p.Stage)), CancellationToken.None);

            Assert.Single(result.Images);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, _waits);
            Assert.Equal(new[] { GenerationStage.Uploading, GenerationStage.Retrying }, stages);
            // A request message can only be sent once.
            Assert.NotSame(handler.Requests[0], handler.Requests[1]);
            Assert.NotEqual(
                handler.Requests[0].Headers.GetValues("X-Request-ID").Single(),
                handler.Requests[1].Headers.GetValues("X-Request-ID").Single());
        }

        [Fact]
        public async Task GenerateAsync_HonorsRetryAfterUpToTheCap()
        {
            int calls = 0;
            var handler = new StubHttpHandler(_ =>
            {
                int call = Interlocked.Increment(ref calls);
                if (call > 2)
                {
                    return Ok();
                }

                HttpResponseMessage response = Error(429);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(call == 1 ? 7 : 600));
                return response;
            });

            await Backend(handler).GenerateAsync(Request(), null, CancellationToken.None);

            Assert.Equal(new[] { TimeSpan.FromSeconds(7), RetryPolicy.MaxRetryAfter }, _waits);
        }

        [Fact]
        public async Task GenerateAsync_GivesUpAfterTwoRetries()
        {
            var handler = new StubHttpHandler(_ => Error(500));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6) }, _waits);
        }

        [Fact]
        public async Task GenerateAsync_ConnectionFailure_IsNetworkAndRetried()
        {
            int calls = 0;
            var handler = new StubHttpHandler((request, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new HttpRequestException("connection refused while using " + AdapterTestKit.ApiKey);
                }

                return Task.FromResult(Ok());
            });

            await Backend(handler).GenerateAsync(Request(), null, CancellationToken.None);
            Assert.Equal(2, handler.Requests.Count);

            // And when it never recovers, the failure is classified and the key is not leaked.
            var dead = new StubHttpHandler((request, ct) => throw new IOException("reset " + AdapterTestKit.ApiKey));
            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(dead).GenerateAsync(Request(), null, CancellationToken.None));
            Assert.Equal(RenderErrorCode.Network, ex.Code);
            Assert.DoesNotContain(AdapterTestKit.ApiKey, ex.ProviderMessage);
        }

        [Fact]
        public async Task GenerateAsync_SlowProvider_IsTimeout()
        {
            var handler = new StubHttpHandler(async (request, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Ok();
            });

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler, timeout: TimeSpan.FromMilliseconds(40)).GenerateAsync(Request(), null, CancellationToken.None));

            Assert.Equal(RenderErrorCode.Timeout, ex.Code);
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task GenerateAsync_CallerCancellation_InterruptsTheInFlightRequestWithoutRetry()
        {
            // Regression for legacy defect L6: cancel only took effect between requests.
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new StubHttpHandler(async (request, ct) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return Ok();
            });
            using var cts = new CancellationTokenSource();

            Task<GenerationResult> pending = Backend(handler).GenerateAsync(Request(), null, cts.Token);
            await started.Task;
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Single(handler.Requests);
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task GenerateAsync_ConcurrentRequests_DoNotShareHeaders()
        {
            // Regression for legacy defect L3: requests mutated HttpClient.DefaultRequestHeaders.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new StubHttpHandler(async (request, ct) =>
            {
                await gate.Task;
                return Ok();
            });
            DirectBackend backend = Backend(handler);

            Task<GenerationResult>[] tasks = Enumerable.Range(0, 8)
                .Select(_ => backend.GenerateAsync(Request(), null, CancellationToken.None))
                .ToArray();
            while (handler.Requests.Count < 8)
            {
                await Task.Delay(5);
            }

            gate.SetResult(true);
            await Task.WhenAll(tasks);

            Assert.Equal(8, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal(AdapterTestKit.ApiKey, r.Headers.GetValues("x-goog-api-key").Single()));
            Assert.Equal(8, handler.Requests.Select(r => r.Headers.GetValues("X-Request-ID").Single()).Distinct().Count());
        }

        [Fact]
        public void SharedHttpClient_HasNoDefaultHeadersAndNoOwnTimeout()
        {
            HttpClient client = DirectBackendFactory.CreateSharedHttpClient(new StubHttpHandler(_ => Ok()));

            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
            Assert.Empty(client.DefaultRequestHeaders);
        }

        [Fact]
        public async Task ValidateAsync_SucceedsOn200()
        {
            var handler = new StubHttpHandler(_ => AdapterTestKit.JsonResponse(HttpStatusCode.OK, "{}"));

            await Backend(handler).ValidateAsync("gemini-3-pro-image-preview", CancellationToken.None);

            Assert.EndsWith(":generateContent", Assert.Single(handler.Requests).RequestUri!.ToString());
        }

        [Fact]
        public async Task ValidateAsync_RejectedKey_IsAuthFailed()
        {
            var handler = new StubHttpHandler(_ => Error(400, TestFixtures.ReadText("gemini/error_invalid_key.json")));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler).ValidateAsync("", CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
        }

        [Fact]
        public async Task ValidateAsync_ProbeRejectedForOtherReasons_MeansTheKeyWasAccepted()
        {
            var handler = new StubHttpHandler(_ => Error(400, "{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"message\":\"TEXT modality not supported\"}}"));

            await Backend(handler).ValidateAsync("gemini-3-pro-image-preview", CancellationToken.None);
        }

        [Fact]
        public async Task ValidateAsync_UnknownModel_IsModelUnavailable()
        {
            var handler = new StubHttpHandler(_ => Error(404, TestFixtures.ReadText("gemini/error_model_not_found.json")));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(
                () => Backend(handler).ValidateAsync("does-not-exist", CancellationToken.None));

            Assert.Equal(RenderErrorCode.ModelUnavailable, ex.Code);
        }

        [Fact]
        public void GetCapabilities_KnownModelFromProfile_UnknownUnrestricted()
        {
            DirectBackend backend = Backend(new StubHttpHandler(_ => Ok()));

            Assert.Equal(8, backend.GetCapabilities("gemini-3-pro-image-preview").MaxInputImages);
            Assert.Equal(0, backend.GetCapabilities("mystery").MaxInputImages);
        }

        [Fact]
        public void Factory_PicksAdapterByFormatAndRejectsUnknownFormats()
        {
            var factory = new DirectBackendFactory(
                AdapterRegistry.CreateDefault(),
                DirectBackendFactory.CreateSharedHttpClient(new StubHttpHandler(_ => Ok())),
                new PromptComposer());

            Assert.Equal("direct:TestOpenAI", factory.Create(AdapterTestKit.OpenAIProfile(), new InMemorySecretStore()).Id);

            ProviderProfile unknown = AdapterTestKit.GeminiProfile();
            unknown.ApiFormat = "smoke-signals";
            RenderException ex = Assert.Throws<RenderException>(() => factory.Create(unknown, new InMemorySecretStore()));
            Assert.Equal(RenderErrorCode.InvalidRequest, ex.Code);
        }

        [Theory]
        [InlineData("direct:BltAI", true, "BltAI")]
        [InlineData("direct:", false, "")]
        [InlineData("cloud", false, "")]
        [InlineData(null, false, "")]
        public void BackendIds_ParseDirectIds(string? backendId, bool expected, string expectedProfileId)
        {
            Assert.Equal(expected, BackendIds.TryGetProfileId(backendId, out string profileId));
            Assert.Equal(expectedProfileId, profileId);
            Assert.Equal("direct:X", BackendIds.Direct("X"));
        }

        [Fact]
        public void RetryPolicy_AppliesJitterWithinTwentyPercent()
        {
            var low = new RetryPolicy(nextRandom: () => 0.0);
            var high = new RetryPolicy(nextRandom: () => 0.999999);
            var failure = new RenderException(RenderErrorCode.ProviderError, "x");

            Assert.Equal(1600, low.GetDelay(0, failure).TotalMilliseconds, 0);
            Assert.Equal(2400, high.GetDelay(0, failure).TotalMilliseconds, 0);
            Assert.Equal(0, RetryPolicy.None.MaxRetries);
        }

        /// <summary>IProgress that reports inline, so assertions do not race the thread pool.</summary>
        private sealed class SyncProgress : IProgress<GenerationProgress>
        {
            private readonly Action<GenerationProgress> _onReport;

            public SyncProgress(Action<GenerationProgress> onReport)
            {
                _onReport = onReport;
            }

            public void Report(GenerationProgress value) => _onReport(value);
        }
    }
}
