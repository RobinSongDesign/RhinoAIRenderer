using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;

namespace Chameleon.App.Services
{
    /// <summary>Selecting the active backend and model, and announcing backend changes.</summary>
    public sealed class BackendService
    {
        private readonly BackendRegistry _backends;
        private readonly SettingsEditor _settings;
        private readonly EventBus _events;

        // Resolving the selection awaits the backends (model lists), so two announcements can
        // overlap, e.g. a sign-in finishing while the UI selects a backend. Serializing them keeps
        // the last published selection the newest one; otherwise a stale list could arrive last.
        private readonly SemaphoreSlim _publishGate = new SemaphoreSlim(1, 1);

        /// <summary>Creates the service.</summary>
        public BackendService(BackendRegistry backends, SettingsEditor settings, EventBus events)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<SelectBackendParams, BackendSelectionDto>("backend.select", SelectAsync);
        }

        /// <summary>Makes a backend (and optionally a model) the active one and persists the choice.</summary>
        public async Task<BackendSelectionDto> SelectAsync(SelectBackendParams parameters, CancellationToken cancellationToken)
        {
            if (_backends.Find(parameters.BackendId) == null)
            {
                throw BridgeException.NotFound("Unknown backend '" + parameters.BackendId + "'.");
            }

            _settings.Update(settings =>
            {
                settings.ActiveBackendId = parameters.BackendId;
                if (!string.IsNullOrEmpty(parameters.ModelId))
                {
                    settings.ActiveModelByBackend[parameters.BackendId] = parameters.ModelId!;
                }
            });

            return await PublishChangedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Rebuilds the backend list and tells the UI about it.</summary>
        public async Task<BackendSelectionDto> PublishChangedAsync(CancellationToken cancellationToken)
        {
            await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _backends.Invalidate();
                BackendSelectionDto selection = await _backends.ResolveSelectionAsync(cancellationToken).ConfigureAwait(false);
                _events.Publish(BridgeEvents.BackendsChanged, selection);
                return selection;
            }
            finally
            {
                _publishGate.Release();
            }
        }
    }
}
