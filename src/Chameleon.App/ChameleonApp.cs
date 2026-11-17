using System;
using System.IO;
using System.Net.Http;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.App.Server;
using Chameleon.App.Services;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Jobs;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.App
{
    /// <summary>
    /// The running application: every service wired together behind the local bridge server.
    /// This is the single composition root, shared by the Rhino plug-in and the development host.
    /// </summary>
    public sealed class ChameleonApp : IDisposable
    {
        private readonly LocalServer _server;
        private readonly GenerationQueue _queue;
        private readonly HttpClient _httpClient;

        private ChameleonApp(
            LocalServer server,
            GenerationQueue queue,
            HttpClient httpClient,
            RpcRouter router,
            EventBus events,
            IAssetStore assets,
            ISettingsStore settings,
            ISecretStore secrets)
        {
            _server = server;
            _queue = queue;
            _httpClient = httpClient;
            Router = router;
            Events = events;
            Assets = assets;
            Settings = settings;
            Secrets = secrets;
        }

        /// <summary>Address the WebView must load; contains the secret token.</summary>
        public string BaseUrl => _server.BaseUrl;

        /// <summary>RPC entry point; hosts may register additional methods before the UI connects.</summary>
        public RpcRouter Router { get; }

        /// <summary>Event channel to the UI.</summary>
        public EventBus Events { get; }

        /// <summary>Image storage shared with the UI.</summary>
        public IAssetStore Assets { get; }

        /// <summary>User settings.</summary>
        public ISettingsStore Settings { get; }

        /// <summary>Secret storage.</summary>
        public ISecretStore Secrets { get; }

        /// <summary>Builds every service and starts the bridge server.</summary>
        public static ChameleonApp Start(ChameleonAppOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            IRhinoHost host = options.Host;
            IAppPaths paths = options.Paths;
            IEnvironmentInfo environment = options.Environment ?? new SystemEnvironmentInfo();

            ChameleonCatalog catalog = CatalogLoader.LoadBuiltIn();
            var settingsStore = new JsonSettingsStore(paths.SettingsFile, () => AppSettingsDefaults.Create(catalog));
            ISecretStore secrets = options.SecretStore ?? SecretStoreFactory.Create(host.Info.Platform, paths, environment);

            if (paths.LegacySettingsFile != null)
            {
                LegacyMigrationResult migration = LegacySettingsMigrator.TryMigrate(paths.LegacySettingsFile, settingsStore, secrets);
                if (migration.Migrated)
                {
                    host.Log("Imported settings from the previous version (" + migration.KeysMigrated + " API key(s)).");
                }
            }

            HttpClient httpClient = DirectBackendFactory.CreateSharedHttpClient(options.HttpMessageHandler);
            var settings = new SettingsEditor(settingsStore);
            var factory = new DirectBackendFactory(AdapterRegistry.CreateDefault(), httpClient, new PromptComposer(), options.RetryPolicy);
            var backends = new BackendRegistry(catalog, settings, secrets, factory, options.ExtraBackends);
            var queue = new GenerationQueue(backends.Find);

            var assets = new DiskAssetStore(Path.Combine(paths.CacheDirectory, "assets"));
            assets.PurgeStale(DiskAssetStore.DefaultMaxAge);

            var events = new EventBus(host.Log);
            var router = new RpcRouter(host.Log);

            var backendService = new BackendService(backends, settings, events);
            new AppStateService(host, catalog, settings, backends, events).Register(router);
            backendService.Register(router);
            new ProviderService(catalog, settings, secrets, backends, backendService, factory).Register(router);
            new CaptureService(host, assets).Register(router);
            new JobService(queue, backends, assets, settings, catalog, events).Register(router);
            new FileService(host, assets).Register(router);

            var server = new LocalServer(new LocalServerOptions(options.StaticFiles, router, events, assets)
            {
                Port = options.Port,
                Token = options.Token,
                AllowedDevOrigin = options.AllowedDevOrigin,
                Log = host.Log
            });
            server.Start();

            return new ChameleonApp(server, queue, httpClient, router, events, assets, settingsStore, secrets);
        }

        /// <summary>Stops the server, cancels running jobs and releases network resources.</summary>
        public void Dispose()
        {
            _server.Dispose();
            _queue.Dispose();
            _httpClient.Dispose();
        }
    }
}
