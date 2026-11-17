using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>Managing bring-your-own-key providers and their API keys.</summary>
    public sealed class ProviderService
    {
        private readonly ChameleonCatalog _catalog;
        private readonly SettingsEditor _settings;
        private readonly ISecretStore _secrets;
        private readonly BackendRegistry _backends;
        private readonly BackendService _backendService;
        private readonly DirectBackendFactory _factory;

        /// <summary>Creates the service.</summary>
        public ProviderService(
            ChameleonCatalog catalog,
            SettingsEditor settings,
            ISecretStore secrets,
            BackendRegistry backends,
            BackendService backendService,
            DirectBackendFactory factory)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _backendService = backendService ?? throw new ArgumentNullException(nameof(backendService));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register("providers.list", ct => Task.FromResult(List()));
            router.Register<ProviderDto, ProviderDto>("providers.save", SaveAsync);
            router.Register<IdParams, object?>("providers.delete", async (p, ct) =>
            {
                await DeleteAsync(p.Id, ct).ConfigureAwait(false);
                return null;
            });
            router.Register<IdParams, ProviderDto>("providers.resetBuiltIn", (p, ct) => ResetBuiltInAsync(p.Id, ct));
            router.Register<SetKeyParams, object>("providers.setKey", async (p, ct) =>
                new { hasKey = await SetKeyAsync(p.Id, p.Key, ct).ConfigureAwait(false) });
            router.Register<TestKeyParams, object>("providers.testKey", async (p, ct) =>
            {
                await TestKeyAsync(p, ct).ConfigureAwait(false);
                return new { ok = true };
            });
        }

        /// <summary>Lists all providers. API keys are never included, only whether one is stored.</summary>
        public IReadOnlyList<ProviderDto> List()
        {
            return _backends.GetProviders().Select(ToDto).ToList();
        }

        /// <summary>Creates a provider, updates a user-defined one, or overrides a built-in one.</summary>
        public async Task<ProviderDto> SaveAsync(ProviderDto provider, CancellationToken cancellationToken)
        {
            List<string> models = (provider.Models ?? new List<string>())
                .Select(m => (m ?? string.Empty).Trim())
                .Where(m => m.Length > 0)
                .Distinct()
                .ToList();
            string displayName = (provider.DisplayName ?? string.Empty).Trim();
            string baseUrl = (provider.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            string apiFormat = string.IsNullOrWhiteSpace(provider.ApiFormat) ? ApiFormats.Gemini : provider.ApiFormat.Trim();
            // The OpenAI format only knows bearer authentication.
            string authType = apiFormat == ApiFormats.OpenAI || string.IsNullOrWhiteSpace(provider.AuthType)
                ? AuthTypes.Bearer
                : provider.AuthType.Trim();

            if (displayName.Length == 0)
            {
                throw BridgeException.InvalidRequest("A provider name is required.");
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw BridgeException.InvalidRequest("The base URL must start with http:// or https://.");
            }

            if (models.Count == 0)
            {
                throw BridgeException.InvalidRequest("At least one model is required.");
            }

            if (apiFormat != ApiFormats.Gemini && apiFormat != ApiFormats.OpenAI)
            {
                throw BridgeException.InvalidRequest("Unknown API format '" + apiFormat + "'.");
            }

            if (authType != AuthTypes.Bearer && authType != AuthTypes.Goog)
            {
                throw BridgeException.InvalidRequest("Unknown authentication type '" + authType + "'.");
            }

            string defaultModel = models.Contains(provider.DefaultModel ?? string.Empty) ? provider.DefaultModel! : models[0];
            string id = (provider.Id ?? string.Empty).Trim();
            bool isBuiltIn = _catalog.FindProvider(id) != null;

            if (id.Length == 0)
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 12);
            }

            _settings.Update(settings =>
            {
                if (isBuiltIn)
                {
                    settings.BuiltInOverrides[id] = new ProviderOverride
                    {
                        DisplayName = displayName,
                        BaseUrl = baseUrl,
                        ApiFormat = apiFormat,
                        AuthType = authType,
                        Models = models,
                        DefaultModel = defaultModel,
                        EnableSearchGrounding = provider.EnableSearchGrounding
                    };
                    return;
                }

                var config = new UserProviderConfig
                {
                    Id = id,
                    DisplayName = displayName,
                    BaseUrl = baseUrl,
                    ApiFormat = apiFormat,
                    AuthType = authType,
                    Models = models,
                    DefaultModel = defaultModel,
                    EnableSearchGrounding = provider.EnableSearchGrounding
                };
                int index = settings.UserProviders.FindIndex(p => p.Id == id);
                if (index >= 0)
                {
                    settings.UserProviders[index] = config;
                }
                else
                {
                    settings.UserProviders.Add(config);
                }
            });

            await _backendService.PublishChangedAsync(cancellationToken).ConfigureAwait(false);
            return Require(id);
        }

        /// <summary>Deletes a user-defined provider and its key.</summary>
        public async Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            if (_catalog.FindProvider(id) != null)
            {
                throw BridgeException.InvalidRequest("Built-in providers cannot be deleted; reset them instead.");
            }

            bool removed = false;
            string backendId = BackendIds.Direct(id);
            _settings.Update(settings =>
            {
                removed = settings.UserProviders.RemoveAll(p => p.Id == id) > 0;
                settings.ActiveModelByBackend.Remove(backendId);
                if (settings.ActiveBackendId == backendId)
                {
                    settings.ActiveBackendId = _backends.DefaultBackendId;
                }
            });

            if (!removed)
            {
                throw BridgeException.NotFound("Unknown provider '" + id + "'.");
            }

            _secrets.Delete(SecretNames.ForProvider(id));
            await _backendService.PublishChangedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Discards the user's changes to a built-in provider.</summary>
        public async Task<ProviderDto> ResetBuiltInAsync(string id, CancellationToken cancellationToken)
        {
            if (_catalog.FindProvider(id) == null)
            {
                throw BridgeException.NotFound("'" + id + "' is not a built-in provider.");
            }

            _settings.Update(settings => settings.BuiltInOverrides.Remove(id));
            await _backendService.PublishChangedAsync(cancellationToken).ConfigureAwait(false);
            return Require(id);
        }

        /// <summary>Stores or clears a provider's API key.</summary>
        /// <returns>True when a key is stored afterwards.</returns>
        public async Task<bool> SetKeyAsync(string id, string? key, CancellationToken cancellationToken)
        {
            Require(id);
            string name = SecretNames.ForProvider(id);
            string trimmed = (key ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _secrets.Delete(name);
            }
            else
            {
                _secrets.Set(name, trimmed);
            }

            await _backendService.PublishChangedAsync(cancellationToken).ConfigureAwait(false);
            return trimmed.Length > 0;
        }

        /// <summary>Checks a key against the provider. A key passed in is tested without being stored.</summary>
        public async Task TestKeyAsync(TestKeyParams parameters, CancellationToken cancellationToken)
        {
            ProviderProfile profile = _backends.GetProviders().FirstOrDefault(p => p.Id == parameters.Id)
                                      ?? throw BridgeException.NotFound("Unknown provider '" + parameters.Id + "'.");

            ISecretStore secrets = _secrets;
            if (!string.IsNullOrWhiteSpace(parameters.Key))
            {
                var temporary = new InMemorySecretStore();
                temporary.Set(SecretNames.ForProvider(profile.Id), parameters.Key!.Trim());
                secrets = temporary;
            }

            DirectBackend backend = _factory.Create(profile, secrets);
            await backend.ValidateAsync(parameters.ModelId ?? profile.DefaultModel, cancellationToken).ConfigureAwait(false);
        }

        private ProviderDto Require(string id)
        {
            ProviderProfile? profile = _backends.GetProviders().FirstOrDefault(p => p.Id == id);
            return profile != null ? ToDto(profile) : throw BridgeException.NotFound("Unknown provider '" + id + "'.");
        }

        private ProviderDto ToDto(ProviderProfile profile)
        {
            return new ProviderDto
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                BaseUrl = profile.BaseUrl,
                ApiFormat = profile.ApiFormat,
                AuthType = profile.AuthType,
                Models = profile.Models.Select(m => m.Id).ToList(),
                DefaultModel = profile.DefaultModel,
                ApiKeyUrl = profile.ApiKeyUrl,
                IsBuiltIn = profile.IsBuiltIn,
                IsOverridden = profile.IsOverridden,
                EnableSearchGrounding = profile.EnableSearchGrounding,
                HasKey = _secrets.Contains(SecretNames.ForProvider(profile.Id))
            };
        }
    }
}
