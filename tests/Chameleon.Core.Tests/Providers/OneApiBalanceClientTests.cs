using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Balance;
using Xunit;

namespace Chameleon.Core.Tests.Providers
{
    public class OneApiBalanceClientTests
    {
        private const string Key = "sk-test-key-123";

        private static ProviderProfile Relay(string? balanceApi = BalanceApis.OneApi)
        {
            return new ProviderProfile { Id = "relay", DisplayName = "Relay", BaseUrl = "https://relay.test/", BalanceApi = balanceApi };
        }

        private static (OneApiBalanceClient Client, StubHttpHandler Handler) Setup(string subscriptionFixture = "relay/subscription.json", HttpStatusCode status = HttpStatusCode.OK, string? body = null)
        {
            var handler = new StubHttpHandler(request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                string json = body ?? (path.EndsWith("/subscription", StringComparison.Ordinal)
                    ? TestFixtures.ReadText(subscriptionFixture)
                    : TestFixtures.ReadText("relay/usage.json"));
                return AdapterTestKit.JsonResponse(status, json, request);
            });
            return (new OneApiBalanceClient(new HttpClient(handler)), handler);
        }

        [Fact]
        public async Task Balance_IsTheLimitMinusTheUsageInCents()
        {
            var (client, handler) = Setup();

            ProviderBalance balance = await client.GetBalanceAsync(Relay(), Key, CancellationToken.None);

            Assert.Equal(2m, balance.Total);
            Assert.Equal(0.828m, balance.Used);
            Assert.Equal(1.172m, balance.Remaining);
            Assert.Equal("USD", balance.Currency);
            Assert.Equal(
                new[] { "https://relay.test/v1/dashboard/billing/subscription", "https://relay.test/v1/dashboard/billing/usage" },
                handler.Requests.Select(r => r.RequestUri!.ToString()));
            Assert.All(handler.Requests, r => Assert.Equal("Bearer " + Key, r.Headers.Authorization!.ToString()));
        }

        [Fact]
        public async Task KeysWithoutALimit_HaveNoRemainingAmount()
        {
            var (client, _) = Setup("relay/subscription_unlimited.json");

            ProviderBalance balance = await client.GetBalanceAsync(Relay(), Key, CancellationToken.None);

            Assert.Null(balance.Total);
            Assert.Null(balance.Remaining);
            Assert.Equal(0.828m, balance.Used);
        }

        [Fact]
        public void Remaining_IsNeverNegative()
        {
            Assert.Equal(0m, new ProviderBalance(1m, 3m, "USD").Remaining);
        }

        [Fact]
        public async Task ARejectedKey_IsAnAuthFailure_WithoutTheKeyInTheMessage()
        {
            var (client, _) = Setup(status: HttpStatusCode.Unauthorized, body: "{\"error\":{\"message\":\"Invalid token " + Key + "\"}}");

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => client.GetBalanceAsync(Relay(), Key, CancellationToken.None));

            Assert.Equal(RenderErrorCode.AuthFailed, ex.Code);
            Assert.DoesNotContain(Key, ex.ProviderMessage);
        }

        [Fact]
        public async Task UnreadableAnswers_AreProviderErrors()
        {
            var (client, _) = Setup(body: "{\"object\":\"list\"}");

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => client.GetBalanceAsync(Relay(), Key, CancellationToken.None));

            Assert.Equal(RenderErrorCode.ProviderError, ex.Code);
        }

        [Fact]
        public async Task ProvidersWithoutABalanceApi_AreRefusedWithoutARequest()
        {
            var (client, handler) = Setup();

            RenderException ex = await Assert.ThrowsAsync<RenderException>(() => client.GetBalanceAsync(Relay(null), Key, CancellationToken.None));

            Assert.Equal(RenderErrorCode.InvalidRequest, ex.Code);
            Assert.Empty(handler.Requests);
        }
    }
}
