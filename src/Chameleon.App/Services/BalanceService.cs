using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Balance;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>"providers.balance": what is left on a provider key, for relays that report it.</summary>
    public sealed class BalanceService
    {
        private readonly BackendRegistry _backends;
        private readonly ISecretStore _secrets;
        private readonly IBalanceClient _client;

        /// <summary>Creates the service.</summary>
        public BalanceService(BackendRegistry backends, ISecretStore secrets, IBalanceClient client)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<IdParams, BalanceDto>("providers.balance", GetAsync);
        }

        /// <summary>Queries the balance of the stored key of a provider.</summary>
        public async Task<BalanceDto> GetAsync(IdParams parameters, CancellationToken cancellationToken)
        {
            ProviderProfile profile = _backends.GetProviders().FirstOrDefault(p => p.Id == parameters.Id)
                                      ?? throw BridgeException.NotFound("Unknown provider '" + parameters.Id + "'.");
            if (profile.BalanceApi == null)
            {
                throw BridgeException.InvalidRequest("Provider '" + profile.DisplayName + "' does not report a balance.");
            }

            string? key = _secrets.Get(SecretNames.ForProvider(profile.Id));
            if (string.IsNullOrEmpty(key))
            {
                throw new BridgeException(BridgeErrorCodes.NotReady, "No API key is stored for '" + profile.DisplayName + "'.");
            }

            ProviderBalance balance = await _client.GetBalanceAsync(profile, key!, cancellationToken).ConfigureAwait(false);
            return new BalanceDto { Remaining = balance.Remaining, Used = balance.Used, Total = balance.Total, Currency = balance.Currency };
        }
    }
}
