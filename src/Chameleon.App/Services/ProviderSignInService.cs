using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Accounts;

namespace Chameleon.App.Services
{
    /// <summary>
    /// "providers.signIn", "providers.signInVerify", "providers.signOut": signing in to a relay
    /// account from the plug-in. The account's key ends up stored exactly like a pasted one, so
    /// every other part of the app treats both the same way.
    /// </summary>
    public sealed class ProviderSignInService
    {
        private readonly BackendRegistry _backends;
        private readonly ProviderService _providers;
        private readonly IRelayAccountClient _client;
        private readonly string _keyName;

        /// <summary>Creates the service.</summary>
        /// <param name="backends">Source of the provider profiles.</param>
        /// <param name="providers">Stores the key and the signed-in account name.</param>
        /// <param name="client">Talks to the gateway.</param>
        /// <param name="keyName">Name of the key created on the account for this installation.</param>
        public ProviderSignInService(BackendRegistry backends, ProviderService providers, IRelayAccountClient client, string keyName)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _keyName = string.IsNullOrWhiteSpace(keyName) ? throw new ArgumentException("A key name is required.", nameof(keyName)) : keyName;
        }

        /// <summary>The key name used for a machine: one key per installation, reused on later sign-ins.</summary>
        public static string KeyNameFor(string machineName)
        {
            string machine = (machineName ?? string.Empty).Trim();
            return machine.Length == 0 ? "Chameleon for Rhino" : "Chameleon for Rhino (" + machine + ")";
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<SignInParams, SignInResultDto>("providers.signIn", SignInAsync);
            router.Register<SignInVerifyParams, SignInResultDto>("providers.signInVerify", VerifyAsync);
            router.Register<IdParams, object?>("providers.signOut", async (p, ct) =>
            {
                await SignOutAsync(p.Id, ct).ConfigureAwait(false);
                return null;
            });
        }

        /// <summary>Signs in with username and password.</summary>
        public async Task<SignInResultDto> SignInAsync(SignInParams parameters, CancellationToken cancellationToken)
        {
            ProviderProfile profile = RequireSignIn(parameters.Id);
            RelaySignInResult result = await _client
                .SignInAsync(profile, parameters.Username, parameters.Password, _keyName, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(profile, result, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Completes a sign-in with a two-factor code.</summary>
        public async Task<SignInResultDto> VerifyAsync(SignInVerifyParams parameters, CancellationToken cancellationToken)
        {
            ProviderProfile profile = RequireSignIn(parameters.Id);
            if (string.IsNullOrWhiteSpace(parameters.FlowToken))
            {
                throw BridgeException.InvalidRequest("The sign-in expired; start again.");
            }

            RelaySignInResult result = await _client
                .VerifyTwoFactorAsync(profile, parameters.FlowToken, parameters.Code, _keyName, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(profile, result, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Forgets the account's key on this machine. The key stays on the account for the next sign-in.</summary>
        public Task SignOutAsync(string id, CancellationToken cancellationToken)
        {
            RequireSignIn(id);
            return _providers.SetKeyAsync(id, null, cancellationToken);
        }

        private async Task<SignInResultDto> CompleteAsync(ProviderProfile profile, RelaySignInResult result, CancellationToken cancellationToken)
        {
            if (result.NeedsTwoFactor)
            {
                return new SignInResultDto { Status = SignInResultDto.StatusNeedsTwoFactor, FlowToken = result.FlowToken };
            }

            await _providers.SetKeyAsync(profile.Id, result.ApiKey, cancellationToken, result.Username).ConfigureAwait(false);
            return new SignInResultDto { Status = SignInResultDto.StatusSignedIn, Username = result.Username };
        }

        private ProviderProfile RequireSignIn(string id)
        {
            ProviderProfile profile = _backends.GetProviders().FirstOrDefault(p => p.Id == id)
                                      ?? throw BridgeException.NotFound("Unknown provider '" + id + "'.");
            if (profile.SignInApi == null)
            {
                throw BridgeException.InvalidRequest("Provider '" + profile.DisplayName + "' does not support signing in.");
            }

            return profile;
        }
    }
}
