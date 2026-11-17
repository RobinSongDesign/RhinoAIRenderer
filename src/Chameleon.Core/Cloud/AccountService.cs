using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Cloud
{
    /// <summary>Account operations: balance, redeem codes, ledger, price estimates.</summary>
    public sealed class AccountService
    {
        private readonly CloudApiClient _api;
        private readonly AuthSession _session;
        private readonly CloudCatalogCache _catalog;

        /// <summary>Creates the service.</summary>
        public AccountService(CloudApiClient api, AuthSession session, CloudCatalogCache catalog)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Fetches the user and balance, and remembers them in the session.</summary>
        public async Task<CloudUser> GetMeAsync(CancellationToken ct)
        {
            CloudUser user = await _session.RunAuthorizedAsync(token => _api.GetMeAsync(token, ct), ct).ConfigureAwait(false);
            _session.UpdateUser(user);
            return user;
        }

        /// <summary>Redeems a code. Gateway answers such as "already used" stay <see cref="CloudApiException"/>s, so callers can tell them apart.</summary>
        public async Task<RedeemResult> RedeemAsync(string code, CancellationToken ct)
        {
            string normalized = Normalize(code);
            if (normalized.Length == 0)
            {
                throw new RenderException(RenderErrorCode.InvalidRequest, "Enter a redeem code.");
            }

            string token = await _session.GetAccessTokenAsync(ct).ConfigureAwait(false);
            // One key per attempt: a retried click must not redeem twice.
            RedeemResult result = await _api.RedeemAsync(token, normalized, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
            if (_session.User != null)
            {
                _session.User.Credits = result.Credits;
            }

            return result;
        }

        /// <summary>One page of the ledger.</summary>
        public Task<Page<LedgerEntry>> GetLedgerAsync(string? cursor, CancellationToken ct)
        {
            return _session.RunAuthorizedAsync(token => _api.GetLedgerAsync(token, cursor, ct), ct);
        }

        /// <summary>Credits a generation would cost, from the cached catalog; null when not offered.</summary>
        public int? Estimate(string modelId, string imageSize)
        {
            return _catalog.GetPrice(modelId, imageSize);
        }

        /// <summary>Upper-cases a code and drops spaces and hyphens, as the gateway does.</summary>
        public static string Normalize(string? code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            var chars = new System.Text.StringBuilder(code!.Length);
            foreach (char c in code)
            {
                if (!char.IsWhiteSpace(c) && c != '-')
                {
                    chars.Append(char.ToUpperInvariant(c));
                }
            }

            return chars.ToString();
        }
    }
}
