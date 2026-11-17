using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Core.Tests
{
    /// <summary>Scriptable HttpMessageHandler: records every request and answers from a callback.</summary>
    internal sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        private readonly object _gate = new object();

        public StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Requests.Add(request);
            }

            HttpResponseMessage response = await _respond(request, cancellationToken).ConfigureAwait(false);
            response.RequestMessage = response.RequestMessage ?? request;
            return response;
        }
    }
}
