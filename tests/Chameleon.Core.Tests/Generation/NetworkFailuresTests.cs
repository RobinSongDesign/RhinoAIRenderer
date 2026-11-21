using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Accounts;
using Chameleon.Core.Providers.Balance;
using Xunit;

namespace Chameleon.Core.Tests.Generation
{
    public class NetworkFailuresTests
    {
        // Windows error 10013 (WSAEACCES): what a firewall rule blocking the program produces.
        private static SocketException Blocked() => new SocketException((int)SocketError.AccessDenied);

        private static ProviderProfile Relay()
        {
            return new ProviderProfile
            {
                Id = "relay", DisplayName = "Relay", BaseUrl = "https://relay.test",
                BalanceApi = BalanceApis.OneApi, SignInApi = SignInApis.NewApi
            };
        }

        private static HttpClient Throwing(Exception exception)
        {
            return new HttpClient(new StubHttpHandler((request, ct) => Task.FromException<HttpResponseMessage>(exception)));
        }

        [Fact]
        public void RecognizesTheSystemRefusal_HoweverTheRuntimeWrapsIt()
        {
            // .NET 7 (Rhino 8 default): HttpRequestException → SocketException.
            Assert.True(NetworkFailures.IsBlockedBySystem(new HttpRequestException("denied", Blocked())));
            // .NET Framework (Rhino 8 /netfx): HttpRequestException → WebException → SocketException.
            Assert.True(NetworkFailures.IsBlockedBySystem(new HttpRequestException("denied", new WebException("denied", Blocked()))));
            Assert.False(NetworkFailures.IsBlockedBySystem(new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))));
            Assert.False(NetworkFailures.IsBlockedBySystem(new IOException("reset")));
            Assert.False(NetworkFailures.IsBlockedBySystem(null));
        }

        [Fact]
        public void BlockedConnections_AreNotRetried_OtherFailuresAre()
        {
            RenderException blocked = NetworkFailures.From(new HttpRequestException("x", Blocked()), "detail");
            RenderException other = NetworkFailures.From(new HttpRequestException("x"), "detail", "the account website");

            Assert.Equal(RenderErrorCode.NetworkBlocked, blocked.Code);
            Assert.False(blocked.IsRetryable);
            Assert.Equal(RenderErrorCode.Network, other.Code);
            Assert.True(other.IsRetryable);
            Assert.Contains("account website", other.Message);
        }

        [Fact]
        public async Task SignIn_ReportsABlockedComputer_InsteadOfARawException()
        {
            var client = new NewApiAccountClient(Throwing(new HttpRequestException("denied", Blocked())));

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() =>
                client.SignInAsync(Relay(), "alice", "test-password-123", "key", CancellationToken.None));

            Assert.Equal(RenderErrorCode.NetworkBlocked, ex.Code);
        }

        [Fact]
        public async Task BalanceQueries_ReportNetworkFailures_AsRenderExceptions()
        {
            var blocked = new OneApiBalanceClient(Throwing(new HttpRequestException("denied", Blocked())));
            var down = new OneApiBalanceClient(Throwing(new HttpRequestException("no route")));

            Assert.Equal(RenderErrorCode.NetworkBlocked, (await Assert.ThrowsAsync<RenderException>(() => blocked.GetBalanceAsync(Relay(), "test-key-123", CancellationToken.None))).Code);
            Assert.Equal(RenderErrorCode.Network, (await Assert.ThrowsAsync<RenderException>(() => down.GetBalanceAsync(Relay(), "test-key-123", CancellationToken.None))).Code);
        }
    }
}
