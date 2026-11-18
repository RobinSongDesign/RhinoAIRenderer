using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Providers.Balance
{
    /// <summary>
    /// Balance through the dashboard endpoints One API / new-api relays serve:
    /// <c>subscription.hard_limit_usd</c> is the total, <c>usage.total_usage</c> the amount used in
    /// US cents.
    /// </summary>
    public sealed class OneApiBalanceClient : IBalanceClient
    {
        // Relays report keys without a limit with an absurdly large hard limit.
        private const decimal UnlimitedThreshold = 100_000_000m;

        private readonly HttpClient _http;

        /// <summary>Creates the client over the shared HTTP client.</summary>
        public OneApiBalanceClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <inheritdoc />
        public async Task<ProviderBalance> GetBalanceAsync(ProviderProfile profile, string apiKey, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (profile.BalanceApi != BalanceApis.OneApi)
            {
                throw new RenderException(RenderErrorCode.InvalidRequest, "Provider '" + profile.DisplayName + "' does not report a balance.");
            }

            JObject subscription = await GetAsync(profile, apiKey, "/v1/dashboard/billing/subscription", cancellationToken).ConfigureAwait(false);
            JObject usage = await GetAsync(profile, apiKey, "/v1/dashboard/billing/usage", cancellationToken).ConfigureAwait(false);

            decimal? total = ReadDecimal(subscription, "hard_limit_usd");
            decimal? usedCents = ReadDecimal(usage, "total_usage");
            if (total == null || usedCents == null)
            {
                throw new RenderException(RenderErrorCode.ProviderError, "The provider's balance answer is missing hard_limit_usd or total_usage.");
            }

            return new ProviderBalance(total.Value >= UnlimitedThreshold ? (decimal?)null : total.Value, usedCents.Value / 100m, "USD");
        }

        private async Task<JObject> GetAsync(ProviderProfile profile, string apiKey, string path, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, profile.BaseUrl.TrimEnd('/') + path))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using (HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw RenderException.FromHttpStatus((int)response.StatusCode, Redactor.Redact(body, apiKey));
                    }

                    try
                    {
                        return JObject.Parse(body);
                    }
                    catch (JsonException)
                    {
                        throw new RenderException(RenderErrorCode.ProviderError, "The provider's balance answer is not JSON.", (int)response.StatusCode, Redactor.Redact(body, apiKey));
                    }
                }
            }
        }

        private static decimal? ReadDecimal(JObject json, string name)
        {
            JToken? token = json[name];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            {
                return null;
            }

            return decimal.Parse(token.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
