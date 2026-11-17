using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>
    /// The live set of backends: one <see cref="DirectBackend"/> per configured provider plus any
    /// extra backends (account mode, development fakes). Rebuilt lazily after settings change.
    /// </summary>
    public sealed class BackendRegistry
    {
        private readonly ChameleonCatalog _catalog;
        private readonly SettingsEditor _settings;
        private readonly ISecretStore _secrets;
        private readonly DirectBackendFactory _factory;
        private readonly IReadOnlyList<IRenderBackend> _extraBackends;
        private readonly ProviderRegistry _providers;
        private readonly object _gate = new object();
        private List<IRenderBackend>? _backends;

        /// <summary>Creates the registry.</summary>
        public BackendRegistry(
            ChameleonCatalog catalog,
            SettingsEditor settings,
            ISecretStore secrets,
            DirectBackendFactory factory,
            IEnumerable<IRenderBackend>? extraBackends = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _extraBackends = (extraBackends ?? Enumerable.Empty<IRenderBackend>()).ToList();
            _providers = new ProviderRegistry(catalog);
        }

        /// <summary>Id of the backend used when nothing valid is selected.</summary>
        public string DefaultBackendId => BackendIds.Direct(_catalog.DefaultProviderId);

        /// <summary>Forgets the cached backends; the next call rebuilds them from settings.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _backends = null;
            }
        }

        /// <summary>Returns the effective provider list for the current settings.</summary>
        public IReadOnlyList<ProviderProfile> GetProviders()
        {
            AppSettings settings = _settings.Read();
            return _providers.Build(settings.UserProviders, settings.BuiltInOverrides, settings.Language);
        }

        /// <summary>Finds a backend by id; null when unknown.</summary>
        public IRenderBackend? Find(string backendId)
        {
            return GetAll().FirstOrDefault(b => string.Equals(b.Id, backendId, StringComparison.Ordinal));
        }

        /// <summary>Returns all backends: providers first, then the extra ones.</summary>
        public IReadOnlyList<IRenderBackend> GetAll()
        {
            lock (_gate)
            {
                if (_backends == null)
                {
                    var backends = new List<IRenderBackend>();
                    foreach (ProviderProfile profile in GetProviders())
                    {
                        try
                        {
                            backends.Add(_factory.Create(profile, _secrets));
                        }
                        catch (RenderException)
                        {
                            // A provider with an API format this build does not know is simply not offered.
                        }
                    }

                    backends.AddRange(_extraBackends);
                    _backends = backends;
                }

                return _backends;
            }
        }

        /// <summary>True when the backend exists and can be used now.</summary>
        public bool IsReady(IRenderBackend backend)
        {
            if (backend is IBackendStatus status)
            {
                return status.IsReady;
            }

            return backend is DirectBackend direct
                ? _secrets.Contains(SecretNames.ForProvider(direct.Profile.Id))
                : true;
        }

        /// <summary>Model a backend selects by default.</summary>
        public async Task<string> GetDefaultModelAsync(IRenderBackend backend, CancellationToken cancellationToken)
        {
            if (backend is DirectBackend direct && !string.IsNullOrEmpty(direct.Profile.DefaultModel))
            {
                return direct.Profile.DefaultModel;
            }

            IReadOnlyList<ModelInfo> models = await backend.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            return models.Count > 0 ? models[0].Id : string.Empty;
        }

        /// <summary>Describes every backend for the UI.</summary>
        public async Task<IReadOnlyList<BackendInfoDto>> DescribeAsync(CancellationToken cancellationToken)
        {
            var result = new List<BackendInfoDto>();
            foreach (IRenderBackend backend in GetAll())
            {
                result.Add(new BackendInfoDto
                {
                    Id = backend.Id,
                    Kind = backend is IBackendStatus status ? status.Kind : BackendInfoDto.KindDirect,
                    DisplayName = backend.DisplayName,
                    Ready = IsReady(backend),
                    Models = await backend.ListModelsAsync(cancellationToken).ConfigureAwait(false),
                    DefaultModel = await GetDefaultModelAsync(backend, cancellationToken).ConfigureAwait(false)
                });
            }

            return result;
        }

        /// <summary>Resolves the backend and model that are in effect for the given settings.</summary>
        public async Task<BackendSelectionDto> ResolveSelectionAsync(CancellationToken cancellationToken)
        {
            AppSettings settings = _settings.Read();
            IReadOnlyList<BackendInfoDto> backends = await DescribeAsync(cancellationToken).ConfigureAwait(false);

            // A stored selection can point at a provider that was deleted since.
            BackendInfoDto? active = backends.FirstOrDefault(b => b.Id == settings.ActiveBackendId)
                                     ?? backends.FirstOrDefault(b => b.Id == DefaultBackendId)
                                     ?? backends.FirstOrDefault();

            string model = string.Empty;
            if (active != null)
            {
                settings.ActiveModelByBackend.TryGetValue(active.Id, out string? stored);
                model = stored != null && active.Models.Any(m => m.Id == stored) ? stored : active.DefaultModel;
            }

            return new BackendSelectionDto
            {
                Backends = backends,
                ActiveBackendId = active?.Id ?? string.Empty,
                ActiveModelId = model
            };
        }
    }
}
