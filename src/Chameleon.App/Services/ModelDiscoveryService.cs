using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>
    /// Keeps provider model lists current by asking the endpoints: built-in providers store the
    /// answer separately from the catalog (see <see cref="AppSettings.DiscoveredModels"/>); a
    /// provider the user added gets its own list replaced.
    /// </summary>
    public sealed class ModelDiscoveryService : IModelDiscovery
    {
        /// <summary>How old a built-in provider's list may get before start-up refreshes it.</summary>
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(1);

        private readonly ChameleonCatalog _catalog;
        private readonly BackendRegistry _backends;
        private readonly SettingsEditor _settings;
        private readonly ISecretStore _secrets;
        private readonly BackendService _backendService;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Action<string> _log;

        /// <summary>Creates the service.</summary>
        public ModelDiscoveryService(
            ChameleonCatalog catalog,
            BackendRegistry backends,
            SettingsEditor settings,
            ISecretStore secrets,
            BackendService backendService,
            Func<DateTimeOffset>? clock = null,
            Action<string>? log = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _backendService = backendService ?? throw new ArgumentNullException(nameof(backendService));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _log = log ?? (_ => { });
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<IdParams, DiscoveredModelsDto>("providers.discoverModels", (p, ct) => DiscoverAsync(p.Id, ct));
        }

        /// <summary>Asks a provider for its image models and makes them the provider's list.</summary>
        public async Task<DiscoveredModelsDto> DiscoverAsync(string providerId, CancellationToken cancellationToken)
        {
            ProviderProfile profile = _backends.GetProviders().FirstOrDefault(p => p.Id == providerId)
                                      ?? throw BridgeException.NotFound("Unknown provider '" + providerId + "'.");
            if (!(_backends.Find(BackendIds.Direct(providerId)) is DirectBackend backend))
            {
                throw BridgeException.NotFound("Unknown provider '" + providerId + "'.");
            }

            if (!_secrets.Contains(SecretNames.ForProvider(providerId)))
            {
                throw new BridgeException(BridgeErrorCodes.NotReady, "No API key is stored for '" + profile.DisplayName + "'.");
            }

            IReadOnlyList<string> found = await backend.DiscoverModelsAsync(cancellationToken).ConfigureAwait(false);
            List<string> before = profile.Models.Select(m => m.Id).ToList();
            if (found.Count == 0)
            {
                // An empty answer is more likely a misconfigured endpoint than "no models": keep the list.
                return new DiscoveredModelsDto { Models = before };
            }

            bool builtIn = _catalog.FindProvider(providerId) != null;
            _settings.Update(settings =>
            {
                if (builtIn)
                {
                    settings.DiscoveredModels[providerId] = new DiscoveredModels { Models = found.ToList(), CheckedAt = _clock() };
                    return;
                }

                UserProviderConfig? config = settings.UserProviders.FirstOrDefault(p => p.Id == providerId);
                if (config != null)
                {
                    config.Models = found.ToList();
                    if (!found.Contains(config.DefaultModel))
                    {
                        config.DefaultModel = found[0];
                    }
                }
            });

            await _backendService.PublishChangedAsync(cancellationToken).ConfigureAwait(false);
            return new DiscoveredModelsDto
            {
                Models = found,
                Added = found.Except(before).ToList(),
                Removed = before.Except(found).ToList()
            };
        }

        /// <inheritdoc />
        public void RefreshInBackground(string providerId)
        {
            _ = RefreshQuietlyAsync(providerId);
        }

        /// <summary>Refreshes built-in providers with a stored key whose list is older than <paramref name="maxAge"/>.</summary>
        public async Task RefreshStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken)
        {
            AppSettings settings = _settings.Read();
            foreach (CatalogProvider provider in _catalog.Providers)
            {
                bool stale = !settings.DiscoveredModels.TryGetValue(provider.Id, out DiscoveredModels? known)
                             || _clock() - known.CheckedAt > maxAge;
                if (stale && _secrets.Contains(SecretNames.ForProvider(provider.Id)))
                {
                    await RefreshQuietlyAsync(provider.Id, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task RefreshQuietlyAsync(string providerId, CancellationToken cancellationToken = default)
        {
            try
            {
                await DiscoverAsync(providerId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RenderException || ex is BridgeException || ex is OperationCanceledException)
            {
                // The catalog's list keeps working; the next refresh tries again.
                _log("Model discovery for '" + providerId + "' failed: " + ex.Message);
            }
        }
    }
}
