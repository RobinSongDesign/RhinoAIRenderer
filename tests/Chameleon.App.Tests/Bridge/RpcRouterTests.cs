using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Hosting;
using Chameleon.Core.Generation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Bridge
{
    public class RpcRouterTests
    {
        private sealed class EchoParams
        {
            public string Text { get; set; } = string.Empty;

            public int Times { get; set; } = 1;
        }

        private sealed class EchoResult
        {
            public string Echoed { get; set; } = string.Empty;
        }

        private readonly List<string> _log = new List<string>();
        private readonly RpcRouter _router;

        public RpcRouterTests()
        {
            _router = new RpcRouter(_log.Add);
            _router.Register<EchoParams, EchoResult>("test.echo", (p, ct) =>
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < p.Times; i++)
                {
                    sb.Append(p.Text);
                }

                return Task.FromResult(new EchoResult { Echoed = sb.ToString() });
            });
            _router.Register("test.ping", ct => Task.FromResult("pong"));
            _router.Register<object?>("test.nothing", ct => Task.FromResult<object?>(null));
        }

        private Task<RpcResponse> Call(string method, object? parameters = null)
        {
            return _router.DispatchAsync(
                new RpcRequest { Id = "7", Method = method, Params = parameters == null ? null : JToken.FromObject(parameters) },
                CancellationToken.None);
        }

        private void RegisterThrowing(string method, Exception exception)
        {
            _router.Register<object?>(method, ct => throw exception);
        }

        [Fact]
        public async Task Dispatch_RunsHandlerWithTypedParameters()
        {
            RpcResponse response = await Call("test.echo", new { text = "ab", times = 3 });

            Assert.Equal("7", response.Id);
            Assert.Null(response.Error);
            Assert.Equal("ababab", Assert.IsType<EchoResult>(response.Result).Echoed);
        }

        [Fact]
        public async Task Dispatch_ParameterlessMethod_IgnoresParameters()
        {
            Assert.Equal("pong", (await Call("test.ping")).Result);
            Assert.Equal("pong", (await Call("test.ping", new { anything = 1 })).Result);
        }

        [Fact]
        public async Task Dispatch_UnknownOrEmptyMethod_IsInvalidRequest()
        {
            Assert.Equal(BridgeErrorCodes.InvalidRequest, (await Call("nope")).Error!.Code);
            Assert.Equal(BridgeErrorCodes.InvalidRequest, (await Call("")).Error!.Code);
            Assert.Equal(BridgeErrorCodes.InvalidRequest, (await _router.DispatchAsync(null!, CancellationToken.None)).Error!.Code);
        }

        [Fact]
        public async Task Dispatch_MissingOrMistypedParameters_AreInvalidRequest()
        {
            RpcResponse missing = await Call("test.echo");
            RpcResponse mistyped = await Call("test.echo", new { text = "a", times = "many" });

            Assert.Equal(BridgeErrorCodes.InvalidRequest, missing.Error!.Code);
            Assert.Contains("requires parameters", missing.Error.Message);
            Assert.Equal(BridgeErrorCodes.InvalidRequest, mistyped.Error!.Code);
            Assert.Empty(_log);
        }

        [Fact]
        public async Task Dispatch_UnknownParameterFields_AreIgnored()
        {
            RpcResponse response = await Call("test.echo", new { text = "x", extra = true });

            Assert.Equal("x", Assert.IsType<EchoResult>(response.Result).Echoed);
        }

        [Theory]
        [InlineData(RenderErrorCode.InvalidRequest, "invalid_request")]
        [InlineData(RenderErrorCode.AuthFailed, "auth_failed")]
        [InlineData(RenderErrorCode.InsufficientCredits, "insufficient_credits")]
        [InlineData(RenderErrorCode.RateLimited, "rate_limited")]
        [InlineData(RenderErrorCode.ContentBlocked, "content_blocked")]
        [InlineData(RenderErrorCode.ModelUnavailable, "model_unavailable")]
        [InlineData(RenderErrorCode.Timeout, "timeout")]
        [InlineData(RenderErrorCode.Network, "network")]
        [InlineData(RenderErrorCode.ProviderError, "provider_error")]
        [InlineData(RenderErrorCode.NoImageReturned, "no_image")]
        [InlineData(RenderErrorCode.Unknown, "internal")]
        public async Task Dispatch_RenderException_MapsCodeDetailAndRetryability(RenderErrorCode code, string expected)
        {
            var exception = new RenderException(code, "failed", 500, "provider said no");
            RegisterThrowing("test.render", exception);

            RpcError error = (await Call("test.render")).Error!;

            Assert.Equal(expected, error.Code);
            Assert.Equal("provider said no", error.Detail);
            Assert.Equal(exception.IsRetryable, error.Retryable);
        }

        [Fact]
        public async Task Dispatch_OtherExceptions_MapToTheirCodes()
        {
            RegisterThrowing("t.cancel", new OperationCanceledException());
            RegisterThrowing("t.rhino", new RhinoHostException("No active document."));
            RegisterThrowing("t.missing", new KeyNotFoundException("asset gone"));
            RegisterThrowing("t.bridge", new BridgeException(BridgeErrorCodes.NotReady, "no key", "set a key"));

            Assert.Equal("cancelled", (await Call("t.cancel")).Error!.Code);
            RpcError rhino = (await Call("t.rhino")).Error!;
            Assert.Equal("rhino_error", rhino.Code);
            Assert.Equal("No active document.", rhino.Detail);
            Assert.Equal("not_found", (await Call("t.missing")).Error!.Code);
            RpcError bridge = (await Call("t.bridge")).Error!;
            Assert.Equal("not_ready", bridge.Code);
            Assert.Equal("set a key", bridge.Detail);
            Assert.Empty(_log);
        }

        [Fact]
        public async Task Dispatch_UnexpectedException_IsInternalWithoutStackTraceAndIsLogged()
        {
            RegisterThrowing("t.bug", new InvalidOperationException("boom"));

            RpcError error = (await Call("t.bug")).Error!;

            Assert.Equal("internal", error.Code);
            Assert.Equal("InvalidOperationException: boom", error.Detail);
            Assert.DoesNotContain(" at ", error.Detail);
            Assert.Single(_log);
        }

        [Fact]
        public void Register_DuplicateMethod_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _router.Register("test.ping", ct => Task.FromResult("again")));
            Assert.Contains("test.echo", _router.Methods);
        }

        [Fact]
        public async Task Response_SerializesNullResultExplicitlyAndOmitsResultOnError()
        {
            string ok = BridgeJson.Serialize(await Call("test.nothing"));
            string failed = BridgeJson.Serialize(await Call("nope"));

            Assert.Equal("{\"id\":\"7\",\"result\":null}", ok);
            Assert.DoesNotContain("\"result\"", failed);
            Assert.Contains("\"error\":{\"code\":\"invalid_request\"", failed);
        }

        [Fact]
        public void BridgeJson_UsesCamelCaseAndKeepsDictionaryKeys()
        {
            var value = new Dictionary<string, object> { ["direct:BltAI"] = new EchoResult { Echoed = "x" } };

            Assert.Equal("{\"direct:BltAI\":{\"echoed\":\"x\"}}", BridgeJson.Serialize(value));
        }
    }
}
