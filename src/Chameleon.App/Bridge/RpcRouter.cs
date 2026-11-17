using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.App.Bridge
{
    /// <summary>Dispatches RPC requests to registered handlers and turns failures into bridge errors.</summary>
    public sealed class RpcRouter
    {
        private readonly Dictionary<string, IRpcHandler> _handlers = new Dictionary<string, IRpcHandler>(StringComparer.Ordinal);
        private readonly Action<string>? _log;

        /// <summary>Creates a router.</summary>
        /// <param name="log">Receives a line for every unexpected failure; may be null.</param>
        public RpcRouter(Action<string>? log = null)
        {
            _log = log;
        }

        /// <summary>Names of all registered methods.</summary>
        public IReadOnlyCollection<string> Methods => _handlers.Keys;

        /// <summary>Registers a handler object.</summary>
        /// <exception cref="InvalidOperationException">The method is already registered.</exception>
        public void Register(IRpcHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_handlers.ContainsKey(handler.Method))
            {
                throw new InvalidOperationException("RPC method '" + handler.Method + "' is already registered.");
            }

            _handlers[handler.Method] = handler;
        }

        /// <summary>Registers a method that takes typed parameters and returns a typed result.</summary>
        public void Register<TParams, TResult>(string method, Func<TParams, CancellationToken, Task<TResult>> handle)
            where TParams : class
        {
            Register(new DelegateHandler(method, async (parameters, ct) =>
            {
                if (parameters == null || parameters.Type == JTokenType.Null)
                {
                    throw BridgeException.InvalidRequest("Method '" + method + "' requires parameters.");
                }

                TParams? typed = BridgeJson.ToObject<TParams>(parameters);
                if (typed == null)
                {
                    throw BridgeException.InvalidRequest("Method '" + method + "' requires parameters.");
                }

                return (object?)await handle(typed, ct).ConfigureAwait(false);
            }));
        }

        /// <summary>Registers a method without parameters.</summary>
        public void Register<TResult>(string method, Func<CancellationToken, Task<TResult>> handle)
        {
            Register(new DelegateHandler(method, async (parameters, ct) => (object?)await handle(ct).ConfigureAwait(false)));
        }

        /// <summary>Registers a method that inspects its raw JSON parameters.</summary>
        public void RegisterRaw(string method, Func<JToken?, CancellationToken, Task<object?>> handle)
        {
            Register(new DelegateHandler(method, handle));
        }

        /// <summary>Runs a request. Never throws: every failure is reported in the response.</summary>
        public async Task<RpcResponse> DispatchAsync(RpcRequest request, CancellationToken cancellationToken)
        {
            var response = new RpcResponse { Id = request?.Id ?? string.Empty };

            if (request == null || string.IsNullOrEmpty(request.Method) || !_handlers.TryGetValue(request.Method, out IRpcHandler? handler))
            {
                response.Error = new RpcError
                {
                    Code = BridgeErrorCodes.InvalidRequest,
                    Message = "Unknown method '" + request?.Method + "'."
                };
                return response;
            }

            try
            {
                response.Result = await handler.HandleAsync(request.Params, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response.Error = BridgeErrorMapper.FromException(ex);
                if (response.Error.Code == BridgeErrorCodes.Internal)
                {
                    _log?.Invoke("RPC " + request.Method + " failed: " + ex);
                }
            }

            return response;
        }

        private sealed class DelegateHandler : IRpcHandler
        {
            private readonly Func<JToken?, CancellationToken, Task<object?>> _handle;

            public DelegateHandler(string method, Func<JToken?, CancellationToken, Task<object?>> handle)
            {
                Method = method;
                _handle = handle;
            }

            public string Method { get; }

            public Task<object?> HandleAsync(JToken? parameters, CancellationToken cancellationToken)
            {
                return _handle(parameters, cancellationToken);
            }
        }
    }
}
