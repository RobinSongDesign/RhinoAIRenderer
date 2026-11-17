using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Hosting;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Cloud;
using Chameleon.Core.Settings;
using Newtonsoft.Json.Linq;

namespace Chameleon.App.Services
{
    /// <summary>Application state, user settings, and the small shell helpers (log, open URL).</summary>
    public sealed class AppStateService
    {
        private readonly IRhinoHost _host;
        private readonly IAppPaths _paths;
        private readonly ChameleonCatalog _catalog;
        private readonly SettingsEditor _settings;
        private readonly BackendRegistry _backends;
        private readonly EventBus _events;
        private readonly AccountRpcService? _account;
        private readonly CloudCatalogCache? _cloudCatalog;

        /// <summary>Creates the service.</summary>
        public AppStateService(
            IRhinoHost host,
            IAppPaths paths,
            ChameleonCatalog catalog,
            SettingsEditor settings,
            BackendRegistry backends,
            EventBus events,
            AccountRpcService? account = null,
            CloudCatalogCache? cloudCatalog = null)
        {
            _account = account;
            _cloudCatalog = cloudCatalog;
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register("app.getState", GetStateAsync);
            router.RegisterRaw("settings.update", (parameters, ct) => Task.FromResult<object?>(UpdateSettings(parameters as JObject)));
            router.Register<OpenUrlParams, object?>("shell.openUrl", (p, ct) =>
            {
                OpenUrl(p.Url);
                return Task.FromResult<object?>(null);
            });
            router.Register<LogParams, object?>("log.write", (p, ct) =>
            {
                _host.Log("[ui:" + p.Level + "] " + p.Message);
                return Task.FromResult<object?>(null);
            });
        }

        /// <summary>Builds the full state the UI starts from.</summary>
        public async Task<AppStateDto> GetStateAsync(CancellationToken cancellationToken)
        {
            AppSettings settings = _settings.Read();
            BackendSelectionDto selection = await _backends.ResolveSelectionAsync(cancellationToken).ConfigureAwait(false);

            return new AppStateDto
            {
                AppVersion = _host.Info.AppVersion,
                Platform = PlatformCode(_host.Info.Platform),
                RhinoVersion = _host.Info.RhinoVersion,
                LogsDirectory = _paths.LogsDirectory,
                Language = settings.Language,
                Backends = selection.Backends,
                ActiveBackendId = selection.ActiveBackendId,
                ActiveModelId = selection.ActiveModelId,
                Catalog = CatalogFor(selection.ActiveBackendId),
                Account = _account?.Describe() ?? new AccountInfoDto(),
                Settings = UiSettingsDto.From(settings)
            };
        }

        /// <summary>
        /// Applies a partial settings update. Only properties present in <paramref name="patch"/>
        /// change; an explicit null system prompt restores the catalog default.
        /// </summary>
        public UiSettingsDto UpdateSettings(JObject? patch)
        {
            if (patch == null)
            {
                throw BridgeException.InvalidRequest("settings.update requires an object.");
            }

            bool languageChanged = false;
            AppSettings updated = _settings.Update(settings =>
            {
                if (patch.TryGetValue("language", out JToken? language) && language.Type == JTokenType.String)
                {
                    string normalized = Languages.Normalize((string?)language);
                    languageChanged = normalized != settings.Language;
                    settings.Language = normalized;
                }

                if (patch.TryGetValue("systemPrompt", out JToken? systemPrompt))
                {
                    string? text = systemPrompt.Type == JTokenType.Null ? null : (string?)systemPrompt;
                    settings.SystemPrompt = string.IsNullOrWhiteSpace(text) ? null : text;
                }

                if (patch["batch"] is JObject batch)
                {
                    string? mode = (string?)batch["mode"];
                    if (mode == BatchSettings.ModeSerial || mode == BatchSettings.ModeAnchorParallel || mode == BatchSettings.ModeIndependent)
                    {
                        settings.Batch.Mode = mode;
                    }

                    int? maxRefs = (int?)batch["maxConsistencyRefs"];
                    if (maxRefs.HasValue)
                    {
                        settings.Batch.MaxConsistencyRefs = Math.Max(0, Math.Min(16, maxRefs.Value));
                    }
                }
            });

            UiSettingsDto dto = UiSettingsDto.From(updated);
            _events.Publish(BridgeEvents.SettingsChanged, dto);
            if (languageChanged)
            {
                // Built-in provider names are localized, so the backend list depends on the language.
                _backends.Invalidate();
            }

            return dto;
        }

        // In account mode the gateway's templates and system prompt apply, so they can change without a new plug-in.
        private CatalogDto CatalogFor(string activeBackendId)
        {
            CatalogDto dto = CatalogDto.From(_catalog);
            CloudCatalog? cloud = activeBackendId == BackendIds.Cloud ? _cloudCatalog?.Current : null;
            if (cloud != null)
            {
                if (cloud.StyleTemplates.Count > 0)
                {
                    dto.StyleTemplates = cloud.StyleTemplates;
                }

                if (!string.IsNullOrEmpty(cloud.DefaultSystemPrompt))
                {
                    dto.DefaultSystemPrompt = cloud.DefaultSystemPrompt;
                }
            }

            return dto;
        }

        private void OpenUrl(string url)
        {
            // The UI can ask the OS to open things; restrict that to web pages.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw BridgeException.InvalidRequest("Only https URLs can be opened.");
            }

            _host.OpenUrl(uri.AbsoluteUri);
        }

        private static string PlatformCode(HostPlatform platform)
        {
            switch (platform)
            {
                case HostPlatform.Windows: return "win";
                case HostPlatform.MacOS: return "mac";
                default: return "dev";
            }
        }
    }
}
