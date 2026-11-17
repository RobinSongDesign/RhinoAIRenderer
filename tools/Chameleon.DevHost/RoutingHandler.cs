using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.DevHost
{
    /// <summary>Sends requests for one host to a fake handler and everything else on to the next handler.</summary>
    internal sealed class RoutingHandler : DelegatingHandler
    {
        private readonly string _host;
        private readonly HttpMessageInvoker _fake;

        public RoutingHandler(string host, HttpMessageHandler fake, HttpMessageHandler fallback)
            : base(fallback)
        {
            _host = host;
            _fake = new HttpMessageInvoker(fake);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return string.Equals(request.RequestUri?.Host, _host, StringComparison.OrdinalIgnoreCase)
                ? _fake.SendAsync(request, cancellationToken)
                : base.SendAsync(request, cancellationToken);
        }
    }
}
